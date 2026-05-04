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
        string accessCode,
        string name,
        string placeName,
        string thumbnailUrl)
    {
        if (placeId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(placeId), "placeId must be positive.");
        }
        if (string.IsNullOrWhiteSpace(accessCode))
        {
            throw new ArgumentException("Access code must not be empty.", nameof(accessCode));
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name must not be empty.", nameof(name));
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);

            // Replace by (placeId, accessCode) pair — preserves Id + AddedAt for the same server.
            var existingIndex = blob.Servers.FindIndex(s =>
                s.PlaceId == placeId && string.Equals(s.AccessCode, accessCode, StringComparison.Ordinal));

            var record = new SavedPrivateServer(
                Id: existingIndex >= 0 ? blob.Servers[existingIndex].Id : Guid.NewGuid(),
                PlaceId: placeId,
                AccessCode: accessCode,
                Name: name,
                PlaceName: placeName ?? string.Empty,
                ThumbnailUrl: thumbnailUrl ?? string.Empty,
                AddedAt: existingIndex >= 0 ? blob.Servers[existingIndex].AddedAt : DateTimeOffset.UtcNow,
                LastLaunchedAt: existingIndex >= 0 ? blob.Servers[existingIndex].LastLaunchedAt : null);

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
            return JsonSerializer.Deserialize<PrivateServersBlob>(bytes, JsonOptions)
                ?? new PrivateServersBlob(Version: 1, Servers: []);
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
}
