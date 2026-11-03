using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ROROROblox.Core;

/// <summary>
/// File-backed <see cref="IGameIconCache"/>: <c>game-icons.json</c> beside the other stores
/// (placeId to name, universe id, icon url, fetched-at). Plaintext, non-secret, safe to delete.
/// <para>
/// The file is read once, lazily; after that the in-memory map is the truth, so deleting the file
/// while the app runs loses nothing: the next resolved icon rewrites every known row. A missing,
/// empty or corrupt file reads as an empty cache and never throws. Writes go tmp-then-move.
/// </para>
/// <para>
/// The lookup is <see cref="IRobloxApi.GetGameMetadataByPlaceIdAsync"/>, a public no-cookie call.
/// </para>
/// </summary>
public sealed class GameIconCache : IGameIconCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IRobloxApi _api;
    private readonly string _filePath;
    private readonly TimeProvider _time;
    private readonly ILogger<GameIconCache> _log;
    private readonly object _gate = new();
    private readonly HashSet<long> _attempted = [];
    private Dictionary<long, GameIconInfo>? _rows;
    private Func<long, bool>? _enqueue;

    public GameIconCache(IRobloxApi api, string? filePath = null, TimeProvider? time = null, ILogger<GameIconCache>? log = null)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _filePath = string.IsNullOrWhiteSpace(filePath) ? DefaultPath() : filePath;
        _time = time ?? TimeProvider.System;
        _log = log ?? NullLogger<GameIconCache>.Instance;
    }

    public static string DefaultPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "ROROROblox", "game-icons.json");
    }

    public event EventHandler<GameIconResolvedEventArgs>? IconResolved;

    public GameIconInfo? TryGet(long placeId)
    {
        lock (_gate)
        {
            return Rows().TryGetValue(placeId, out var info) ? info : null;
        }
    }

    public void AttachQueue(Func<long, bool> enqueue)
    {
        lock (_gate)
        {
            _enqueue = enqueue ?? throw new ArgumentNullException(nameof(enqueue));
        }
    }

    public bool Request(long placeId)
    {
        if (placeId <= 0)
        {
            return false;
        }

        Func<long, bool>? enqueue;
        lock (_gate)
        {
            if (Rows().ContainsKey(placeId) || _enqueue is null || !_attempted.Add(placeId))
            {
                return false;
            }
            enqueue = _enqueue;
        }

        if (enqueue(placeId))
        {
            return true;
        }
        lock (_gate)
        {
            _attempted.Remove(placeId); // the queue refused (full or disposed): a later ask may try again.
        }
        return false;
    }

    public async Task<bool> ResolveAsync(long placeId, CancellationToken ct = default)
    {
        if (placeId <= 0)
        {
            return false;
        }

        GameMetadata? metadata;
        try
        {
            metadata = await _api.GetGameMetadataByPlaceIdAsync(placeId).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogDebug(ex, "Icon lookup for place {PlaceId} threw; the entry keeps its placeholder.", placeId);
            return true; // a request was made.
        }
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.IconUrl))
        {
            return true;
        }

        var info = new GameIconInfo(metadata.Name, metadata.UniverseId, metadata.IconUrl, _time.GetUtcNow());
        lock (_gate)
        {
            Rows()[placeId] = info;
            Save();
        }

        try
        {
            IconResolved?.Invoke(this, new GameIconResolvedEventArgs(placeId, info));
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "An IconResolved subscriber threw for place {PlaceId}; continuing.", placeId);
        }
        return true;
    }

    // ---- file ----

    /// <summary>Caller holds <see cref="_gate"/>.</summary>
    private Dictionary<long, GameIconInfo> Rows() => _rows ??= Load();

    private Dictionary<long, GameIconInfo> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }
            var bytes = File.ReadAllBytes(_filePath);
            if (bytes.Length == 0)
            {
                return [];
            }
            var blob = JsonSerializer.Deserialize<Blob>(bytes, JsonOptions);
            return blob?.Games is { } games ? new Dictionary<long, GameIconInfo>(games) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _log.LogDebug(ex, "game-icons.json could not be read; starting with an empty icon cache.");
            return [];
        }
    }

    /// <summary>Caller holds <see cref="_gate"/>. Best effort: a failed write keeps the in-memory rows.</summary>
    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new Blob(1, Rows()), JsonOptions);
            var tempPath = _filePath + ".tmp";
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogDebug(ex, "game-icons.json could not be written; the icons stay in memory this run.");
        }
    }

    private sealed record Blob(int Version, Dictionary<long, GameIconInfo> Games);
}
