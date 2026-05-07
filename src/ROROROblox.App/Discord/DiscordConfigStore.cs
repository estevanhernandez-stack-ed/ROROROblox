using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ROROROblox.Core.Discord;

namespace ROROROblox.App.Discord;

/// <summary>
/// JSON-backed store for Discord clan-coordination settings. NOT DPAPI-encrypted — webhook URL
/// is a clan-shared resource, not a per-user secret (spec §11 decision). Atomic write via .tmp
/// + File.Replace. Missing or malformed JSON degrades to safe defaults; the corrupt file is
/// preserved as discord-config.json.corrupt-{yyyyMMdd-HHmmss} so future debugging has the
/// failing payload to look at.
///
/// FileSystemWatcher fires <see cref="Changed"/> when the file is written by either us or
/// (via "edit the JSON in Notepad while the app runs") an external process. Consumers
/// re-read state on Changed; we do not try to suppress self-fired events because consumers
/// already need to handle no-op re-reads idempotently.
/// </summary>
public sealed class DiscordConfigStore : IDiscordConfig, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private readonly string _filePath;
    private readonly ILogger<DiscordConfigStore> _log;
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly FileSystemWatcher? _watcher;

    private DiscordConfigSnapshot _current;
    private bool _disposed;

    public DiscordConfigStore(ILogger<DiscordConfigStore> log) : this(DefaultPath(), log) { }

    public DiscordConfigStore(string filePath, ILogger<DiscordConfigStore> log)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path must not be empty.", nameof(filePath));
        }
        _filePath = filePath;
        _log = log;

        // Load initial state synchronously so property reads (RichPresenceEnabled etc.) return
        // real values immediately. The file is small (<1 KiB) so the cost is negligible.
        _current = LoadSyncSafe();

        // FileSystemWatcher requires the directory to exist. Best-effort create — if we can't
        // (permission issues, drive missing), log and skip the watcher; SaveAsync will retry.
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                _watcher = new FileSystemWatcher(dir, Path.GetFileName(_filePath))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };
                _watcher.Changed += OnFileChanged;
                _watcher.Created += OnFileChanged;
                _watcher.Renamed += OnFileChanged;
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "FileSystemWatcher init failed for {Path}; live-reload disabled.", _filePath);
        }
    }

    public static string DefaultPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "ROROROblox", "discord-config.json");
    }

    public bool RichPresenceEnabled
    {
        get { lock (_stateGate) return _current.RichPresenceEnabled; }
    }

    public string? WebhookUrl
    {
        get { lock (_stateGate) return _current.WebhookUrl; }
    }

    public DiscordWebhookEvents WebhookEvents
    {
        get { lock (_stateGate) return _current.WebhookEvents; }
    }

    public event EventHandler? Changed;

    public async Task SaveAsync(DiscordConfigSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        var watcherWasEnabled = false;
        try
        {
            // Suppress the watcher during our own write. Two reasons:
            //   1. File.Replace needs exclusive access to dst; the watcher's OnFileChanged
            //      reads dst, which can race the Replace and IOException it.
            //   2. SaveAsync already raises Changed itself; without suppression the watcher
            //      would fire a second redundant event for the same write.
            if (_watcher is not null)
            {
                watcherWasEnabled = _watcher.EnableRaisingEvents;
                _watcher.EnableRaisingEvents = false;
            }

            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.SerializeToUtf8Bytes(snapshot, SerializerOptions);
            var tempPath = _filePath + ".tmp";
            await File.WriteAllBytesAsync(tempPath, json, ct).ConfigureAwait(false);

            // File.Replace requires destination to exist; first-time writes use Move(overwrite).
            // Both are atomic on NTFS for same-volume operations.
            if (File.Exists(_filePath))
            {
                File.Replace(tempPath, _filePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, _filePath);
            }

            lock (_stateGate)
            {
                _current = snapshot;
            }
            RaiseChanged();
        }
        finally
        {
            if (_watcher is not null && watcherWasEnabled)
            {
                try { _watcher.EnableRaisingEvents = true; } catch { /* watcher disposed mid-write */ }
            }
            _writeGate.Release();
        }
    }

    private DiscordConfigSnapshot LoadSyncSafe()
    {
        var defaults = new DiscordConfigSnapshot(false, null, DiscordWebhookEvents.AllOff);

        if (!File.Exists(_filePath))
        {
            return defaults;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(_filePath);
        }
        catch (IOException ex)
        {
            _log.LogWarning(ex, "Couldn't read {Path}; using defaults.", _filePath);
            return defaults;
        }

        if (bytes.Length == 0)
        {
            return defaults;
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<DiscordConfigSnapshot>(bytes, SerializerOptions);
            return snapshot ?? defaults;
        }
        catch (JsonException ex)
        {
            // Malformed payload — preserve for debugging, don't lose user data.
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var corruptPath = $"{_filePath}.corrupt-{stamp}";
            try
            {
                File.Move(_filePath, corruptPath);
                _log.LogWarning(ex, "discord-config.json was malformed; preserved as {Corrupt} and reset to defaults.", corruptPath);
            }
            catch (Exception moveEx)
            {
                _log.LogWarning(moveEx, "discord-config.json was malformed and the corrupt-copy preservation failed; defaults loaded.");
            }
            return defaults;
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // The watcher fires on a thread-pool thread. Re-read + raise Changed synchronously here;
        // the file is tiny and consumers expect to do their own marshaling.
        try
        {
            var snapshot = LoadSyncSafe();
            lock (_stateGate)
            {
                _current = snapshot;
            }
            RaiseChanged();
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "FileSystemWatcher reload threw; ignoring.");
        }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_watcher is not null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Changed -= OnFileChanged;
                _watcher.Created -= OnFileChanged;
                _watcher.Renamed -= OnFileChanged;
                _watcher.Dispose();
            }
        }
        catch
        {
            // Dispose must not throw.
        }
        _writeGate.Dispose();
    }
}
