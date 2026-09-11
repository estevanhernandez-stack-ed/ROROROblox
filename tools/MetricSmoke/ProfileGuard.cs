using System.IO;
using System.Security.Cryptography;
using System.Text;
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
/// <b>Both stores this class writes through lie to it on failure, and that is the central hazard.</b>
/// <see cref="AppSettings"/> swallows <c>IOException</c> and <c>JsonException</c> into a fresh
/// defaults blob, and <see cref="DiscordConfigStore"/> swallows <c>CryptographicException</c> into an
/// empty config. Both are right for an app that must not fail to start, and both are catastrophic for
/// a tool whose job is to put a value back: a lost read looks like "the original was false", and a
/// read-modify-write over a lost read persists defaults over the real file. So every read this class
/// trusts is probed first (does the file exist, and does it parse or decrypt?) and every write is read
/// back. Where it cannot establish the truth it refuses, and says which value it did not put back.
/// </para>
/// <para>
/// Two more invariants. <see cref="RestoreAsync"/> never throws: every step is attempted, failures are
/// named in the <see cref="RestoreReport"/>, and a restore that gave up halfway is worse than one that
/// never started. And a run takes exclusive ownership before it touches anything (design §1.6) — the
/// marker file is claimed with <c>FileMode.CreateNew</c> before the first backup copy, and a
/// process-scoped named semaphore keyed to the backup root makes a second concurrent run, or a
/// <c>--recover</c> against a live run, refuse instead of racing.
/// </para>
/// </summary>
public sealed class ProfileGuard : IDisposable
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
    private readonly Semaphore _ownership;

    private bool _ownershipReleased;
    private bool _restoredCleanly;

    private ProfileGuard(string backupRoot, SmokeRunMarker marker, Action<string> log, Semaphore ownership)
    {
        _backupRoot = backupRoot;
        _marker = marker;
        _log = log;
        _ownership = ownership;
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
    /// .gitignore entries are the second belt for anyone who points this somewhere else.
    /// </summary>
    public static string DefaultBackupRoot() => Path.Combine(DefaultDataRoot(), "smoke-backup");

    /// <summary>True when a marker from an unfinished run is sitting in <paramref name="backupRoot"/>.</summary>
    public static bool HasOrphanedMarker(string backupRoot)
        => File.Exists(Path.Combine(Canonical(backupRoot), MarkerFileName));

    /// <summary>
    /// One spelling for one folder. <see cref="Path.GetFullPath(string)"/> alone is not enough —
    /// it keeps a trailing separator, so <c>…\smoke-backup</c> and <c>…\smoke-backup\</c> stay
    /// different strings and would hash to two different ownership names, which is a live run this
    /// process cannot see. <see cref="Path.TrimEndingDirectorySeparator(string)"/> leaves a root like
    /// <c>C:\</c> alone, which is the one case where the separator is not decoration.
    /// </summary>
    private static string Canonical(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>
    /// Takes ownership, recovers an orphaned run, backs up what needs backing up, and hands back the
    /// guard. Nothing in the profile is mutated by this method.
    /// <para>
    /// This one MAY throw, and deliberately does in four cases: another run is already in flight; an
    /// orphan could not be fully recovered (taking fresh backups over an unrecovered profile would
    /// overwrite the good <c>discord.dat</c> backup with the localhost-pointing file the dead run left
    /// behind); <c>discord.dat</c> exists but will not decrypt; or <c>settings.json</c> exists but will
    /// not parse. The last two are refusals rather than repairs, because both stores would silently
    /// hand back an empty value and let the harness overwrite the file with it.
    /// </para>
    /// </summary>
    public static async Task<ProfileGuard> AcquireAsync(string dataRoot, string backupRoot, Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupRoot);
        var write = log ?? (_ => { });

        dataRoot = Canonical(dataRoot);
        backupRoot = Canonical(backupRoot);
        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(backupRoot);

        var ownership = ClaimOwnership(backupRoot);
        var markerPath = Path.Combine(backupRoot, MarkerFileName);
        var claimed = false;

        try
        {
            if (File.Exists(markerPath))
            {
                write($"[guard] found {MarkerFileName} from a run that never finished — restoring from it before anything else.");
                // The core, not the public entry point: the liveness probe would see OUR ownership.
                var recovery = await RecoverFromMarkerFileAsync(backupRoot, write).ConfigureAwait(false);
                if (recovery is null || !recovery.Complete || File.Exists(markerPath))
                {
                    throw new InvalidOperationException(
                        $"An interrupted smoke run could not be fully restored — {markerPath} is still there. "
                        + "Refusing to start, because taking fresh backups now would overwrite the good ones with "
                        + "whatever the interrupted run left behind. Release whatever is locking the profile files "
                        + "and run MetricSmoke --recover again; delete the marker by hand only if you have "
                        + "confirmed the profile is already intact.");
                }
            }

            var discordPath = Path.Combine(dataRoot, DiscordFileName);
            var settingsPath = Path.Combine(dataRoot, SettingsFileName);
            var rulesPath = Path.Combine(dataRoot, RulesFileName);

            // Probe both lying stores BEFORE claiming the marker, so a refusal leaves nothing at all
            // behind. A backup of a file that will not decrypt is worth nothing anyway — it is the
            // same unreadable bytes — so there is nothing to take first.
            var discordExisted = File.Exists(discordPath);
            DiscordConfig? discordBefore = null;
            if (discordExisted)
            {
                var read = await ReadDiscordAsync(discordPath).ConfigureAwait(false);
                discordBefore = read.Config;
                if (read.Outcome != DiscordReadOutcome.Ok)
                {
                    throw new InvalidOperationException(
                        read.Explain(discordPath)
                        + " Writing the harness's webhook URLs on top of a config this tool could not read "
                        + "first would blank the destinations, the muted list and both toggles while reporting "
                        + "a successful swap. Refusing to start.");
                }
            }

            var settingsState = await ProbeSettingsAsync(settingsPath).ConfigureAwait(false);
            if (settingsState == SettingsFileState.Unreadable)
            {
                throw new InvalidOperationException(
                    $"{settingsPath} exists but could not be read as JSON, and AppSettings answers that with a "
                    + "DEFAULTS blob rather than an error. Any value read from it now would be a fabrication, and "
                    + "putting it back would persist defaults over the real file. Refusing to start: fix or "
                    + "remove that file first.");
            }

            var marker = new SmokeRunMarker
            {
                DataRoot = dataRoot,
                StartedUtc = DateTimeOffset.UtcNow.ToString("O"),
                DiscordExisted = discordExisted,
                DiscordShape = discordBefore is null ? null : ShapeOf(discordBefore),
                RulesExisted = File.Exists(rulesPath),
                SettingsExisted = settingsState == SettingsFileState.Readable,
                ConsentExisted = File.Exists(Path.Combine(dataRoot, ConsentFileName)),
            };

            // The marker is claimed BEFORE the first copy, with CreateNew so two runs cannot both
            // believe they own it. A marker naming no backups therefore means "a run started and
            // mutated nothing", which is exactly what a crash in this window leaves behind — and the
            // Existed flags, not the absence of a backup name, are what the restore reads.
            ClaimMarkerFile(markerPath, marker);
            claimed = true;

            if (discordExisted)
            {
                // The bytes, not the decoded config: a DPAPI envelope round-tripped through Load/Save
                // would be a re-encryption, which is not the same file back.
                File.Copy(discordPath, Path.Combine(backupRoot, DiscordFileName + BackupSuffix), overwrite: true);
                marker.DiscordBackupFile = DiscordFileName + BackupSuffix;
                write($"[guard] backed up {DiscordFileName}");
            }
            else
            {
                write($"[guard] no {DiscordFileName} in the profile — restore will delete the one the harness writes.");
            }

            if (marker.RulesExisted)
            {
                File.Copy(rulesPath, Path.Combine(backupRoot, RulesFileName + BackupSuffix), overwrite: true);
                marker.RulesBackupFile = RulesFileName + BackupSuffix;
                write($"[guard] backed up {RulesFileName}");
            }

            await SaveMarkerAsync(backupRoot, marker).ConfigureAwait(false);
            write($"[guard] marker written. Backups live in {backupRoot} and never leave this machine.");

            return new ProfileGuard(backupRoot, marker, write, ownership);
        }
        catch
        {
            // A refusal must not leave a marker nobody owns: the next run would read it as an orphan
            // and refuse too. Only the marker THIS call claimed is removed, and only before any
            // mutation has happened.
            if (claimed)
            {
                try { File.Delete(markerPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            var released = false;
            ReleaseOwnership(ownership, ref released);
            throw;
        }
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
    /// <para>
    /// Probes the file before reading and reads the value back after writing, because a settings read
    /// that loses the app's tmp-and-rename race answers "false" instead of failing. Throws rather than
    /// record a fabricated original.
    /// </para>
    /// </summary>
    public async Task<bool> SetBooleanSettingAsync(BooleanSetting setting, bool value)
    {
        ArgumentNullException.ThrowIfNull(setting);
        EnsureUsable();
        EnsureBackupsIntact();

        var state = await ProbeSettingsAsync(SettingsPath).ConfigureAwait(false);
        if (state == SettingsFileState.Unreadable)
        {
            throw new InvalidOperationException(
                $"{SettingsPath} could not be read as JSON, so the value in it cannot be trusted as the one to "
                + $"put back — AppSettings would hand back a defaults blob and the restore would persist it. "
                + $"{setting.Key} was NOT changed.");
        }

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

        var readBack = await setting.ReadAsync(settings).ConfigureAwait(false);
        if (readBack != value)
        {
            throw new InvalidOperationException(
                $"{setting.Key} was written as {value} but reads back as {readBack}. Either something else is "
                + $"writing {SettingsFileName} at the same time, or the read is failing silently. Refusing to "
                + "continue on a value the app may never see.");
        }

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
        EnsureUsable();
        EnsureBackupsIntact();
        return File.WriteAllTextAsync(RulesPath, json);
    }

    /// <summary>
    /// Read-modify-writes <c>discord.dat</c> through the production store, so the envelope the app
    /// reads next is one the app wrote the format of. A transform rather than two URL parameters
    /// because the runner also has to point a destination set at the catcher.
    /// <para>
    /// Refuses if the file has stopped decrypting since <see cref="AcquireAsync"/> probed it: the
    /// store would hand back an empty config, the transform would add the harness's URLs to it, and
    /// the save would persist that — the destinations, the muted list and both toggles gone, with the
    /// read-back still reporting a successful swap.
    /// </para>
    /// </summary>
    public async Task MutateDiscordAsync(Func<DiscordConfig, DiscordConfig> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        EnsureUsable();
        EnsureBackupsIntact();

        var read = await ReadDiscordAsync(DiscordPath).ConfigureAwait(false);
        var current = read.Outcome switch
        {
            DiscordReadOutcome.Ok => read.Config!,
            DiscordReadOutcome.Absent => new DiscordConfig(),
            _ => throw new InvalidOperationException(
                read.Explain(DiscordPath)
                + " Writing now would blank the real destinations and still look like a successful swap, so "
                + $"nothing was written. The backup taken at the start of this run is in {_backupRoot}."),
        };

        await new DiscordConfigStore(DiscordPath).SaveAsync(transform(current)).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads <c>discord.dat</c> back and says whether the swap landed and nothing else was lost
    /// (design §1.5). Of the three things that can go wrong with this harness, two are private
    /// annoyances; the only one with an audience is a test alert landing in the real clan channel
    /// because the swap silently did not take. This is that guard, so the runner calls it before
    /// reporting a single metric and aborts if it answers false.
    /// <para>
    /// Three questions, not one: does the envelope still decrypt, are both URLs the ones we wrote,
    /// and does the rest of the config still look like what was there at
    /// <see cref="AcquireAsync"/>. The third distinguishes "swapped" from "blanked and swapped" — a
    /// swallowed <c>CryptographicException</c> produces a perfectly valid envelope holding a default
    /// config plus the new URLs, which answers the first two questions yes. The comparison covers the
    /// fields the harness never touches (see <see cref="ShapeOf"/>), so the runner's own destination
    /// change does not read as damage.
    /// </para>
    /// Never logs a URL, matched or not: the real values are bearer credentials and the harness's own
    /// log is not a place for them.
    /// </summary>
    public async Task<bool> VerifyDiscordSwapAsync(string expectedMineUrl, string expectedClanUrl)
    {
        var read = await ReadDiscordAsync(DiscordPath).ConfigureAwait(false);
        if (read.Outcome != DiscordReadOutcome.Ok)
        {
            _log($"[guard] the swap cannot be confirmed: {read.Explain(DiscordPath)} Nothing may be reported.");
            return false;
        }

        var config = read.Config!;

        var mineOk = string.Equals(config.MineWebhookUrl, expectedMineUrl, StringComparison.Ordinal);
        var clanOk = string.Equals(config.ClanWebhookUrl, expectedClanUrl, StringComparison.Ordinal);
        if (!mineOk || !clanOk)
        {
            _log($"[guard] the {DiscordFileName} read-back does NOT match what was written "
                + $"(personal webhook {(mineOk ? "matches" : "does not match")}, "
                + $"clan webhook {(clanOk ? "matches" : "does not match")}). "
                + "Nothing may be reported until this is true.");
            return false;
        }

        var shapeNow = ShapeOf(config);
        if (_marker.DiscordShape is { } before && !string.Equals(before, shapeNow, StringComparison.Ordinal))
        {
            _log($"[guard] both webhook URLs landed, but the rest of {DiscordFileName} is not what it was at "
                + $"the start of this run (was [{before}], now [{shapeNow}]). Either the config was blanked by "
                + "a failed decrypt on the way through, or the app rewrote it while the harness ran. Either way "
                + "this is not a clean swap, so nothing may be reported — restore and start again.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Grants the harness's own plugin id its capabilities, and records the id so the restore revokes
    /// exactly that and nothing else.
    /// </summary>
    public async Task GrantConsentAsync(string pluginId, IEnumerable<string> capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(capabilities);
        EnsureUsable();
        EnsureBackupsIntact();

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
    /// <para>
    /// Idempotent after success, and quietly so. The runner will have both an explicit restore and a
    /// <c>finally</c>, so the second call happens on every normal run; answering it with the scariest
    /// message the tool can print would teach a reader to ignore the one message that means trouble.
    /// </para>
    /// </summary>
    public async Task<RestoreReport> RestoreAsync()
    {
        if (_restoredCleanly)
        {
            _log("[guard] already restored by this run — nothing left to put back.");
            return new RestoreReport([], []);
        }

        var report = await RestoreFromMarkerAsync(_backupRoot, _marker, _log).ConfigureAwait(false);
        if (report.Complete)
        {
            _restoredCleanly = true;
            ReleaseOwnership(_ownership, ref _ownershipReleased);
        }
        return report;
    }

    /// <summary>
    /// Releases this run's ownership. Does NOT restore anything — a process that dies holds no
    /// ownership either, and the marker it leaves behind is what the next run acts on.
    /// </summary>
    public void Dispose() => ReleaseOwnership(_ownership, ref _ownershipReleased);

    /// <summary>
    /// The Ctrl-C path (design §1.6). Looks for a marker from a run that never finished and restores
    /// from the backups beside it. Returns true when a marker was found and a restore was attempted;
    /// check <see cref="HasOrphanedMarker"/> afterwards, or read the log, to know whether it
    /// completed. False means there was nothing to recover, a live run owns the profile, or the marker
    /// was unreadable — in the last two cases nothing is touched and the log says why, because
    /// restoring under a live runner, or guessing at a profile, is worse than refusing.
    /// </summary>
    public static async Task<bool> RecoverOrphanedAsync(string backupRoot, Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupRoot);
        var write = log ?? (_ => { });

        // Canonicalised for the same reason AcquireAsync does it: the ownership name is a hash of
        // this string, so a caller spelling the same folder differently — a trailing separator, a
        // relative path — would ask a different semaphore whether a run is live, be told no, and
        // restore underneath it. Task 5 constructs this path rather than taking the default.
        backupRoot = Canonical(backupRoot);

        if (!HasOrphanedMarker(backupRoot))
        {
            return false;
        }

        if (IsRunLive(backupRoot))
        {
            write("[guard] a MetricSmoke run is in flight right now and owns this backup root. Refusing to "
                + "restore underneath it — that would hand the profile back while the runner is still driving "
                + "it, and the run would then restore its own localhost URLs as though they were yours. Wait "
                + "for it to finish, or stop it and run this again.");
            return false;
        }

        var report = await RecoverFromMarkerFileAsync(backupRoot, write).ConfigureAwait(false);
        return report is not null;
    }

    /// <summary>
    /// Reads the marker off disk and restores from it. No liveness probe — the two callers have
    /// already established they are allowed to do this. Null means the marker could not be trusted.
    /// </summary>
    private static async Task<RestoreReport?> RecoverFromMarkerFileAsync(string backupRoot, Action<string> write)
    {
        var markerPath = Path.Combine(backupRoot, MarkerFileName);

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
            return null;
        }

        if (marker is null || string.IsNullOrWhiteSpace(marker.DataRoot))
        {
            write($"[guard] {markerPath} names no data root, so there is nothing safe to restore from it. Nothing was touched.");
            return null;
        }

        write($"[guard] recovering an interrupted run (started {marker.StartedUtc}) against {marker.DataRoot}.");
        var report = await RestoreFromMarkerAsync(backupRoot, marker, write).ConfigureAwait(false);
        write(report.ToString());
        return report;
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
            RestoreFileFromBackup(backupRoot, marker.DiscordBackupFile, discordPath, marker.DiscordExisted,
                DiscordFileName, "re-enter the two webhook URLs in Settings > Alerts", log);
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        var rulesPath = Path.Combine(marker.DataRoot, RulesFileName);
        await StepAsync(RulesFileName, () =>
        {
            RestoreFileFromBackup(backupRoot, marker.RulesBackupFile, rulesPath, marker.RulesExisted,
                RulesFileName, "write the rules file again from Settings", log);
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        // One step per key, so a failure on one still puts the other back.
        var settingsPath = Path.Combine(marker.DataRoot, SettingsFileName);
        foreach (var (key, original) in marker.OriginalSettings)
        {
            await StepAsync($"{SettingsFileName}:{key}", async () =>
            {
                var setting = BooleanSetting.ByKey(key)
                    ?? throw new InvalidOperationException(
                        $"The marker records a setting this build does not know how to restore: {key}. "
                        + $"Set it back to {original} by hand.");

                var state = await ProbeSettingsAsync(settingsPath).ConfigureAwait(false);
                if (state == SettingsFileState.Unreadable)
                {
                    throw new InvalidOperationException(
                        $"{SettingsFileName} could not be read as JSON. Writing through AppSettings now would "
                        + $"persist a DEFAULTS blob over the whole file, so {key} was left alone — set it back "
                        + $"to {original} by hand once that file is readable.");
                }

                if (state == SettingsFileState.Absent)
                {
                    // Never create the file to restore a value into it. If it never existed, the value
                    // was the app's default and still is; if it existed and is now gone, something
                    // outside this run deleted it and writing a defaults blob would not be a restore.
                    log(marker.SettingsExisted
                        ? $"[guard] {SettingsFileName} existed when this run started and is gone now — NOT "
                          + $"recreating it. {key} would have gone back to {original}."
                        : $"[guard] there is no {SettingsFileName} and there was none before the run, so {key} "
                          + "needs nothing.");
                    return;
                }

                using var settings = new AppSettings(settingsPath);
                await setting.WriteAsync(settings, original).ConfigureAwait(false);

                var readBack = await setting.ReadAsync(settings).ConfigureAwait(false);
                if (readBack != original)
                {
                    throw new InvalidOperationException(
                        $"{key} was written back as {original} but reads as {readBack} — the write did not stick. "
                        + "Set it by hand.");
                }
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
            // does not colour the report. Clearing the marker's own fields is what makes a second
            // RestoreAsync — the runner's finally — a no-op rather than a false alarm.
            DeleteQuietly(Path.Combine(backupRoot, marker.DiscordBackupFile ?? ""), log);
            DeleteQuietly(Path.Combine(backupRoot, marker.RulesBackupFile ?? ""), log);
            DeleteQuietly(Path.Combine(backupRoot, MarkerFileName), log);
            marker.ClearRestoredState();
        }

        return report;
    }

    /// <summary>
    /// Puts one file back from its backup, or deletes the one the harness created, or leaves a file
    /// alone that was never backed up because the run died before it mutated anything. The
    /// <c>existed</c> flag is what separates the third case from the second — reading "no backup name"
    /// as "the harness created it" would delete a file the run had not even copied yet.
    /// </summary>
    private static void RestoreFileFromBackup(
        string backupRoot, string? backupFile, string livePath, bool existed, string label, string byHand, Action<string> log)
    {
        if (backupFile is { Length: > 0 } name)
        {
            var backup = Path.Combine(backupRoot, name);
            if (!File.Exists(backup))
            {
                throw new FileNotFoundException(
                    $"the backup this run took is gone, so {label} cannot be put back — it is still whatever the "
                    + $"harness left in it. Recover it from a copy, or {byHand}.", backup);
            }
            File.Copy(backup, livePath, overwrite: true);
            return;
        }

        if (!existed)
        {
            if (File.Exists(livePath))
            {
                File.Delete(livePath);      // there was none before the run, so the harness wrote this one
            }
            return;
        }

        log($"[guard] {label} was never backed up — the run ended before it changed anything, so it is left as it is.");
    }

    private void EnsureUsable()
    {
        if (_restoredCleanly)
        {
            throw new InvalidOperationException(
                "This guard has already restored the profile and released its ownership. Acquire a new one "
                + "before mutating anything else, or the next restore will have nothing recorded to put back.");
        }
    }

    /// <summary>
    /// Re-checks that the backups this run recorded are still on disk, before every mutation. Deleting
    /// the backup folder mid-run would otherwise be invisible: <see cref="SaveMarkerAsync"/> recreates
    /// the directory, so the marker comes back while the backups do not, and the run would carry on
    /// mutating a profile it could no longer put back.
    /// </summary>
    private void EnsureBackupsIntact()
    {
        var missing = new List<string>();
        if (!File.Exists(Path.Combine(_backupRoot, MarkerFileName))) missing.Add(MarkerFileName);
        foreach (var name in new[] { _marker.DiscordBackupFile, _marker.RulesBackupFile })
        {
            if (name is { Length: > 0 } && !File.Exists(Path.Combine(_backupRoot, name))) missing.Add(name);
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"This run's backups are no longer in {_backupRoot} (missing: {string.Join(", ", missing)}). "
                + "Refusing to change anything else — carrying on would mutate a profile that can no longer be "
                + "put back. Restore what you can from wherever those files went and start a fresh run.");
        }
    }

    /// <summary>
    /// The fields the harness never touches, as one comparable string. Deliberately excludes both
    /// webhook URLs and <c>MetricBreachDestinations</c>, which the runner changes on purpose — a
    /// fingerprint that flagged the harness's own edits would be ignored within a week.
    /// </summary>
    private static string ShapeOf(DiscordConfig c) => string.Join(
        ' ',
        $"presence={c.PresenceEnabled}",
        $"join={c.JoinEnabled}",
        $"droppedOut={c.DroppedOutDestination}",
        $"memoryWarning={c.MemoryWarningDestination}",
        $"droppedOutSet={c.DroppedOutDestinations.Count}",
        $"memoryWarningSet={c.MemoryWarningDestinations.Count}",
        $"recycledSet={c.RecycledDestinations.Count}",
        $"uptimeSet={c.UptimeMarkDestinations.Count}",
        $"muted={c.MutedAccountIds.Count}");

    private enum DiscordReadOutcome
    {
        /// <summary>No file. The harness may create one, and the restore deletes it again.</summary>
        Absent,

        /// <summary>Something else has it open right now. Nothing is wrong with the file.</summary>
        Locked,

        /// <summary>It will not decrypt or will not parse. This is the one that means damage.</summary>
        Unreadable,

        /// <summary>Decrypted and parsed.</summary>
        Ok,
    }

    private readonly record struct DiscordRead(DiscordReadOutcome Outcome, DiscordConfig? Config)
    {
        /// <summary>The half of a failure a user can act on, without naming a cause we did not establish.</summary>
        public string Explain(string path) => Outcome switch
        {
            DiscordReadOutcome.Locked =>
                $"{path} is open by another process right now — the app writes that file too, so this is "
                + "usually a moment's overlap rather than damage. Nothing is wrong with the file; try again.",
            DiscordReadOutcome.Unreadable =>
                $"{path} exists but will not decrypt with this user's DPAPI key, or does not parse, and the "
                + "store answers that with an EMPTY config rather than an error.",
            DiscordReadOutcome.Absent => $"{path} is not there.",
            _ => $"{path} is readable.",
        };
    }

    /// <summary>
    /// Decodes <c>discord.dat</c> only if it genuinely decrypts and parses, so a caller can tell
    /// "there is no config" and "someone has the file open" from "the store swallowed a
    /// <c>CryptographicException</c> and handed me a blank one."
    /// <para>
    /// Retries an <c>IOException</c> exactly as the settings probe does, and for the same reason: this
    /// tool runs ALONGSIDE the app, the app touches this file, and a momentary lock is not
    /// hypothetical. Answering one with "will not decrypt with this user's DPAPI key" would point a
    /// user at a frightening and wrong conclusion about a file that is perfectly fine. Failing safe is
    /// right; misnaming the cause is not.
    /// </para>
    /// </summary>
    private static async Task<DiscordRead> ReadDiscordAsync(string path)
    {
        const int attempts = 3;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            if (!File.Exists(path))
            {
                return new DiscordRead(DiscordReadOutcome.Absent, null);
            }

            try
            {
                var encrypted = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                if (encrypted.Length == 0)
                {
                    return new DiscordRead(DiscordReadOutcome.Unreadable, null);
                }
                var decrypted = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
                var config = JsonSerializer.Deserialize<DiscordConfig>(decrypted);
                return config is null
                    ? new DiscordRead(DiscordReadOutcome.Unreadable, null)
                    : new DiscordRead(DiscordReadOutcome.Ok, config);
            }
            catch (IOException) when (attempt < attempts)
            {
                await Task.Delay(60).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return new DiscordRead(DiscordReadOutcome.Locked, null);
            }
            catch (Exception)
            {
                // CryptographicException and JsonException — the two DiscordConfigStore turns into an
                // empty config. Here they stay what they are.
                return new DiscordRead(DiscordReadOutcome.Unreadable, null);
            }
        }

        return new DiscordRead(DiscordReadOutcome.Locked, null);
    }

    private enum SettingsFileState { Absent, Readable, Unreadable }

    /// <summary>
    /// Whether <c>settings.json</c> can be trusted right now. Retries an <c>IOException</c>, because
    /// the app writes that file with a tmp-and-rename and a read can lose the race; does not retry a
    /// parse failure, because <c>File.Move</c> is atomic so a reader sees the whole old file or the
    /// whole new one, and unparseable therefore means genuinely unparseable.
    /// </summary>
    private static async Task<SettingsFileState> ProbeSettingsAsync(string path)
    {
        const int attempts = 3;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            if (!File.Exists(path))
            {
                return SettingsFileState.Absent;
            }

            try
            {
                var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                if (bytes.Length == 0)
                {
                    return SettingsFileState.Unreadable;    // AppSettings would answer this with defaults
                }
                using var _ = JsonDocument.Parse(bytes);
                return SettingsFileState.Readable;
            }
            catch (IOException) when (attempt < attempts)
            {
                await Task.Delay(60).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return SettingsFileState.Unreadable;
            }
        }

        return SettingsFileState.Unreadable;
    }

    /// <summary>
    /// Exclusive ownership of one backup root, for as long as this process lives. A named semaphore
    /// rather than a lock file: it cannot go stale (the kernel object dies with the last handle, so a
    /// killed run leaves the marker but never a lock), it leaves nothing to clean up, and unlike a
    /// Mutex it is not thread-affine, which matters because this class releases across awaits.
    /// Keyed to the backup root so two harnesses against two profiles never see each other.
    /// </summary>
    private static Semaphore ClaimOwnership(string backupRoot)
    {
        var semaphore = new Semaphore(1, 1, OwnershipName(backupRoot));
        if (!semaphore.WaitOne(0))
        {
            semaphore.Dispose();
            throw new InvalidOperationException(
                $"Another MetricSmoke run already owns {backupRoot}. Two runs against one profile would each "
                + "back up the other's half-swapped files, and the second one's restore would put back the "
                + "first one's localhost webhook URLs as though they were yours. Wait for it to finish.");
        }
        return semaphore;
    }

    private static bool IsRunLive(string backupRoot)
    {
        using var semaphore = new Semaphore(1, 1, OwnershipName(backupRoot));
        if (!semaphore.WaitOne(0))
        {
            return true;
        }
        semaphore.Release();
        return false;
    }

    private static void ReleaseOwnership(Semaphore semaphore, ref bool released)
    {
        if (released)
        {
            return;
        }
        released = true;
        try { semaphore.Release(); } catch (SemaphoreFullException) { }
        semaphore.Dispose();
    }

    // Local\ rather than Global\: overlapping runs are one user in one session double-clicking, or two
    // terminals. The hash keeps the name inside the 260-character limit and out of the way of a path
    // with characters a kernel object name cannot carry.
    //
    // Canonicalised HERE as well as at every entry point, on purpose: this is the one function whose
    // output IS the identity of a run, so normalising at the chokepoint means a later caller cannot
    // reintroduce a second name for one folder by spelling the path differently.
    private static string OwnershipName(string backupRoot)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(backupRoot).ToLowerInvariant())));
        return $@"Local\rororo-metric-smoke-{key[..16]}";
    }

    /// <summary>
    /// Claims the marker with <c>CreateNew</c>, which fails if one is already there. This is the
    /// on-disk half of ownership and it happens before the first backup copy, so a crash in that
    /// window leaves a marker that names no backups — which the restore reads, correctly, as "this run
    /// mutated nothing."
    /// </summary>
    private static void ClaimMarkerFile(string markerPath, SmokeRunMarker marker)
    {
        try
        {
            using var stream = new FileStream(markerPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(marker, MarkerJson);
            stream.Write(bytes);
            stream.Flush();
        }
        catch (IOException ex) when (File.Exists(markerPath))
        {
            throw new InvalidOperationException(
                $"{markerPath} appeared between this run's check and its claim, which means another run got "
                + "there first. Refusing to start.", ex);
        }
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
/// only says "a run was in flight" leaves the next run nothing to act on. Claimed before the first
/// backup copy and updated whenever more state is captured, so whatever is on disk describes
/// everything currently changed.
/// <para>
/// It holds absolute paths, which is why it lives in the data root (or a temp root, under test) and
/// never in the repo.
/// </para>
/// </summary>
public sealed class SmokeRunMarker
{
    public string DataRoot { get; set; } = string.Empty;
    public string StartedUtc { get; set; } = string.Empty;

    /// <summary>Backup file NAMES, relative to the backup root. Null means no backup was taken —
    /// which, read together with the Existed flag beside it, is either "there was nothing to back up,
    /// so delete what the harness wrote" or "the run died before it copied anything, so leave it."</summary>
    public string? DiscordBackupFile { get; set; }

    public bool DiscordExisted { get; set; }

    /// <summary>The <c>discord.dat</c> fields the harness never touches, as they were at the start of
    /// the run, so the read-back can tell a swap from a blanking.</summary>
    public string? DiscordShape { get; set; }

    public string? RulesBackupFile { get; set; }

    public bool RulesExisted { get; set; }

    /// <summary>Whether a readable <c>settings.json</c> was there when the run started, so the restore
    /// never creates one to hold a value that would be the app's default anyway.</summary>
    public bool SettingsExisted { get; set; }

    /// <summary>Whether the profile already had a <c>consent.dat</c>, so a revoke that empties the
    /// file knows whether to leave an empty envelope behind.</summary>
    public bool ConsentExisted { get; set; }

    /// <summary>Settings key → the value the user had. First touch wins; see
    /// <see cref="ProfileGuard.SetBooleanSettingAsync"/>.</summary>
    public Dictionary<string, bool> OriginalSettings { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Plugin ids the harness granted consent to, and nothing else.</summary>
    public List<string> GrantedPluginIds { get; set; } = [];

    /// <summary>
    /// Forgets everything a completed restore has already put back, so a second restore has nothing
    /// to do instead of trying to copy backups that were just deleted and reporting a false partial
    /// failure. The Existed flags go true because "nothing recorded, nothing created" must not be
    /// read as "the harness created these files."
    /// </summary>
    internal void ClearRestoredState()
    {
        DiscordBackupFile = null;
        RulesBackupFile = null;
        DiscordExisted = true;
        RulesExisted = true;
        OriginalSettings.Clear();
        GrantedPluginIds.Clear();
    }
}
