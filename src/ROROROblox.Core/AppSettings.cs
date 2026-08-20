using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ROROROblox.Core;

/// <summary>
/// JSON-file-backed implementation of <see cref="IAppSettings"/>. File lives at
/// <c>%LOCALAPPDATA%\ROROROblox\settings.json</c>. Plaintext (no DPAPI) — these are
/// preferences, not secrets. Atomic write via tmp-file + rename. Thread-safe via
/// <see cref="SemaphoreSlim"/>.
/// </summary>
public sealed class AppSettings : IAppSettings, IDisposable
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

    public AppSettings() : this(DefaultPath()) { }

    public AppSettings(string filePath)
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
        return Path.Combine(localAppData, "ROROROblox", "settings.json");
    }

    public async Task<string?> GetDefaultPlaceUrlAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var settings = await LoadAsync().ConfigureAwait(false);
            return string.IsNullOrEmpty(settings.DefaultPlaceUrl) ? null : settings.DefaultPlaceUrl;
        }
        finally
        {
            _gate.Release();
        }
    }

    // SetDefaultPlaceUrlAsync was deleted by v1.21 item 9 (F-093). Nothing in the app called it and
    // IAppSettings documented a Preferences control for it that has never existed. The GETTER stays
    // so a legacy settings.json still deserializes and SaveAsync round-trips the value instead of
    // dropping it — DefaultPlaceUrl is still a SettingsBlob property, so the next write preserves
    // whatever an old file carried. See IAppSettings.GetDefaultPlaceUrlAsync for why deleting the
    // field entirely is a smaller change than this cycle's spec assumed.

    public async Task<bool> GetLaunchMainOnStartupAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var settings = await LoadAsync().ConfigureAwait(false);
            return settings.LaunchMainOnStartup;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetLaunchMainOnStartupAsync(bool enabled)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var settings = await LoadAsync().ConfigureAwait(false);
            settings = settings with { LaunchMainOnStartup = enabled };
            await SaveAsync(settings).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> GetActiveThemeIdAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var settings = await LoadAsync().ConfigureAwait(false);
            return settings.ActiveThemeId;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetActiveThemeIdAsync(string themeId)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var settings = await LoadAsync().ConfigureAwait(false);
            settings = settings with { ActiveThemeId = themeId };
            await SaveAsync(settings).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool?> GetEdgeRemediationAnswerAsync(string themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId)) return null;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var answers = (await LoadAsync().ConfigureAwait(false)).EdgeRemediationAnswers;
            // OrdinalIgnoreCase, not Ordinal: theme ids come from FILENAMES on a case-insensitive
            // filesystem, and ThemeStore.GetByIdAsync matches them case-insensitively too. Matching
            // Ordinal here meant renaming "Neon Dusk.json" to "neon dusk.json" silently lost a
            // recorded decline and repainted the theme. Found by the wave-5 review gate.
            return answers is not null && Lookup(answers).TryGetValue(themeId, out var answered) ? answered : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetEdgeRemediationAnswerAsync(string themeId, bool accepted)
    {
        if (string.IsNullOrWhiteSpace(themeId))
        {
            throw new ArgumentException("Theme id must not be empty.", nameof(themeId));
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var settings = await LoadAsync().ConfigureAwait(false);
            // Copy rather than mutate: `with` shares the dictionary reference with the blob we
            // just read, so mutating in place would edit a value another caller may still hold.
            var answers = settings.EdgeRemediationAnswers is null
                ? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                : Lookup(settings.EdgeRemediationAnswers);
            answers[themeId] = accepted;
            await SaveAsync(settings with { EdgeRemediationAnswers = answers }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> GetBloxstrapWarningDismissedAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var settings = await LoadAsync().ConfigureAwait(false);
            return settings.BloxstrapWarningDismissed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetBloxstrapWarningDismissedAsync(bool value)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var settings = await LoadAsync().ConfigureAwait(false);
            settings = settings with { BloxstrapWarningDismissed = value };
            await SaveAsync(settings).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> GetDismissedFpsCapWarningSignatureAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return (await LoadAsync().ConfigureAwait(false)).DismissedFpsCapWarningSignature; }
        finally { _gate.Release(); }
    }

    public async Task SetDismissedFpsCapWarningSignatureAsync(string? signature)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var s = await LoadAsync().ConfigureAwait(false);
            await SaveAsync(s with { DismissedFpsCapWarningSignature = signature }).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> GetMuteIdleAlertsAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return (await LoadAsync().ConfigureAwait(false)).MuteIdleAlerts; }
        finally { _gate.Release(); }
    }

    public async Task SetMuteIdleAlertsAsync(bool muted)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var s = await LoadAsync().ConfigureAwait(false);
            await SaveAsync(s with { MuteIdleAlerts = muted }).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<int> GetIdleWarnThresholdMinutesAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var mins = (await LoadAsync().ConfigureAwait(false)).IdleWarnThresholdMinutes;
            return mins <= 0 ? 15 : mins;   // guard against a bad stored value
        }
        finally { _gate.Release(); }
    }

    public async Task SetIdleWarnThresholdMinutesAsync(int minutes)
    {
        if (minutes <= 0) minutes = 15;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var s = await LoadAsync().ConfigureAwait(false);
            await SaveAsync(s with { IdleWarnThresholdMinutes = minutes }).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> GetCarefulSquadLaunchAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return (await LoadAsync().ConfigureAwait(false)).CarefulSquadLaunch; }
        finally { _gate.Release(); }
    }

    public async Task SetCarefulSquadLaunchAsync(bool careful)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var s = await LoadAsync().ConfigureAwait(false);
            await SaveAsync(s with { CarefulSquadLaunch = careful }).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> GetAlwaysShowRecycleAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return (await LoadAsync().ConfigureAwait(false)).AlwaysShowRecycle; }
        finally { _gate.Release(); }
    }

    public async Task SetAlwaysShowRecycleAsync(bool always)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var s = await LoadAsync().ConfigureAwait(false);
            await SaveAsync(s with { AlwaysShowRecycle = always }).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> GetAutoForceStopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return (await LoadAsync().ConfigureAwait(false)).AutoForceStop; }
        finally { _gate.Release(); }
    }

    public async Task SetAutoForceStopAsync(bool auto)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var s = await LoadAsync().ConfigureAwait(false);
            await SaveAsync(s with { AutoForceStop = auto }).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> GetStreamerModeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return (await LoadAsync().ConfigureAwait(false)).StreamerMode; }
        finally { _gate.Release(); }
    }

    public async Task SetStreamerModeAsync(bool enabled)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var s = await LoadAsync().ConfigureAwait(false);
            await SaveAsync(s with { StreamerMode = enabled }).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> GetMemoryWatchdogEnabledAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return (await LoadAsync().ConfigureAwait(false)).MemoryWatchdogEnabled; }
        finally { _gate.Release(); }
    }

    public async Task SetMemoryWatchdogEnabledAsync(bool enabled)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var s = await LoadAsync().ConfigureAwait(false);
            await SaveAsync(s with { MemoryWatchdogEnabled = enabled }).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<int?> GetMemoryReserveMbAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return (await LoadAsync().ConfigureAwait(false)).MemoryReserveMb; }
        finally { _gate.Release(); }
    }

    public async Task SetMemoryReserveMbAsync(int? reserveMb)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var s = await LoadAsync().ConfigureAwait(false);
            await SaveAsync(s with { MemoryReserveMb = reserveMb }).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<int?> GetMemoryCapMbAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return (await LoadAsync().ConfigureAwait(false)).MemoryCapMb; }
        finally { _gate.Release(); }
    }

    public async Task SetMemoryCapMbAsync(int? capMb)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var s = await LoadAsync().ConfigureAwait(false);
            await SaveAsync(s with { MemoryCapMb = capMb }).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<int> GetProjectionWarnMinutesAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return (await LoadAsync().ConfigureAwait(false)).ProjectionWarnMinutes; }
        finally { _gate.Release(); }
    }

    public async Task SetProjectionWarnMinutesAsync(int minutes)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var s = await LoadAsync().ConfigureAwait(false);
            await SaveAsync(s with { ProjectionWarnMinutes = minutes }).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> GetCompactModeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return (await LoadAsync().ConfigureAwait(false)).CompactMode; }
        finally { _gate.Release(); }
    }

    public async Task SetCompactModeAsync(bool compact)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var s = await LoadAsync().ConfigureAwait(false);
            await SaveAsync(s with { CompactMode = compact }).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Re-keys the deserialized answers case-insensitively. System.Text.Json always hands back an
    /// Ordinal dictionary regardless of what was written, so the comparer has to be re-applied on
    /// every read rather than assumed to have survived the round trip.
    /// </summary>
    private static Dictionary<string, bool> Lookup(Dictionary<string, bool> answers) =>
        new(answers, StringComparer.OrdinalIgnoreCase);

    private async Task<SettingsBlob> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new SettingsBlob(Version: 1, DefaultPlaceUrl: null, LaunchMainOnStartup: false, ActiveThemeId: null);
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(_filePath).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return new SettingsBlob(Version: 1, DefaultPlaceUrl: null, LaunchMainOnStartup: false, ActiveThemeId: null);
        }

        if (bytes.Length == 0)
        {
            return new SettingsBlob(Version: 1, DefaultPlaceUrl: null, LaunchMainOnStartup: false, ActiveThemeId: null);
        }

        try
        {
            return JsonSerializer.Deserialize<SettingsBlob>(bytes, JsonOptions)
                ?? new SettingsBlob(Version: 1, DefaultPlaceUrl: null);
        }
        catch (JsonException)
        {
            // Corrupt settings file — return defaults rather than fail. The user can re-set in
            // the settings UI; we log nothing because we have no logger surface in Core yet.
            return new SettingsBlob(Version: 1, DefaultPlaceUrl: null, LaunchMainOnStartup: false, ActiveThemeId: null);
        }
    }

    private async Task SaveAsync(SettingsBlob blob)
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

    // SettingsBlob: missing fields decode as defaults (System.Text.Json), so older v1 blobs
    // without LaunchMainOnStartup, BloxstrapWarningDismissed, MuteIdleAlerts,
    // IdleWarnThresholdMinutes, CarefulSquadLaunch, StreamerMode, MemoryWatchdogEnabled,
    // MemoryReserveMb, MemoryCapMb, ProjectionWarnMinutes, CompactMode, or
    // DismissedFpsCapWarningSignature
    // load cleanly with those fields at their defaults — no migration step. MemoryReserveMb/
    // MemoryCapMb default to null ("never set" — the composition root derives from installed
    // RAM); they are NOT sentinel-zero because 0 is a real, distinct user choice for MemoryCapMb
    // (disable the cap trigger). DismissedFpsCapWarningSignature defaults to null ("nothing
    // dismissed yet") for the same reason — an empty string would be ambiguous with a real
    // (if degenerate) signature. EdgeRemediationAnswers defaults to null ("nobody has been asked
    // about any theme"), which is what every settings.json written before v1.16 looks like — the
    // absent key is exactly the state the remediation prompt is designed to handle.
    private sealed record SettingsBlob(
        int Version,
        string? DefaultPlaceUrl,
        bool LaunchMainOnStartup = false,
        string? ActiveThemeId = null,
        bool BloxstrapWarningDismissed = false,
        bool MuteIdleAlerts = false,
        int IdleWarnThresholdMinutes = 15,
        bool CarefulSquadLaunch = false,
        bool StreamerMode = false,
        bool MemoryWatchdogEnabled = true,
        int? MemoryReserveMb = null,
        int? MemoryCapMb = null,
        int ProjectionWarnMinutes = 120,
        string? DismissedFpsCapWarningSignature = null,
        bool AlwaysShowRecycle = false,
        bool CompactMode = false,
        bool AutoForceStop = false,
        Dictionary<string, bool>? EdgeRemediationAnswers = null);
}
