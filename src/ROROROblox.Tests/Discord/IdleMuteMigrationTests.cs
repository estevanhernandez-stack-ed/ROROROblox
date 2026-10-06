using System.IO;
using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

/// <summary>
/// v1.33 item 3 — <c>MuteIdleAlerts</c> stops being a control and becomes a one-time migration.
/// <para>
/// The setting shipped in v1.8 as the only thing <c>IdleAlertPresenter</c> honoured: one bool, the
/// whole roster, nothing else. Idle alerts now route through the dispatcher like every other kind,
/// so "muted" is expressible properly — an empty <c>IdleDestinations</c> — and the bool's job is to
/// carry the one answer the user already gave across the upgrade. It must carry it exactly once: a
/// migration that re-ran would wipe idle destinations the user set AFTER upgrading, every restart,
/// forever.
/// </para>
/// <para>
/// The key itself stays on <c>SettingsBlob</c>. Removing a property from that record changes what
/// an older blob deserializes into, and there is nothing to gain by it — it is one bool. It goes on
/// <c>SettingsReachabilityTests</c>' allow-list instead, because item 6 takes its checkbox off the
/// page.
/// </para>
/// <para>
/// Written against the real <c>DiscordConfigStore</c> (DPAPI, a temp file) and the real
/// <c>AppSettings</c>, not fakes, because the half most likely to break is persistence: an empty
/// list that sprang back to <c>[Local]</c> on read would hand every muted user their alerts back
/// once per restart and the migration would look like it worked.
/// </para>
/// </summary>
public class IdleMuteMigrationTests : IDisposable
{
    private readonly string _dir;

    public IdleMuteMigrationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"rororo-idle-mute-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private AppSettings Settings() => new(Path.Combine(_dir, "settings.json"));

    private DiscordConfigStore Store() => new(Path.Combine(_dir, "discord.dat"));

    [Fact]
    public async Task AMutedUser_GetsAnExplicitlyEmptyIdleDestinationList()
    {
        using var settings = Settings();
        await settings.SetMuteIdleAlertsAsync(true);
        var config = new DiscordConfigService(Store());

        var migrated = await IdleMuteMigration.RunAsync(settings, config);

        Assert.True(migrated);
        Assert.Empty(config.Current.IdleDestinations);
        Assert.Empty(config.Current.DestinationsFor(AlertKind.AccountIdle));
    }

    [Fact]
    public async Task TheEmptyListSurvivesARestart()
    {
        // The guard this migration depends on, re-asserted from the migration's own side. An
        // ABSENT IdleDestinations key defaults to [Local] on purpose (item 1 — an upgrade must not
        // silence the toast that has shown since v1.8), so the migration's whole correctness rests
        // on SaveAsync writing the empty list PRESENT rather than absent.
        using var settings = Settings();
        await settings.SetMuteIdleAlertsAsync(true);
        await IdleMuteMigration.RunAsync(settings, new DiscordConfigService(Store()));

        var nextLaunch = new DiscordConfigService(Store());
        await nextLaunch.InitializeAsync();

        Assert.Empty(nextLaunch.Current.DestinationsFor(AlertKind.AccountIdle));
    }

    [Fact]
    public async Task ItRunsExactlyOnce_SoADestinationChosenAfterwardsSurvives()
    {
        // The failure this test exists for: a migration keyed on a flag it never clears re-runs on
        // every launch, so a user who upgrades, finds the new Alerts section and ticks Desktop for
        // idle is silenced again by the next restart and has no way to tell why.
        using var settings = Settings();
        await settings.SetMuteIdleAlertsAsync(true);
        var config = new DiscordConfigService(Store());
        await IdleMuteMigration.RunAsync(settings, config);

        Assert.False(await settings.GetMuteIdleAlertsAsync());

        // The user changes their mind, the way item 6's page will let them.
        await config.MutateAsync(c => c with { IdleDestinations = [AlertDestination.Local] });

        var ranAgain = await IdleMuteMigration.RunAsync(settings, config);

        Assert.False(ranAgain);
        Assert.Equal([AlertDestination.Local], config.Current.DestinationsFor(AlertKind.AccountIdle));
    }

    [Fact]
    public async Task AUserWhoNeverMutedIsLeftAlone()
    {
        using var settings = Settings();
        var config = new DiscordConfigService(Store());
        await config.InitializeAsync();

        var migrated = await IdleMuteMigration.RunAsync(settings, config);

        Assert.False(migrated);
        Assert.Equal([AlertDestination.Local], config.Current.DestinationsFor(AlertKind.AccountIdle));
        Assert.False(await settings.GetMuteIdleAlertsAsync());
    }

    [Fact]
    public async Task ItSilencesIdleAndNothingElse()
    {
        // The old flag was named for idle and only ever read on the idle path, so a migration that
        // touched another kind's routing would be inventing a preference the user never expressed.
        using var settings = Settings();
        await settings.SetMuteIdleAlertsAsync(true);
        var config = new DiscordConfigService(Store());
        await config.MutateAsync(c => c with
        {
            DroppedOutDestinations = [AlertDestination.Local, AlertDestination.Phone],
            MemoryWarningDestinations = [AlertDestination.Local],
            MineWebhookUrl = "https://discord.com/api/webhooks/1/mine",
        });

        await IdleMuteMigration.RunAsync(settings, config);

        Assert.Empty(config.Current.DestinationsFor(AlertKind.AccountIdle));
        Assert.Equal([AlertDestination.Local, AlertDestination.Phone],
            config.Current.DestinationsFor(AlertKind.AccountDroppedOut));
        Assert.Equal([AlertDestination.Local],
            config.Current.DestinationsFor(AlertKind.MemoryWarning));
        Assert.Equal("https://discord.com/api/webhooks/1/mine", config.Current.MineWebhookUrl);
    }

    [Fact]
    public async Task TheDestinationIsWrittenBeforeTheFlagIsCleared()
    {
        // Ordering, not tidiness. A crash between the two writes must leave the migration still
        // PENDING rather than lost: the flag is the only record that the user ever asked for
        // silence, so clearing it first and then failing to write the empty list would un-mute them
        // permanently with nothing left to replay. A store that throws stands in for the crash.
        using var settings = Settings();
        await settings.SetMuteIdleAlertsAsync(true);
        var config = new DiscordConfigService(new ThrowingStore());

        await Assert.ThrowsAsync<IOException>(() => IdleMuteMigration.RunAsync(settings, config));

        Assert.True(await settings.GetMuteIdleAlertsAsync());
    }

    private sealed class ThrowingStore : IDiscordConfigStore
    {
        public Task<DiscordConfig> LoadAsync() => Task.FromResult(new DiscordConfig());

        public Task SaveAsync(DiscordConfig config) => throw new IOException("discord.dat is locked.");
    }
}
