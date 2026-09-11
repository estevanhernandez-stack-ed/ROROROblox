using System.Text.Json;
using ROROROblox.App.Plugins;
using ROROROblox.Core.Discord;
using ROROROblox.MetricSmoke;

namespace ROROROblox.Tests.SmokeHarness;

/// <summary>
/// The guard writes the user's REAL profile when the harness runs for real — settings, plugin
/// consent, and the DPAPI envelope holding two Discord webhook URLs. So every test here works
/// against a temp directory standing in for the data root, and the seeded <c>discord.dat</c> is a
/// genuine envelope written through <see cref="DiscordConfigStore"/> rather than a stub file: a
/// backup/restore that only ever round-trips bytes we invented would not prove the real format
/// survives.
/// <para>
/// Where a test simulates a killed run it calls <see cref="ProfileGuard.Dispose"/> without restoring.
/// That is what a kill actually is: the OS closes the process's handles, so its ownership goes, and
/// nothing restores. Leaving the guard alive instead would be simulating a hang, not a crash, and the
/// ownership check would (correctly) refuse to recover underneath it.
/// </para>
/// </summary>
public sealed class ProfileGuardTests : IDisposable
{
    private const string SeededMineUrl = "https://discord.com/api/webhooks/1/seeded-mine";
    private const string SeededClanUrl = "https://discord.com/api/webhooks/2/seeded-clan";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rororo-smoke-{Guid.NewGuid():N}");
    private readonly string _dataRoot;
    private readonly string _backupRoot;
    private readonly string _discordPath;
    private readonly string _settingsPath;
    private readonly string _rulesPath;
    private readonly string _consentPath;
    private readonly List<string> _log = [];
    private readonly List<ProfileGuard> _guards = [];

    public ProfileGuardTests()
    {
        _dataRoot = Path.Combine(_root, "data");
        _backupRoot = Path.Combine(_root, "backup");
        Directory.CreateDirectory(_dataRoot);

        _discordPath = Path.Combine(_dataRoot, "discord.dat");
        _settingsPath = Path.Combine(_dataRoot, "settings.json");
        _rulesPath = Path.Combine(_dataRoot, "metric-rules.json");
        _consentPath = Path.Combine(_dataRoot, "consent.dat");

        // A real envelope, written by the production store, holding both webhook URLs and a
        // routing set — the shape the guard has to hand back untouched.
        new DiscordConfigStore(_discordPath).SaveAsync(new DiscordConfig
        {
            PresenceEnabled = true,
            MineWebhookUrl = SeededMineUrl,
            ClanWebhookUrl = SeededClanUrl,
            DroppedOutDestination = AlertDestination.Clan,
            MetricBreachDestinations = [AlertDestination.Mine],
        }).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        foreach (var guard in _guards) guard.Dispose();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* a leaked lock in a failing test must not mask the failure */ }
        catch (UnauthorizedAccessException) { }
    }

    private async Task<ProfileGuard> AcquireAsync()
    {
        var guard = await ProfileGuard.AcquireAsync(_dataRoot, _backupRoot, _log.Add);
        _guards.Add(guard);
        return guard;
    }

    [Fact]
    public async Task Acquire_WritesAMarker_AndRestore_RemovesIt()
    {
        // The marker is what makes an interrupted run recoverable. If it is absent, a crash is
        // indistinguishable from a clean exit and the next run has nothing to restore from.
        var guard = await AcquireAsync();
        Assert.True(File.Exists(Path.Combine(_backupRoot, ProfileGuard.MarkerFileName)));

        await guard.RestoreAsync();
        Assert.False(File.Exists(Path.Combine(_backupRoot, ProfileGuard.MarkerFileName)));
    }

    [Fact]
    public async Task Restore_PutsDiscordConfigBackByteForByte()
    {
        var original = await File.ReadAllBytesAsync(_discordPath);

        var guard = await AcquireAsync();
        await File.WriteAllBytesAsync(_discordPath, [1, 2, 3]);
        await guard.RestoreAsync();

        Assert.Equal(original, await File.ReadAllBytesAsync(_discordPath));
    }

    [Fact]
    public async Task Restore_LeavesTheRestoredEnvelopeReadableByTheProductionStore()
    {
        // Byte-for-byte is the assertion above; this one says the bytes still decrypt and still
        // carry both URLs, because "restored" that the app cannot read is not restored.
        var guard = await AcquireAsync();
        await guard.MutateDiscordAsync(c => c with
        {
            MineWebhookUrl = "http://localhost:9/mine",
            ClanWebhookUrl = "http://localhost:9/clan",
        });
        await guard.RestoreAsync();

        var reloaded = await new DiscordConfigStore(_discordPath).LoadAsync();
        Assert.Equal(SeededMineUrl, reloaded.MineWebhookUrl);
        Assert.Equal(SeededClanUrl, reloaded.ClanWebhookUrl);
        Assert.Equal(AlertDestination.Clan, reloaded.DroppedOutDestination);
    }

    [Fact]
    public async Task AnAbsentDiscordFile_IsDeletedOnRestore_NotLeftBehind()
    {
        // A profile that never had Discord configured must not gain a discord.dat because the
        // harness ran. Symmetric with the rules file below.
        File.Delete(_discordPath);

        var guard = await AcquireAsync();
        await guard.MutateDiscordAsync(c => c with { MineWebhookUrl = "http://localhost:9/mine" });
        Assert.True(File.Exists(_discordPath));

        await guard.RestoreAsync();
        Assert.False(File.Exists(_discordPath));
    }

    [Fact]
    public async Task Restore_PutsBackOnlyTheOneSettingsKey()
    {
        // What this shows: the restore goes through AppSettings' read-modify-write, so a key the
        // harness never touched still has its value afterwards. It does NOT show that an arbitrary
        // unknown key survives — AppSettings deserializes into SettingsBlob and reserializes, so a
        // key outside that record is dropped by production itself, and no guard could keep it. The
        // point being pinned is the one that is ours: we never stash and rewrite the file wholesale,
        // which would discard what the app wrote while the harness ran.
        await File.WriteAllTextAsync(_settingsPath,
            """{ "version": 1, "metricAlertsEnabled": false, "streamerMode": true }""");

        var guard = await AcquireAsync();
        await guard.SetMetricAlertsEnabledAsync(true);
        await guard.RestoreAsync();

        var json = await File.ReadAllTextAsync(_settingsPath);
        Assert.Contains("\"streamerMode\": true", json);
        Assert.Contains("\"metricAlertsEnabled\": false", json);
    }

    [Fact]
    public async Task TheSameSingleKeyPath_SavesAndRestoresStreamerMode()
    {
        // The save-and-restore is general, not welded to the metric gate: a later harness row needs
        // streamer mode on and put back, and that must not arrive as a second hand-rolled copy.
        await File.WriteAllTextAsync(_settingsPath,
            """{ "version": 1, "metricAlertsEnabled": true, "streamerMode": false }""");

        var guard = await AcquireAsync();
        await guard.SetBooleanSettingAsync(BooleanSetting.StreamerMode, true);
        Assert.True(await ReadSettingAsync(BooleanSetting.StreamerMode));

        await guard.RestoreAsync();

        Assert.False(await ReadSettingAsync(BooleanSetting.StreamerMode));
        Assert.True(await ReadSettingAsync(BooleanSetting.MetricAlertsEnabled));
    }

    [Fact]
    public async Task TwoSettingsChangedInOneRun_AreBothPutBack()
    {
        await File.WriteAllTextAsync(_settingsPath,
            """{ "version": 1, "metricAlertsEnabled": false, "streamerMode": false }""");

        var guard = await AcquireAsync();
        await guard.SetBooleanSettingAsync(BooleanSetting.MetricAlertsEnabled, true);
        await guard.SetBooleanSettingAsync(BooleanSetting.StreamerMode, true);
        await guard.RestoreAsync();

        Assert.False(await ReadSettingAsync(BooleanSetting.MetricAlertsEnabled));
        Assert.False(await ReadSettingAsync(BooleanSetting.StreamerMode));
    }

    [Fact]
    public async Task ASettingToggledTwice_RestoresTheValueTheUserHad_NotTheIntermediateOne()
    {
        // The gate scenarios toggle the same key on and off. Capturing the original on every write
        // would make the last harness value the "original" and leave the profile changed.
        await File.WriteAllTextAsync(_settingsPath,
            """{ "version": 1, "metricAlertsEnabled": false }""");

        var guard = await AcquireAsync();
        await guard.SetMetricAlertsEnabledAsync(true);
        await guard.SetMetricAlertsEnabledAsync(false);
        await guard.SetMetricAlertsEnabledAsync(true);
        await guard.RestoreAsync();

        Assert.False(await ReadSettingAsync(BooleanSetting.MetricAlertsEnabled));
    }

    [Fact]
    public async Task AnAbsentSettingsFile_IsNotCreatedByARunThatNeverTouchedIt()
    {
        // AppSettings.LoadAsync answers a missing file with a defaults blob and SaveAsync writes the
        // whole thing, so a restore that "puts a value back" unconditionally would conjure a
        // settings.json onto a profile that never had one.
        Assert.False(File.Exists(_settingsPath));

        var guard = await AcquireAsync();
        await guard.WriteRulesAsync("[]");
        var report = await guard.RestoreAsync();

        Assert.True(report.Complete);
        Assert.False(File.Exists(_settingsPath));
    }

    [Fact]
    public async Task AnAbsentSettingsFile_ThatTheHarnessHadToCreate_ComesBackWithTheKeyAtItsDefault()
    {
        // The other half, and the honest limit: flipping the gate on a profile with no settings.json
        // necessarily creates one, because that is what the app's own accessor does. The restore puts
        // the key back to the default it had, and does NOT delete the file — the app may legitimately
        // have written its own settings into it while the harness ran.
        Assert.False(File.Exists(_settingsPath));

        var guard = await AcquireAsync();
        await guard.SetMetricAlertsEnabledAsync(true);
        await guard.RestoreAsync();

        // Both halves asserted, because "the key reads false" is also what a DELETED file reads.
        Assert.True(File.Exists(_settingsPath));
        Assert.False(await ReadSettingAsync(BooleanSetting.MetricAlertsEnabled));
    }

    [Fact]
    public async Task Acquire_RefusesWhenSettingsWillNotParse_AndLeavesNothingBehind()
    {
        // The trap this whole class is built around: AppSettings turns a JsonException into a fresh
        // defaults blob, so a read would answer "false" and the restore would persist defaults over
        // the real file. Refuse at the door instead.
        await File.WriteAllTextAsync(_settingsPath, "{ this is not json");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ProfileGuard.AcquireAsync(_dataRoot, _backupRoot, _log.Add));

        Assert.False(File.Exists(Path.Combine(_backupRoot, ProfileGuard.MarkerFileName)));
        Assert.Equal("{ this is not json", await File.ReadAllTextAsync(_settingsPath));
    }

    [Fact]
    public async Task ASettingsFileThatGoesUnreadableMidRun_IsNotCapturedAsAFabricatedOriginal()
    {
        var guard = await AcquireAsync();
        await File.WriteAllTextAsync(_settingsPath, "{ torn");
        var before = await File.ReadAllBytesAsync(_settingsPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.SetMetricAlertsEnabledAsync(true));

        Assert.Equal(before, await File.ReadAllBytesAsync(_settingsPath));
    }

    [Fact]
    public async Task ASettingsFileThatGoesUnreadableBeforeTheRestore_IsReported_NotOverwrittenWithDefaults()
    {
        // The race made deterministic: the app rewrites settings.json on exit, so a restore read can
        // lose it. AppSettings would answer with defaults and then SAVE them over the real file.
        await File.WriteAllTextAsync(_settingsPath,
            """{ "version": 1, "metricAlertsEnabled": false, "streamerMode": true }""");
        var guard = await AcquireAsync();
        await guard.SetMetricAlertsEnabledAsync(true);

        await File.WriteAllTextAsync(_settingsPath, "{ torn");
        var before = await File.ReadAllBytesAsync(_settingsPath);

        var report = await guard.RestoreAsync();

        Assert.False(report.Complete);
        Assert.Contains(report.Failed, f => f.Contains("metricAlertsEnabled", StringComparison.Ordinal));
        Assert.Equal(before, await File.ReadAllBytesAsync(_settingsPath));
    }

    [Fact]
    public async Task AnAbsentRulesFile_IsDeletedOnRestore_NotLeftBehind()
    {
        Assert.False(File.Exists(_rulesPath));

        var guard = await AcquireAsync();
        await guard.WriteRulesAsync("[]");
        await guard.RestoreAsync();

        Assert.False(File.Exists(_rulesPath));
    }

    [Fact]
    public async Task AnExistingRulesFile_IsRestored_NotClobbered()
    {
        await File.WriteAllTextAsync(_rulesPath, "ORIGINAL");

        var guard = await AcquireAsync();
        await guard.WriteRulesAsync("[]");
        await guard.RestoreAsync();

        Assert.Equal("ORIGINAL", await File.ReadAllTextAsync(_rulesPath));
    }

    [Fact]
    public async Task Restore_RevokesTheHarnessConsent_AndLeavesEveryOtherPluginAlone()
    {
        // consent.dat is self-cleaning by design (spec 1.4): no backup, because the harness only
        // ever adds and removes its own id. The "leaves others alone" half is the part that would
        // hurt if it were wrong — Este's real grants live in that file.
        var consent = new ConsentStore(_consentPath);
        await consent.GrantAsync("rororo.urtask", ["host.accounts.read"]);

        var guard = await AcquireAsync();
        await guard.GrantConsentAsync("rororo.smoke", ["host.metrics.report"]);
        Assert.Contains(await consent.ListAsync(), r => r.PluginId == "rororo.smoke");

        await guard.RestoreAsync();

        var records = await consent.ListAsync();
        Assert.DoesNotContain(records, r => r.PluginId == "rororo.smoke");
        Assert.Contains(records, r => r.PluginId == "rororo.urtask");
    }

    [Fact]
    public async Task AnOrphanedMarker_IsDetectedAndRestoredBeforeAnythingElse()
    {
        // The Ctrl-C case. This is spec section 1.6 and it is worthless untested.
        var original = await File.ReadAllBytesAsync(_discordPath);
        var guard = await AcquireAsync();
        await File.WriteAllBytesAsync(_discordPath, [9, 9, 9]);
        guard.Dispose();        // the kill: handles closed, no RestoreAsync

        var recovered = await ProfileGuard.RecoverOrphanedAsync(_backupRoot, _log.Add);

        Assert.True(recovered);
        Assert.Equal(original, await File.ReadAllBytesAsync(_discordPath));
        Assert.False(File.Exists(Path.Combine(_backupRoot, ProfileGuard.MarkerFileName)));
    }

    [Fact]
    public async Task AnOrphanedMarker_CarriesTheSettingsKeyAndTheRulesFileToo_NotJustDiscord()
    {
        // The marker IS the restore manifest. A crash after the gate was flipped must not leave the
        // gate flipped just because the value was only ever held in the dead process's memory.
        await File.WriteAllTextAsync(_settingsPath, """{ "version": 1, "metricAlertsEnabled": false }""");
        await File.WriteAllTextAsync(_rulesPath, "ORIGINAL");

        var guard = await AcquireAsync();
        await guard.SetMetricAlertsEnabledAsync(true);
        await guard.WriteRulesAsync("[]");
        guard.Dispose();        // killed here

        Assert.True(await ProfileGuard.RecoverOrphanedAsync(_backupRoot, _log.Add));

        Assert.False(await ReadSettingAsync(BooleanSetting.MetricAlertsEnabled));
        Assert.Equal("ORIGINAL", await File.ReadAllTextAsync(_rulesPath));
    }

    [Fact]
    public async Task AMarkerNamingNoBackups_MeansNothingWasMutated_SoRecoveryLeavesTheProfileAlone()
    {
        // The window between claiming the marker and finishing the copies. The marker says the files
        // existed but names no backup, and that must NOT be read as "the harness created them" —
        // reading it that way would delete a discord.dat the run had not even copied yet.
        var original = await File.ReadAllBytesAsync(_discordPath);
        await File.WriteAllTextAsync(_rulesPath, "ORIGINAL");
        Directory.CreateDirectory(_backupRoot);
        await File.WriteAllTextAsync(
            Path.Combine(_backupRoot, ProfileGuard.MarkerFileName),
            JsonSerializer.Serialize(new SmokeRunMarker
            {
                DataRoot = _dataRoot,
                StartedUtc = DateTimeOffset.UtcNow.ToString("O"),
                DiscordExisted = true,
                RulesExisted = true,
                SettingsExisted = false,
            }));

        Assert.True(await ProfileGuard.RecoverOrphanedAsync(_backupRoot, _log.Add));

        Assert.Equal(original, await File.ReadAllBytesAsync(_discordPath));
        Assert.Equal("ORIGINAL", await File.ReadAllTextAsync(_rulesPath));
    }

    [Fact]
    public async Task RecoverOrphaned_WithNoMarker_ReturnsFalse_AndTouchesNothing()
    {
        Directory.CreateDirectory(_backupRoot);
        var before = await File.ReadAllBytesAsync(_discordPath);

        Assert.False(await ProfileGuard.RecoverOrphanedAsync(_backupRoot, _log.Add));

        Assert.Equal(before, await File.ReadAllBytesAsync(_discordPath));
    }

    [Fact]
    public async Task RecoverOrphaned_RefusesWhileARunIsStillLive()
    {
        // --recover fired from another terminal against a run that is mid-scenario. Restoring under
        // it would hand the profile back while the runner is still driving it, and the live run would
        // then restore ITS localhost URLs over the real ones and delete the marker — the exact
        // outcome the marker exists to prevent.
        var guard = await AcquireAsync();
        await guard.MutateDiscordAsync(c => c with { MineWebhookUrl = "http://localhost:9/mine" });

        Assert.False(await ProfileGuard.RecoverOrphanedAsync(_backupRoot, _log.Add));

        Assert.True(File.Exists(Path.Combine(_backupRoot, ProfileGuard.MarkerFileName)));
        Assert.Contains(_log, l => l.Contains("in flight", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASecondOverlappingRun_IsRefused_RatherThanRacingTheFirstOnesBackups()
    {
        // Without ownership both runs back up whatever the other has already swapped, and whichever
        // restores last writes localhost URLs back as though they were the user's.
        var first = await AcquireAsync();
        await first.MutateDiscordAsync(c => c with { MineWebhookUrl = "http://localhost:9/mine" });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ProfileGuard.AcquireAsync(_dataRoot, _backupRoot, _log.Add));

        // And the first run can still put everything back.
        var report = await first.RestoreAsync();
        Assert.True(report.Complete);
        Assert.Equal(SeededMineUrl, (await new DiscordConfigStore(_discordPath).LoadAsync()).MineWebhookUrl);
    }

    [Fact]
    public async Task RecoverOrphaned_SeesTheLiveRun_EvenWhenTheBackupRootIsSpeltDifferently()
    {
        // The ownership name is a hash of this string, so two spellings of one folder are two
        // different runs as far as the semaphore is concerned — and the second one would happily
        // restore underneath the first. Path.GetFullPath alone does not close this: it KEEPS a
        // trailing separator. Task 5 builds this path rather than taking the default, which is what
        // makes the spelling something other than a hypothetical.
        var guard = await AcquireAsync();
        await guard.MutateDiscordAsync(c => c with { MineWebhookUrl = "http://localhost:9/mine" });

        var trailing = _backupRoot + Path.DirectorySeparatorChar;
        var roundabout = Path.Combine(_backupRoot, "..", Path.GetFileName(_backupRoot));

        Assert.False(await ProfileGuard.RecoverOrphanedAsync(trailing, _log.Add));
        Assert.False(await ProfileGuard.RecoverOrphanedAsync(roundabout, _log.Add));

        Assert.True((await guard.RestoreAsync()).Complete);
        Assert.Equal(SeededMineUrl, (await new DiscordConfigStore(_discordPath).LoadAsync()).MineWebhookUrl);
    }

    [Fact]
    public async Task ALockedDiscordFile_IsReportedAsLocked_NotAsADecryptionFailure()
    {
        // The harness runs alongside the app, and the app writes this file, so a momentary lock is
        // not hypothetical. Refusing is right; telling the user their DPAPI key no longer works
        // points them at something frightening and wrong.
        var guard = await AcquireAsync();

        using var _hold = File.Open(_discordPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            guard.MutateDiscordAsync(c => c with { MineWebhookUrl = "http://localhost:9/mine" }));

        Assert.Contains("open by another process", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("DPAPI", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALockedDiscordFile_DoesNotMakeTheReadBackClaimTheConfigIsCorrupt()
    {
        var guard = await AcquireAsync();

        using var _hold = File.Open(_discordPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.False(await guard.VerifyDiscordSwapAsync("http://localhost:9/mine", "http://localhost:9/clan"));
        Assert.Contains(_log, l => l.Contains("open by another process", StringComparison.Ordinal));
        Assert.DoesNotContain(_log, l => l.Contains("will not decrypt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Acquire_RecoversAnOrphanFromTheLastRun_BeforeTakingItsOwnBackups()
    {
        // Without this, the second run backs up the ALREADY-BROKEN discord.dat, overwrites the good
        // backup with it, and the localhost URLs become the thing it faithfully restores.
        var original = await File.ReadAllBytesAsync(_discordPath);
        var killed = await AcquireAsync();
        await File.WriteAllBytesAsync(_discordPath, [9, 9, 9]);
        killed.Dispose();

        var second = await AcquireAsync();
        await second.RestoreAsync();

        Assert.Equal(original, await File.ReadAllBytesAsync(_discordPath));
    }

    [Fact]
    public async Task VerifyDiscordSwap_ReturnsFalse_WhenTheWriteDidNotLand()
    {
        // Spec section 1.5. The only risk with an audience is a test alert reaching the real clan
        // channel because the swap silently failed, so the runner must be able to detect that.
        var guard = await AcquireAsync();

        Assert.False(await guard.VerifyDiscordSwapAsync("http://localhost:9/mine", "http://localhost:9/clan"));
    }

    [Fact]
    public async Task VerifyDiscordSwap_ReturnsTrue_OnceTheSwapHasLanded()
    {
        // The other half: a read-back that answers false unconditionally would abort every run and
        // teach the runner to ignore it.
        var guard = await AcquireAsync();
        await guard.MutateDiscordAsync(c => c with
        {
            MineWebhookUrl = "http://localhost:9/mine",
            ClanWebhookUrl = "http://localhost:9/clan",
            MetricBreachDestinations = [AlertDestination.Mine, AlertDestination.Clan],
        });

        Assert.True(await guard.VerifyDiscordSwapAsync("http://localhost:9/mine", "http://localhost:9/clan"));
    }

    [Fact]
    public async Task VerifyDiscordSwap_ReturnsFalse_WhenTheUrlsLandedOnABlankedConfig()
    {
        // What a swallowed CryptographicException looks like from outside: a perfectly valid envelope
        // holding a DEFAULT config plus the new URLs. Both URLs match, and the presence toggle, the
        // dropped-out destination and the muted list are gone. "Swapped" and "blanked and swapped"
        // must not both read as success.
        var guard = await AcquireAsync();
        await new DiscordConfigStore(_discordPath).SaveAsync(new DiscordConfig
        {
            MineWebhookUrl = "http://localhost:9/mine",
            ClanWebhookUrl = "http://localhost:9/clan",
        });

        Assert.False(await guard.VerifyDiscordSwapAsync("http://localhost:9/mine", "http://localhost:9/clan"));
        Assert.Contains(_log, l => l.Contains("not what it was at", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Acquire_RefusesWhenDiscordConfigWillNotDecrypt_AndLeavesNothingBehind()
    {
        // DiscordConfigStore answers a CryptographicException with an empty config, so a harness that
        // went ahead would write its URLs into a blank and report a clean swap.
        await File.WriteAllBytesAsync(_discordPath, [1, 2, 3]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ProfileGuard.AcquireAsync(_dataRoot, _backupRoot, _log.Add));

        Assert.False(File.Exists(Path.Combine(_backupRoot, ProfileGuard.MarkerFileName)));
    }

    [Fact]
    public async Task ADiscordFileThatStopsDecryptingMidRun_IsNotOverwrittenWithABlankConfig()
    {
        var guard = await AcquireAsync();
        await File.WriteAllBytesAsync(_discordPath, [1, 2, 3]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            guard.MutateDiscordAsync(c => c with { MineWebhookUrl = "http://localhost:9/mine" }));

        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(_discordPath));

        // And the backup still puts the real one back.
        Assert.True((await guard.RestoreAsync()).Complete);
        Assert.Equal(SeededMineUrl, (await new DiscordConfigStore(_discordPath).LoadAsync()).MineWebhookUrl);
    }

    [Fact]
    public async Task ABackupDeletedMidRun_StopsTheNextMutation_InsteadOfLettingItProceedUnprotected()
    {
        // SaveMarkerAsync recreates the directory, so without a re-check the marker would come back
        // while the backups did not, and the run would keep mutating a profile it could not put back.
        var guard = await AcquireAsync();
        Directory.Delete(_backupRoot, recursive: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.SetMetricAlertsEnabledAsync(true));
    }

    [Fact]
    public async Task APartialRestore_DoesNotThrow_NamesWhatItCouldNotPutBack_AndKeepsTheMarker()
    {
        // "Nothing may throw past RestoreAsync." A restore that dies on the first locked file
        // abandons the rest of the profile; one that reports honestly can be re-run.
        await File.WriteAllTextAsync(_settingsPath, """{ "version": 1, "metricAlertsEnabled": false }""");
        var guard = await AcquireAsync();
        await guard.SetMetricAlertsEnabledAsync(true);

        RestoreReport report;
        using (File.Open(_discordPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            report = await guard.RestoreAsync();
        }

        Assert.False(report.Complete);
        Assert.Contains(report.Failed, f => f.Contains("discord.dat", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Restored, r => r.Contains("metricAlertsEnabled", StringComparison.Ordinal));
        // Kept on purpose: the marker is the only way the next run knows there is work left.
        Assert.True(File.Exists(Path.Combine(_backupRoot, ProfileGuard.MarkerFileName)));
        // And the half that did work, worked.
        Assert.False(await ReadSettingAsync(BooleanSetting.MetricAlertsEnabled));
    }

    [Fact]
    public async Task ASecondRestore_FinishesWhatTheFirstOneCouldNot()
    {
        await File.WriteAllTextAsync(_settingsPath, """{ "version": 1, "metricAlertsEnabled": false }""");
        var original = await File.ReadAllBytesAsync(_discordPath);
        var guard = await AcquireAsync();
        await guard.SetMetricAlertsEnabledAsync(true);
        await File.WriteAllBytesAsync(_discordPath, [9, 9, 9]);

        using (File.Open(_discordPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await guard.RestoreAsync();
        }

        var second = await guard.RestoreAsync();

        Assert.True(second.Complete);
        Assert.Equal(original, await File.ReadAllBytesAsync(_discordPath));
        Assert.False(File.Exists(Path.Combine(_backupRoot, ProfileGuard.MarkerFileName)));
    }

    [Fact]
    public async Task ASecondRestoreAfterASuccessfulOne_SaysThereIsNothingLeft_NotPartialFailure()
    {
        // The runner will have an explicit restore AND a finally, so this fires on every normal run.
        // Answering it with the scariest message the tool prints — plus a claim that the backups are
        // still there, when they were just deleted — teaches a reader to ignore the one message that
        // means real trouble.
        await File.WriteAllTextAsync(_settingsPath, """{ "version": 1, "metricAlertsEnabled": false }""");
        var guard = await AcquireAsync();
        await guard.SetMetricAlertsEnabledAsync(true);
        await guard.WriteRulesAsync("[]");

        Assert.True((await guard.RestoreAsync()).Complete);

        var second = await guard.RestoreAsync();

        Assert.True(second.Complete);
        Assert.Empty(second.Failed);
        Assert.DoesNotContain("PARTIAL", second.ToString(), StringComparison.Ordinal);
        Assert.False(await ReadSettingAsync(BooleanSetting.MetricAlertsEnabled));
    }

    [Fact]
    public async Task Restore_DeletesTheDiscordBackup_SoRealWebhookUrlsDoNotLinger()
    {
        var guard = await AcquireAsync();
        var backup = Directory.GetFiles(_backupRoot, "discord.dat*");
        Assert.NotEmpty(backup);

        await guard.RestoreAsync();

        Assert.Empty(Directory.GetFiles(_backupRoot, "discord.dat*"));
    }

    [Fact]
    public async Task Acquire_RefusesToRunOverAnOrphanItCouldNotFullyRecover()
    {
        // The one case where throwing is right: proceeding would overwrite the good backup with the
        // broken file. Acquire may throw; RestoreAsync may not.
        var killed = await AcquireAsync();
        await File.WriteAllBytesAsync(_discordPath, [9, 9, 9]);
        killed.Dispose();

        using var _hold = File.Open(_discordPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ProfileGuard.AcquireAsync(_dataRoot, _backupRoot, _log.Add));
    }

    private async Task<bool> ReadSettingAsync(BooleanSetting setting)
    {
        using var settings = new Core.AppSettings(_settingsPath);
        return await setting.ReadAsync(settings);
    }
}
