using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ROROROblox.Core;

/// <summary>
/// JSON-file-backed implementation of <see cref="IPrivateServerStore"/>. Plaintext (these are
/// soft-credential bookmarks, not passwords). Atomic write via tmp-file + rename;
/// thread-safe via <see cref="SemaphoreSlim"/>. Same shape pattern as
/// <see cref="FavoriteGameStore"/>.
/// </summary>
public sealed class PrivateServerStore : IPrivateServerStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);
    private bool _disposed;

    public PrivateServerStore() : this(DefaultPath()) { }

    public PrivateServerStore(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path must not be empty.", nameof(filePath));
        }
        _filePath = filePath;
    }

    public static string DefaultPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "ROROROblox", "private-servers.json");
    }

    public async Task<IReadOnlyList<SavedPrivateServer>> ListAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            return blob.Servers.ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SavedPrivateServer?> GetAsync(Guid id)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            return blob.Servers.FirstOrDefault(s => s.Id == id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SavedPrivateServer> AddAsync(
        long placeId,
        string code,
        PrivateServerCodeKind codeKind,
        string name,
        string placeName,
        string thumbnailUrl)
    {
        if (placeId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(placeId), "placeId must be positive.");
        }
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Code must not be empty.", nameof(code));
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name must not be empty.", nameof(name));
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);

            // Replace by (placeId, code) pair — preserves Id + AddedAt for the same server even
            // if the user re-pastes the same share URL with a different display name.
            var existingIndex = blob.Servers.FindIndex(s =>
                s.PlaceId == placeId && string.Equals(s.Code, code, StringComparison.Ordinal));

            // LocalName survives re-add (spec §8) — preserve the existing local nickname
            // when the user's add path runs against an already-saved (placeId, code) pair.
            var preservedLocalName = existingIndex >= 0 ? blob.Servers[existingIndex].LocalName : null;

            var record = new SavedPrivateServer(
                Id: existingIndex >= 0 ? blob.Servers[existingIndex].Id : Guid.NewGuid(),
                PlaceId: placeId,
                Code: code,
                CodeKind: codeKind,
                Name: name,
                PlaceName: placeName ?? string.Empty,
                ThumbnailUrl: thumbnailUrl ?? string.Empty,
                AddedAt: existingIndex >= 0 ? blob.Servers[existingIndex].AddedAt : DateTimeOffset.UtcNow,
                LastLaunchedAt: existingIndex >= 0 ? blob.Servers[existingIndex].LastLaunchedAt : null,
                LocalName: preservedLocalName);

            if (existingIndex >= 0)
            {
                blob.Servers[existingIndex] = record;
            }
            else
            {
                blob.Servers.Add(record);
            }

            await SaveAsync(blob).ConfigureAwait(false);
            return record;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(Guid id)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var idx = blob.Servers.FindIndex(s => s.Id == id);
            if (idx < 0)
            {
                return;
            }
            blob.Servers.RemoveAt(idx);
            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateLocalNameAsync(Guid serverId, string? localName)
    {
        var normalized = string.IsNullOrWhiteSpace(localName) ? null : localName.Trim();

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var idx = blob.Servers.FindIndex(s => s.Id == serverId);
            if (idx < 0)
            {
                throw new KeyNotFoundException($"Private server {serverId} not found.");
            }
            if (string.Equals(blob.Servers[idx].LocalName, normalized, StringComparison.Ordinal))
            {
                return; // no-op write avoidance
            }
            blob.Servers[idx] = blob.Servers[idx] with { LocalName = normalized };
            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task TouchLastLaunchedAsync(Guid id)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var idx = blob.Servers.FindIndex(s => s.Id == id);
            if (idx < 0)
            {
                return;
            }
            blob.Servers[idx] = blob.Servers[idx] with { LastLaunchedAt = DateTimeOffset.UtcNow };
            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<PrivateServersBlob> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new PrivateServersBlob(Version: 1, Servers: []);
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(_filePath).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return new PrivateServersBlob(Version: 1, Servers: []);
        }

        if (bytes.Length == 0)
        {
            return new PrivateServersBlob(Version: 1, Servers: []);
        }

        try
        {
            // Deserialize through a tolerant on-disk shape that accepts BOTH the legacy
            // accessCode field (pre-link/access discriminator) and the new code+codeKind pair.
            // Pre-discriminator records always wrote what the user pasted into accessCode, but
            // users mostly pasted share URLs (linkCode), so legacy values default to LinkCode.
            var stored = JsonSerializer.Deserialize<StoredBlob>(bytes, JsonOptions);
            if (stored is null)
            {
                return new PrivateServersBlob(Version: 1, Servers: []);
            }

            var migrated = new List<SavedPrivateServer>(stored.Servers.Count);
            foreach (var s in stored.Servers)
            {
                var code = !string.IsNullOrEmpty(s.Code) ? s.Code : s.AccessCode ?? string.Empty;
                if (string.IsNullOrEmpty(code))
                {
                    continue; // skip rows we can't reconstruct
                }
                var kind = s.CodeKind ?? SavedPrivateServer.DefaultLegacyKind;
                migrated.Add(new SavedPrivateServer(
                    Id: s.Id == Guid.Empty ? Guid.NewGuid() : s.Id,
                    PlaceId: s.PlaceId,
                    Code: code,
                    CodeKind: kind,
                    Name: s.Name ?? string.Empty,
                    PlaceName: s.PlaceName ?? string.Empty,
                    ThumbnailUrl: s.ThumbnailUrl ?? string.Empty,
                    AddedAt: s.AddedAt,
                    LastLaunchedAt: s.LastLaunchedAt,
                    LocalName: s.LocalName));
            }
            return new PrivateServersBlob(Version: 1, Servers: migrated);
        }
        catch (JsonException)
        {
            // Corrupt file -> empty list. User can re-add.
            return new PrivateServersBlob(Version: 1, Servers: []);
        }
    }

    private async Task SaveAsync(PrivateServersBlob blob)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(blob, JsonOptions);
        var tempPath = _filePath + ".tmp";
        await File.WriteAllBytesAsync(tempPath, bytes).ConfigureAwait(false);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _gate.Dispose();
    }

    private sealed record PrivateServersBlob(int Version, List<SavedPrivateServer> Servers);

    // On-disk-tolerant shape: accepts both the legacy accessCode field and the new code +
    // codeKind pair. Used only by Load; SaveAsync writes the canonical SavedPrivateServer
    // shape. System.Text.Json fills missing optional fields with defaults.
    private sealed record StoredBlob(int Version, List<StoredServer> Servers);

    private sealed record StoredServer(
        Guid Id,
        long PlaceId,
        string? Code,
        PrivateServerCodeKind? CodeKind,
        string? AccessCode, // legacy
        string? Name,
        string? PlaceName,
        string? ThumbnailUrl,
        DateTimeOffset AddedAt,
        DateTimeOffset? LastLaunchedAt,
        string? LocalName = null);
}
