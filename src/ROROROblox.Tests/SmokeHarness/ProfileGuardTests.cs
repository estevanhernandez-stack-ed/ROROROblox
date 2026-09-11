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
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* a leaked lock in a failing test must not mask the failure */ }
        catch (UnauthorizedAccessException) { }
    }

    private Task<ProfileGuard> AcquireAsync() => ProfileGuard.AcquireAsync(_dataRoot, _backupRoot, _log.Add);

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
        // Rewriting settings.json wholesale would discard anything the app wrote while the harness
        // ran, and the app rewrites that file on exit. Read-modify-write one key, or lose settings.
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
        // No RestoreAsync — simulating a kill.

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
        // Killed here.

        Assert.True(await ProfileGuard.RecoverOrphanedAsync(_backupRoot, _log.Add));

        Assert.False(await ReadSettingAsync(BooleanSetting.MetricAlertsEnabled));
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
    public async Task Acquire_RecoversAnOrphanFromTheLastRun_BeforeTakingItsOwnBackups()
    {
        // Without this, the second run backs up the ALREADY-BROKEN discord.dat, overwrites the good
        // backup with it, and the localhost URLs become the thing it faithfully restores.
        var original = await File.ReadAllBytesAsync(_discordPath);
        _ = await AcquireAsync();
        await File.WriteAllBytesAsync(_discordPath, [9, 9, 9]);   // killed run

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
        });

        Assert.True(await guard.VerifyDiscordSwapAsync("http://localhost:9/mine", "http://localhost:9/clan"));
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
        _ = await AcquireAsync();
        await File.WriteAllBytesAsync(_discordPath, [9, 9, 9]);

        using var _hold = File.Open(_discordPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await Assert.ThrowsAsync<InvalidOperationException>(AcquireAsync);
    }

    private async Task<bool> ReadSettingAsync(BooleanSetting setting)
    {
        using var settings = new Core.AppSettings(_settingsPath);
        return await setting.ReadAsync(settings);
    }
}
