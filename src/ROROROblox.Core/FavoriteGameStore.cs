using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ROROROblox.Core;

/// <summary>
/// JSON-file-backed implementation of <see cref="IFavoriteGameStore"/>. Plaintext (these are
/// preferences, not secrets). Atomic write via tmp-file + rename; thread-safe via
/// <see cref="SemaphoreSlim"/>. Same shape pattern as <see cref="AppSettings"/>.
/// </summary>
public sealed class FavoriteGameStore : IFavoriteGameStore, IDisposable
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

    public FavoriteGameStore() : this(DefaultPath()) { }

    public FavoriteGameStore(string filePath)
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
        return Path.Combine(localAppData, "ROROROblox", "favorites.json");
    }

    public async Task<IReadOnlyList<FavoriteGame>> ListAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            return blob.Favorites.ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FavoriteGame?> GetDefaultAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            return blob.Favorites.FirstOrDefault(f => f.IsDefault);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FavoriteGame> AddAsync(long placeId, long universeId, string name, string thumbnailUrl)
    {
        if (placeId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(placeId), "placeId must be positive.");
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name must not be empty.", nameof(name));
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);

            // Replace if already exists, preserving IsDefault flag.
            var existingIndex = blob.Favorites.FindIndex(f => f.PlaceId == placeId);
            var isDefault = blob.Favorites.Count == 0
                || (existingIndex >= 0 && blob.Favorites[existingIndex].IsDefault);

            var updated = new FavoriteGame(
                PlaceId: placeId,
                UniverseId: universeId,
                Name: name,
                ThumbnailUrl: thumbnailUrl ?? string.Empty,
                IsDefault: isDefault,
                AddedAt: existingIndex >= 0 ? blob.Favorites[existingIndex].AddedAt : DateTimeOffset.UtcNow);

            if (existingIndex >= 0)
            {
                blob.Favorites[existingIndex] = updated;
            }
            else
            {
                blob.Favorites.Add(updated);
            }

            await SaveAsync(blob).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(long placeId)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var index = blob.Favorites.FindIndex(f => f.PlaceId == placeId);
            if (index < 0)
            {
                return;
            }

            var wasDefault = blob.Favorites[index].IsDefault;
            blob.Favorites.RemoveAt(index);

            // If the default was just removed and there are others, promote the first remaining.
            if (wasDefault && blob.Favorites.Count > 0)
            {
                blob.Favorites[0] = blob.Favorites[0] with { IsDefault = true };
            }

            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetDefaultAsync(long placeId)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            if (!blob.Favorites.Any(f => f.PlaceId == placeId))
            {
                throw new KeyNotFoundException($"Favorite with placeId {placeId} not found.");
            }

            for (var i = 0; i < blob.Favorites.Count; i++)
            {
                blob.Favorites[i] = blob.Favorites[i] with { IsDefault = blob.Favorites[i].PlaceId == placeId };
            }

            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FavoritesBlob> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new FavoritesBlob(Version: 1, Favorites: []);
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(_filePath).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return new FavoritesBlob(Version: 1, Favorites: []);
        }

        if (bytes.Length == 0)
        {
            return new FavoritesBlob(Version: 1, Favorites: []);
        }

        try
        {
            return JsonSerializer.Deserialize<FavoritesBlob>(bytes, JsonOptions)
                ?? new FavoritesBlob(Version: 1, Favorites: []);
        }
        catch (JsonException)
        {
            // Corrupt favorites file -- return empty rather than fail. User can re-add.
            return new FavoritesBlob(Version: 1, Favorites: []);
        }
    }

    private async Task SaveAsync(FavoritesBlob blob)
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

    private sealed record FavoritesBlob(int Version, List<FavoriteGame> Favorites);
}
