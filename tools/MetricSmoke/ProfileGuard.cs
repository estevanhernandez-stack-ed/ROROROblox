using System.IO;
using System.Text.Json;
using ROROROblox.App.Plugins;
using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.MetricSmoke;

/// <summary>
/// Saves the four profile files the metric-alert smoke harness has to touch, and gives them back.
/// <para>
/// This runs against the user's REAL <c>%LOCALAPPDATA%\ROROROblox</c>, because the app has no
/// data-root seam (design 2026-09-11, §2: adding one is a bigger, riskier change than the harness it
/// would enable). So the guard is the thing that decides whether the harness is ever run twice. Its
/// failure mode is a <c>discord.dat</c> left pointing at a dead localhost webhook — the user stops
/// getting alerts and has to re-paste two URLs out of Discord.
/// </para>
/// Per-file strategy is design §1.4 and is not this class's to change:
/// <list type="bullet">
///   <item><c>consent.dat</c> — self-cleaning. Grant, then revoke. No backup: the harness only ever
///   adds and removes its own plugin id, so a backup could only ever lose someone else's grant.</item>
///   <item><c>settings.json</c> — single key. Read the original, write the harness value, put the
///   original back. Never rewritten wholesale: the app rewrites that file on exit, and a wholesale
///   restore would discard whatever it wrote while the harness ran.</item>
///   <item><c>metric-rules.json</c> — harness-owned. Backed up only if one already exists; otherwise
///   deleted on restore, so a profile that never had rules does not acquire them.</item>
///   <item><c>discord.dat</c> — full backup and restore. The only file that needs one, and the one
///   holding real webhook URLs, so the backup stays on the machine, never gets committed, and is
///   deleted as soon as the restore completes.</item>
/// </list>
/// <para>
/// Two invariants worth stating out loud. <see cref="RestoreAsync"/> never throws: every step is
/// attempted, failures are named in the <see cref="RestoreReport"/>, and a restore that gave up
/// halfway is worse than one that never started. And a marker file goes down before anything is
/// touched (design §1.6) — it doubles as the restore manifest, so a Ctrl-C is recoverable by the
/// next run rather than by hand.
/// </para>
/// </summary>
public sealed class ProfileGuard
{
    /// <summary>The crash marker, written in the backup root. Present = a run is in flight, or died.</summary>
    public const string MarkerFileName = "smoke-run-in-progress.json";

    public const string SettingsFileName = "settings.json";
    public const string RulesFileName = "metric-rules.json";
    public const string DiscordFileName = "discord.dat";
    public const string ConsentFileName = "consent.dat";

    private const string BackupSuffix = ".bak";

    private static readonly JsonSerializerOptions MarkerJson = new() { WriteIndented = true };

    private readonly string _backupRoot;
    private readonly SmokeRunMarker _marker;
    private readonly Action<string> _log;

    private ProfileGuard(string backupRoot, SmokeRunMarker marker, Action<string> log)
    {
        _backupRoot = backupRoot;
        _marker = marker;
        _log = log;
    }

    public string DataRoot => _marker.DataRoot;
    public string BackupRoot => _backupRoot;

    public string SettingsPath => Path.Combine(DataRoot, SettingsFileName);
    public string RulesPath => Path.Combine(DataRoot, RulesFileName);
    public string DiscordPath => Path.Combine(DataRoot, DiscordFileName);
    public string ConsentPath => Path.Combine(DataRoot, ConsentFileName);

    /// <summary>
    /// The app's live data folder — derived from <see cref="AppSettings.DefaultPath"/> rather than
    /// rebuilt from a literal folder name, so it follows <c>settings.json</c> if the app's data
    /// location ever moves (same reasoning as <c>App.MetricRulesPath</c>).
    /// </summary>
    public static string DefaultDataRoot() => Path.GetDirectoryName(AppSettings.DefaultPath())!;

    /// <summary>
    /// Where backups go by default: a folder inside the data root, which is outside the repo, so a
    /// backup holding real webhook URLs cannot be staged by an <c>add -A</c> in a checkout. The
    /// .gitignore entry for <c>smoke-backup/</c> is the second belt for anyone who points this
    /// somewhere else.
    /// </summary>
    public static string DefaultBackupRoot() => Path.Combine(DefaultDataRoot(), "smoke-backup");

    /// <summary>
    /// Backs up what needs backing up, writes the marker, and hands back the guard. Recovers an
    /// orphaned run first (design §1.6, "before doing anything else").
    /// <para>
    /// This one MAY throw, and deliberately does when an orphan cannot be fully recovered: taking
    /// fresh backups over an unrecovered profile would overwrite the good <c>discord.dat</c> backup
    /// with the localhost-pointing file the dead run left behind, and then faithfully "restore" that.
    /// </para>
    /// </summary>
    public static async Task<ProfileGuard> AcquireAsync(string dataRoot, string backupRoot, Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupRoot);
        var write = log ?? (_ => { });

        dataRoot = Path.GetFullPath(dataRoot);
        backupRoot = Path.GetFullPath(backupRoot);
        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(backupRoot);

        var markerPath = Path.Combine(backupRoot, MarkerFileName);
        if (File.Exists(markerPath))
        {
            write($"[guard] found {MarkerFileName} from a run that never finished — restoring from it before anything else.");
            var recovered = await RecoverOrphanedAsync(backupRoot, write).ConfigureAwait(false);
            if (!recovered || File.Exists(markerPath))
            {
                throw new InvalidOperationException(
                    $"An interrupted smoke run could not be fully restored — {markerPath} is still there. "
                    + "Refusing to start, because taking fresh backups now would overwrite the good ones with "
                    + "whatever the interrupted run left behind. Release whatever is locking the profile files "
                    + "and run the recovery again; delete the marker by hand only if you have confirmed the "
                    + "profile is already intact.");
            }
        }

        var marker = new SmokeRunMarker
        {
            DataRoot = dataRoot,
            StartedUtc = DateTimeOffset.UtcNow.ToString("O"),
            ConsentExisted = File.Exists(Path.Combine(dataRoot, ConsentFileName)),
        };

        // discord.dat: full copy. The bytes, not the decoded config — a DPAPI envelope round-tripped
        // through Load/Save would be a re-encryption, which is not the same file back.
        var discordPath = Path.Combine(dataRoot, DiscordFileName);
        if (File.Exists(discordPath))
        {
            File.Copy(discordPath, Path.Combine(backupRoot, DiscordFileName + BackupSuffix), overwrite: true);
            marker.DiscordBackupFile = DiscordFileName + BackupSuffix;
            write($"[guard] backed up {DiscordFileName}");
        }
        else
        {
            write($"[guard] no {DiscordFileName} in the profile — restore will delete the one the harness writes.");
        }

        // metric-rules.json: backed up only if one is already there.
        var rulesPath = Path.Combine(dataRoot, RulesFileName);
        if (File.Exists(rulesPath))
        {
            File.Copy(rulesPath, Path.Combine(backupRoot, RulesFileName + BackupSuffix), overwrite: true);
            marker.RulesBackupFile = RulesFileName + BackupSuffix;
            write($"[guard] backed up {RulesFileName}");
        }

        // The marker lands AFTER the copies and BEFORE any mutation: that is the only ordering where
        // its presence implies the backups beside it are usable.
        await SaveMarkerAsync(backupRoot, marker).ConfigureAwait(false);
        write($"[guard] marker written. Backups live in {backupRoot} and never leave this machine.");

        return new ProfileGuard(backupRoot, marker, write);
    }

    /// <summary>
    /// Saves the current value of one named boolean setting, sets it, and remembers what to put back.
    /// Returns the value the restore will write.
    /// <para>
    /// General rather than welded to <c>MetricAlertsEnabled</c>: the harness's streamer-mode scenario
    /// needs the same save-set-restore on a different key, and a second hand-rolled copy of this is
    /// how one of them ends up not restoring. First touch wins — a scenario that toggles the gate on,
    /// off and on again restores the value the user had, not the one the previous scenario left.
    /// </para>
    /// </summary>
    public async Task<bool> SetBooleanSettingAsync(BooleanSetting setting, bool value)
    {
        ArgumentNullException.ThrowIfNull(setting);

        using var settings = new AppSettings(SettingsPath);
        if (!_marker.OriginalSettings.ContainsKey(setting.Key))
        {
            var original = await setting.ReadAsync(settings).ConfigureAwait(false);
            _marker.OriginalSettings[setting.Key] = original;
            // Persisted before the write, so a crash between the two restores the user's value
            // rather than nothing.
            await SaveMarkerAsync(_backupRoot, _marker).ConfigureAwait(false);
        }

        await setting.WriteAsync(settings, value).ConfigureAwait(false);
        var restoresTo = _marker.OriginalSettings[setting.Key];
        _log($"[guard] {setting.Key} = {value} (restores to {restoresTo})");
        return restoresTo;
    }

    /// <summary>Convenience for the gate, which every scenario touches. Same path underneath.</summary>
    public Task<bool> SetMetricAlertsEnabledAsync(bool enabled)
        => SetBooleanSettingAsync(BooleanSetting.MetricAlertsEnabled, enabled);

    /// <summary>Convenience for the masked-naming scenario. Same path underneath.</summary>
    public Task<bool> SetStreamerModeAsync(bool enabled)
        => SetBooleanSettingAsync(BooleanSetting.StreamerMode, enabled);

    /// <summary>
    /// Writes the harness's rules file. No backup happens here — <see cref="AcquireAsync"/> already
    /// took one if there was anything to take, so a scenario writing rules twice cannot capture its
    /// own first write as "the original."
    /// </summary>
    public Task WriteRulesAsync(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return File.WriteAllTextAsync(RulesPath, json);
    }

    /// <summary>
    /// Read-modify-writes <c>discord.dat</c> through the production store, so the envelope the app
    /// reads next is one the app wrote the format of. A transform rather than two URL parameters
    /// because the runner also has to point a destination set at the catcher.
    /// </summary>
    public async Task MutateDiscordAsync(Func<DiscordConfig, DiscordConfig> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        var store = new DiscordConfigStore(DiscordPath);
        var current = await store.LoadAsync().ConfigureAwait(false);
        await store.SaveAsync(transform(current)).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads <c>discord.dat</c> back and says whether both webhook URLs are the ones the runner
    /// meant to write (design §1.5). Of the three things that can go wrong with this harness, two are
    /// private annoyances; the only one with an audience is a test alert landing in the real clan
    /// channel because the swap silently did not take. This is that guard, so the runner calls it
    /// before reporting a single metric and aborts if it answers false.
    /// <para>
    /// Never logs a URL, matched or not: the real values are bearer credentials and the harness's own
    /// log is not a place for them.
    /// </para>
    /// </summary>
    public async Task<bool> VerifyDiscordSwapAsync(string expectedMineUrl, string expectedClanUrl)
    {
        var config = await new DiscordConfigStore(DiscordPath).LoadAsync().ConfigureAwait(false);
        var mineOk = string.Equals(config.MineWebhookUrl, expectedMineUrl, StringComparison.Ordinal);
        var clanOk = string.Equals(config.ClanWebhookUrl, expectedClanUrl, StringComparison.Ordinal);

        if (!mineOk || !clanOk)
        {
            _log($"[guard] the {DiscordFileName} read-back does NOT match what was written "
                + $"(personal webhook {(mineOk ? "matches" : "does not match")}, "
                + $"clan webhook {(clanOk ? "matches" : "does not match")}). "
                + "Nothing may be reported until this is true.");
        }

        return mineOk && clanOk;
    }

    /// <summary>
    /// Grants the harness's own plugin id its capabilities, and records the id so the restore revokes
    /// exactly that and nothing else.
    /// </summary>
    public async Task GrantConsentAsync(string pluginId, IEnumerable<string> capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(capabilities);

        if (!_marker.GrantedPluginIds.Contains(pluginId, StringComparer.Ordinal))
        {
            // Recorded first: a revoke of something that was never granted is a no-op, a grant that
            // was never recorded is a leftover in the user's consent list.
            _marker.GrantedPluginIds.Add(pluginId);
            await SaveMarkerAsync(_backupRoot, _marker).ConfigureAwait(false);
        }

        await new ConsentStore(ConsentPath).GrantAsync(pluginId, capabilities).ConfigureAwait(false);
        _log($"[guard] granted {pluginId} its capabilities (revoked on restore)");
    }

    /// <summary>
    /// Puts the profile back. Never throws: every step is attempted independently and the report
    /// names what went back and what did not. A partial restore keeps the marker and the backups, so
    /// calling this again — or the next run's <see cref="AcquireAsync"/> — finishes the job.
    /// </summary>
    public Task<RestoreReport> RestoreAsync() => RestoreFromMarkerAsync(_backupRoot, _marker, _log);

    /// <summary>
    /// The Ctrl-C path (design §1.6). Looks for a marker from a run that never finished and restores
    /// from the backups beside it. Returns true when a marker was found and a restore was attempted —
    /// read the log, or the marker's continued existence, to know whether it completed. False means
    /// there was nothing to recover (or the marker was unreadable, in which case nothing is touched
    /// and the log says so, because guessing at a profile is worse than refusing).
    /// </summary>
    public static async Task<bool> RecoverOrphanedAsync(string backupRoot, Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupRoot);
        var write = log ?? (_ => { });

        var markerPath = Path.Combine(backupRoot, MarkerFileName);
        if (!File.Exists(markerPath))
        {
            return false;
        }

        SmokeRunMarker? marker;
        try
        {
            marker = JsonSerializer.Deserialize<SmokeRunMarker>(
                await File.ReadAllBytesAsync(markerPath).ConfigureAwait(false), MarkerJson);
        }
        catch (Exception ex)
        {
            write($"[guard] {markerPath} is unreadable ({ex.GetType().Name}: {ex.Message}). "
                + "Nothing was touched — the backups beside it are still there to restore by hand.");
            return false;
        }

        if (marker is null || string.IsNullOrWhiteSpace(marker.DataRoot))
        {
            write($"[guard] {markerPath} names no data root, so there is nothing safe to restore from it. Nothing was touched.");
            return false;
        }

        write($"[guard] recovering an interrupted run (started {marker.StartedUtc}) against {marker.DataRoot}.");
        var report = await RestoreFromMarkerAsync(backupRoot, marker, write).ConfigureAwait(false);
        write(report.ToString());
        return true;
    }

    /// <summary>
    /// One restore path, shared by the clean exit and the orphan recovery. Deliberately shared: a
    /// separate recovery implementation is one that only ever runs after a crash, which is exactly
    /// when nobody is watching it.
    /// </summary>
    private static async Task<RestoreReport> RestoreFromMarkerAsync(string backupRoot, SmokeRunMarker marker, Action<string> log)
    {
        var restored = new List<string>();
        var failed = new List<string>();

        // Catching Exception is the point here, not sloppiness: "nothing may throw past RestoreAsync"
        // means an IOException on one file must not abandon the other three.
        async Task StepAsync(string label, Func<Task> body)
        {
            try
            {
                await body().ConfigureAwait(false);
                restored.Add(label);
                log($"[guard] restored {label}");
            }
            catch (Exception ex)
            {
                failed.Add($"{label} ({ex.GetType().Name}: {ex.Message})");
                log($"[guard] could NOT restore {label}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        var discordPath = Path.Combine(marker.DataRoot, DiscordFileName);
        await StepAsync(DiscordFileName, () =>
        {
            if (marker.DiscordBackupFile is { Length: > 0 } name)
            {
                File.Copy(Path.Combine(backupRoot, name), discordPath, overwrite: true);
            }
            else if (File.Exists(discordPath))
            {
                // There was none before the run, so the harness wrote this one.
                File.Delete(discordPath);
            }
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        var rulesPath = Path.Combine(marker.DataRoot, RulesFileName);
        await StepAsync(RulesFileName, () =>
        {
            if (marker.RulesBackupFile is { Length: > 0 } name)
            {
                File.Copy(Path.Combine(backupRoot, name), rulesPath, overwrite: true);
            }
            else if (File.Exists(rulesPath))
            {
                File.Delete(rulesPath);
            }
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        // One step per key, so a failure on one still puts the other back.
        foreach (var (key, original) in marker.OriginalSettings)
        {
            await StepAsync($"{SettingsFileName}:{key}", async () =>
            {
                var setting = BooleanSetting.ByKey(key)
                    ?? throw new InvalidOperationException(
                        $"The marker records a setting this build does not know how to restore: {key}.");
                using var settings = new AppSettings(Path.Combine(marker.DataRoot, SettingsFileName));
                await setting.WriteAsync(settings, original).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        var consentPath = Path.Combine(marker.DataRoot, ConsentFileName);
        foreach (var pluginId in marker.GrantedPluginIds)
        {
            await StepAsync($"{ConsentFileName}:{pluginId}", async () =>
            {
                if (!File.Exists(consentPath))
                {
                    return;     // nothing granted, or already cleaned up
                }
                var store = new ConsentStore(consentPath);
                await store.RevokeAsync(pluginId).ConfigureAwait(false);

                // A profile that had no consent.dat should not keep an empty envelope just because
                // the harness granted and revoked one id.
                if (!marker.ConsentExisted && (await store.ListAsync().ConfigureAwait(false)).Count == 0)
                {
                    File.Delete(consentPath);
                }
            }).ConfigureAwait(false);
        }

        var report = new RestoreReport(restored, failed);
        if (report.Complete)
        {
            // The discord backup holds real webhook URLs; it exists for exactly as long as it is
            // needed. Failing to delete it is hygiene, not a restore failure, so it is logged and
            // does not colour the report.
            DeleteQuietly(Path.Combine(backupRoot, marker.DiscordBackupFile ?? ""), log);
            DeleteQuietly(Path.Combine(backupRoot, marker.RulesBackupFile ?? ""), log);
            DeleteQuietly(Path.Combine(backupRoot, MarkerFileName), log);
        }

        return report;
    }

    private static void DeleteQuietly(string path, Action<string> log)
    {
        if (string.IsNullOrWhiteSpace(Path.GetFileName(path)) || !File.Exists(path))
        {
            return;
        }
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            log($"[guard] the profile is restored, but {path} could not be cleaned up "
                + $"({ex.GetType().Name}). Delete it by hand — a leftover {DiscordFileName}{BackupSuffix} "
                + "holds real webhook URLs.");
        }
    }

    private static async Task SaveMarkerAsync(string backupRoot, SmokeRunMarker marker)
    {
        Directory.CreateDirectory(backupRoot);
        var path = Path.Combine(backupRoot, MarkerFileName);
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, JsonSerializer.SerializeToUtf8Bytes(marker, MarkerJson)).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);     // tmp + rename: a half-written manifest is worse than none
    }
}

/// <summary>
/// What a restore put back and what it did not. A partial restore is reported, never thrown: the
/// runner has to be able to tell the user "your settings are back, your discord.dat is not."
/// </summary>
public sealed record RestoreReport(IReadOnlyList<string> Restored, IReadOnlyList<string> Failed)
{
    public bool Complete => Failed.Count == 0;

    public override string ToString() => Complete
        ? $"[guard] profile restored: {(Restored.Count == 0 ? "nothing needed putting back" : string.Join(", ", Restored))}."
        : $"[guard] PARTIAL RESTORE. Put back: {(Restored.Count == 0 ? "nothing" : string.Join(", ", Restored))}. "
          + $"NOT put back: {string.Join("; ", Failed)}. The marker and backups are still there, so running "
          + "the harness again (or MetricSmoke --recover) retries exactly the files that failed.";
}

/// <summary>
/// One boolean setting the guard can save, set and restore, named by the key it has in
/// <c>settings.json</c>. Two accessor delegates rather than a switch, so the marker can round-trip a
/// key through <see cref="ByKey"/> and the orphan recovery restores a setting the process that set it
/// no longer exists to remember.
/// </summary>
public sealed class BooleanSetting
{
    private readonly Func<IAppSettings, Task<bool>> _read;
    private readonly Func<IAppSettings, bool, Task> _write;

    private BooleanSetting(string key, Func<IAppSettings, Task<bool>> read, Func<IAppSettings, bool, Task> write)
    {
        Key = key;
        _read = read;
        _write = write;
    }

    /// <summary>The camelCase key as it appears in <c>settings.json</c> — also the marker's id for it.</summary>
    public string Key { get; }

    public Task<bool> ReadAsync(IAppSettings settings) => _read(settings);

    public Task WriteAsync(IAppSettings settings, bool value) => _write(settings, value);

    /// <summary>The metric-alert gate. Every scenario touches it.</summary>
    public static BooleanSetting MetricAlertsEnabled { get; } = new(
        "metricAlertsEnabled",
        s => s.GetMetricAlertsEnabledAsync(),
        (s, v) => s.SetMetricAlertsEnabledAsync(v));

    /// <summary>Streamer mode, for the masked-versus-real account naming row.</summary>
    public static BooleanSetting StreamerMode { get; } = new(
        "streamerMode",
        s => s.GetStreamerModeAsync(),
        (s, v) => s.SetStreamerModeAsync(v));

    /// <summary>Everything the guard can restore. A key absent from here cannot be restored from a
    /// marker, so adding a scenario that flips a new setting means adding it here in the same commit.</summary>
    public static IReadOnlyList<BooleanSetting> All { get; } = [MetricAlertsEnabled, StreamerMode];

    public static BooleanSetting? ByKey(string key)
        => All.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.Ordinal));

    public override string ToString() => Key;
}

/// <summary>
/// The crash marker's contents, and the restore manifest — the same thing, because a marker that
/// only says "a run was in flight" leaves the next run nothing to act on. Written before the first
/// mutation and updated whenever more state is captured, so whatever is on disk describes everything
/// currently changed.
/// <para>
/// It holds absolute paths, which is why it lives in the data root (or a temp root, under test) and
/// never in the repo.
/// </para>
/// </summary>
public sealed class SmokeRunMarker
{
    public string DataRoot { get; set; } = string.Empty;
    public string StartedUtc { get; set; } = string.Empty;

    /// <summary>Backup file NAMES, relative to the backup root — null when there was no such file to
    /// back up, which is also the instruction to delete the harness's copy on restore.</summary>
    public string? DiscordBackupFile { get; set; }

    public string? RulesBackupFile { get; set; }

    /// <summary>Whether the profile already had a <c>consent.dat</c>, so a revoke that empties the
    /// file knows whether to leave an empty envelope behind.</summary>
    public bool ConsentExisted { get; set; }

    /// <summary>Settings key → the value the user had. First touch wins; see
    /// <see cref="ProfileGuard.SetBooleanSettingAsync"/>.</summary>
    public Dictionary<string, bool> OriginalSettings { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Plugin ids the harness granted consent to, and nothing else.</summary>
    public List<string> GrantedPluginIds { get; set; } = [];
}
