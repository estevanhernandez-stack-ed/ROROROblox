using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

/// <summary>
/// Multi-destination routing (Este's smoke feedback, 2026-09-05): an alert kind fans out to a
/// SET of destinations, the singular fields stay as the rollback mirror, and a pre-fanout blob
/// migrates on read. These pin the fan-out, the migration, and the dedupe-on-fallback rule.
/// </summary>
public class AlertFanoutTests
{
    private static AlertTrigger Dropped(Guid id, string name) =>
        new(AlertKind.AccountDroppedOut, id, name, name, null, null, DateTimeOffset.UnixEpoch);

    private static readonly Dictionary<(Guid, AlertKind), DateTimeOffset> NoHistory = [];

    [Fact]
    public void Route_MultipleDestinations_EmitsOneRoutedAlertPerDestination()
    {
        var config = new DiscordConfig
        {
            DroppedOutDestinations = [AlertDestination.Local, AlertDestination.Mine, AlertDestination.Phone],
            MineWebhookUrl = "https://discord.com/api/webhooks/1/tok",
        };

        var routed = AlertRouter.Route([Dropped(Guid.NewGuid(), "A")], config, NoHistory,
            DateTimeOffset.UnixEpoch, phoneConfigured: true);

        Assert.Equal(
            new[] { AlertDestination.Local, AlertDestination.Mine, AlertDestination.Phone },
            routed.Select(r => r.Destination).ToArray());
        Assert.All(routed, r => Assert.Single(r.Triggers));
    }

    [Fact]
    public void Route_UnconfiguredFallbacks_DedupeIntoOneDesktopAlert()
    {
        // My-channel with no webhook AND phone with no provider both fall back to Local, and
        // Local is also ticked — the desktop must be paged once, not three times.
        var config = new DiscordConfig
        {
            DroppedOutDestinations = [AlertDestination.Local, AlertDestination.Mine, AlertDestination.Phone],
        };

        var routed = AlertRouter.Route([Dropped(Guid.NewGuid(), "A")], config, NoHistory,
            DateTimeOffset.UnixEpoch, phoneConfigured: false);

        Assert.Equal(AlertDestination.Local, Assert.Single(routed).Destination);
    }

    [Fact]
    public void DestinationsFor_MigratesTheSingularFieldWhenTheListIsEmpty()
    {
        // A blob written before the fan-out shipped has only the singular field set.
        var config = new DiscordConfig { DroppedOutDestination = AlertDestination.Mine };

        Assert.Equal([AlertDestination.Mine], config.DestinationsFor(AlertKind.AccountDroppedOut));
        Assert.Empty(config.DestinationsFor(AlertKind.MemoryWarning));
    }

    [Fact]
    public void DestinationsFor_TheListWinsOverTheSingularMirror()
    {
        // Settings writes both; the singular is the rollback mirror (first entry), never the truth.
        var config = new DiscordConfig
        {
            DroppedOutDestination = AlertDestination.Local,
            DroppedOutDestinations = [AlertDestination.Phone, AlertDestination.Clan],
        };

        Assert.Equal(
            [AlertDestination.Phone, AlertDestination.Clan],
            config.DestinationsFor(AlertKind.AccountDroppedOut));
    }

    [Fact]
    public void StatusLine_NamesEveryFannedOutDestination()
    {
        var line = AlertStatusLine.Compose(
            new DiscordConfig
            {
                DroppedOutDestinations = [AlertDestination.Mine, AlertDestination.Phone],
                MineWebhookUrl = "https://discord.com/api/webhooks/1/tok",
            },
            mineChannelName: "alerts",
            phoneConfigured: true,
            phoneProviderName: "ntfy");

        Assert.False(line.IsFailure);
        Assert.Contains("#alerts", line.Text, StringComparison.Ordinal);
        Assert.Contains("your phone (ntfy)", line.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void PushoverIconResource_IsEmbedded()
    {
        // Settings > Alerts hands this file out for the pushover.net application form; the
        // csproj Link can silently break on a docs move, and the button would then throw at
        // the first click on someone's machine instead of here.
        using var stream = typeof(ROROROblox.App.Preferences.SettingsPage).Assembly
            .GetManifestResourceStream("ROROROblox.App.Notify.pushover-icon-128.png");

        Assert.NotNull(stream);
        Assert.True(stream!.Length > 500, "embedded icon is suspiciously small");
    }

    [Fact]
    public void DestinationsFor_NewKinds_ReadTheirListsWithNoLegacyMirror()
    {
        var config = new DiscordConfig
        {
            RecycledDestinations = [AlertDestination.Phone],
            UptimeMarkDestinations = [AlertDestination.Phone, AlertDestination.Local],
        };

        Assert.Equal([AlertDestination.Phone], config.DestinationsFor(AlertKind.Recycled));
        Assert.Equal(
            [AlertDestination.Phone, AlertDestination.Local],
            config.DestinationsFor(AlertKind.UptimeMark));
        Assert.Empty(new DiscordConfig().DestinationsFor(AlertKind.Recycled));
    }

    [Fact]
    public void Payload_Recycled_CarriesTheReclaimedRam()
    {
        var payload = WebhookPayload.ForAlert(AlertKind.Recycled,
            [new AlertTrigger(AlertKind.Recycled, Guid.NewGuid(), "BaronBloxwell", "Real",
                null, 4_402_341_478, DateTimeOffset.UnixEpoch)]);

        Assert.Equal("BaronBloxwell — recycled", payload.Title);
        Assert.Contains("4.1 GB", payload.Body, StringComparison.Ordinal);
        Assert.Contains("back in its server", payload.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Payload_UptimeMark_ReadsHoursAndCount()
    {
        // The tracker's caller composes the synthetic trigger: DisplayName carries the hours,
        // GameName the count. No identity in either — streamer mode has nothing to mask.
        var payload = WebhookPayload.ForAlert(AlertKind.UptimeMark,
            [new AlertTrigger(AlertKind.UptimeMark, Guid.Empty, "4h up", "4h up",
                "6 accounts in", null, DateTimeOffset.UnixEpoch)]);

        Assert.Equal("4h up — 6 accounts in", payload.Title);
        Assert.Contains("all-good mark", payload.Body, StringComparison.Ordinal);
    }

}
