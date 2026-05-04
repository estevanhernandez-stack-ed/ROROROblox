using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ROROROblox.Core;

/// <summary>
/// JSON-file-backed session history. Same shape pattern as <see cref="FavoriteGameStore"/> /
/// <see cref="PrivateServerStore"/> — atomic write, semaphore-gated, corrupt file falls back
/// to empty. Capped at <see cref="MaxRows"/> entries on every write.
/// </summary>
public sealed class SessionHistoryStore : ISessionHistoryStore, IDisposable
{
    public const int MaxRows = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);
    private bool _disposed;

    public SessionHistoryStore() : this(DefaultPath()) { }

    public SessionHistoryStore(string filePath)
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
        return Path.Combine(localAppData, "ROROROblox", "session-history.json");
    }

    public async Task<IReadOnlyList<LaunchSession>> ListAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            // Newest first — tail of the file is freshest, but explicit sort defends against
            // out-of-order writes (e.g., MarkEnded updating an old row).
            return blob.Sessions
                .OrderByDescending(s => s.LaunchedAtUtc)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddAsync(LaunchSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            blob.Sessions.Add(session);
            // Cap on every write so the file never grows unbounded.
            if (blob.Sessions.Count > MaxRows)
            {
                blob.Sessions.RemoveAll(_ => false); // no-op to satisfy the type
                blob.Sessions = blob.Sessions
                    .OrderByDescending(s => s.LaunchedAtUtc)
                    .Take(MaxRows)
                    .ToList();
            }
            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkEndedAsync(Guid sessionId, DateTimeOffset endedAtUtc, string? outcomeHint = null)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var idx = blob.Sessions.FindIndex(s => s.Id == sessionId);
            if (idx < 0)
            {
                return; // pruned already; harmless.
            }
            blob.Sessions[idx] = blob.Sessions[idx] with
            {
                EndedAtUtc = endedAtUtc,
                OutcomeHint = outcomeHint ?? blob.Sessions[idx].OutcomeHint,
            };
            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await SaveAsync(new SessionsBlob(Version: 1, Sessions: [])).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SessionsBlob> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new SessionsBlob(Version: 1, Sessions: []);
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(_filePath).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return new SessionsBlob(Version: 1, Sessions: []);
        }

        if (bytes.Length == 0)
        {
            return new SessionsBlob(Version: 1, Sessions: []);
        }

        try
        {
            var blob = JsonSerializer.Deserialize<SessionsBlob>(bytes, JsonOptions);
            return blob ?? new SessionsBlob(Version: 1, Sessions: []);
        }
        catch (JsonException)
        {
            return new SessionsBlob(Version: 1, Sessions: []);
        }
    }

    private async Task SaveAsync(SessionsBlob blob)
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

    private sealed record SessionsBlob(int Version, List<LaunchSession> Sessions)
    {
        // List<T> is read-write; the record's positional init is fine. We assign back via 'with'
        // when capping in AddAsync.
        public List<LaunchSession> Sessions { get; set; } = Sessions;
    }
}
