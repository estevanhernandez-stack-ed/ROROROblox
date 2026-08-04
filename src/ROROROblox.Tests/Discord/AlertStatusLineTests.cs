using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

/// <summary>
/// These assert a promise, not a wording: in every state where nothing will actually reach the
/// user's phone, the line says so. The desktop notification is a floor that stops an alert being
/// dropped — it is not the feature, because someone sitting at the PC can already see that a
/// client died.
/// </summary>
public class AlertStatusLineTests
{
    private const string Webhook = "https://discord.com/api/webhooks/1/tok";

    [Fact]
    public void Compose_NothingRouted_SaysSoPlainly()
    {
        var line = AlertStatusLine.Compose(new DiscordConfig());

        Assert.Contains("No alerts yet", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compose_DesktopOnly_AdmitsNothingReachesThePhone()
    {
        // The state most likely to be mistaken for "set up and working."
        var line = AlertStatusLine.Compose(new DiscordConfig
        {
            DroppedOutDestination = AlertDestination.Local,
        });

        Assert.Contains("phone", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nothing", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compose_RoutedToAChannelWithNoWebhook_SaysItWontHelpAwayFromThePC()
    {
        var line = AlertStatusLine.Compose(new DiscordConfig
        {
            DroppedOutDestination = AlertDestination.Mine,
            MineWebhookUrl = null,
        });

        Assert.Contains("haven't pasted a webhook", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("away from it", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compose_ADeletedWebhook_IsTheLoudestThingTheLineSays()
    {
        // A 404'd webhook has NO other discovery path. Discord never tells the user they deleted
        // it, alerts fall back to desktop, and every control still reads "My channel" — so the
        // setup looks correct while nothing arrives. If this line is polite about it, the user
        // finds out the next time something goes wrong while they are out, which is the exact
        // moment the feature existed to cover.
        var line = AlertStatusLine.Compose(
            new DiscordConfig { DroppedOutDestination = AlertDestination.Mine, MineWebhookUrl = Webhook },
            mineWebhookRejected: true);

        Assert.Contains("deleted", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nothing is reaching your phone", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compose_ADeletedWebhookForARouteNobodyUses_IsNotReported()
    {
        // The clan webhook died but nothing routes there any more. Shouting about it would train
        // the user to ignore this line, which costs us the case above.
        var line = AlertStatusLine.Compose(
            new DiscordConfig
            {
                DroppedOutDestination = AlertDestination.Mine,
                MineWebhookUrl = Webhook,
                ClanWebhookUrl = Webhook,
            },
            clanWebhookRejected: true);

        Assert.DoesNotContain("deleted", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compose_Working_NamesTheChannelItPostsTo()
    {
        // So a clan webhook pasted into the personal slot is visible BEFORE the first alert lands
        // in the wrong channel.
        var line = AlertStatusLine.Compose(
            new DiscordConfig { DroppedOutDestination = AlertDestination.Mine, MineWebhookUrl = Webhook },
            mineChannelName: "rororo-alerts");

        Assert.Equal("Sending to #rororo-alerts.", line);
    }

    [Fact]
    public void Compose_WorkingButTheChannelIsUnknown_StillReadsAsWorking()
    {
        // The probe is best-effort — offline, or Discord slow. Not knowing the channel name is
        // not the same as not being configured, and must not be reported as a problem.
        var line = AlertStatusLine.Compose(
            new DiscordConfig { DroppedOutDestination = AlertDestination.Mine, MineWebhookUrl = Webhook });

        Assert.Contains("Sending", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nothing", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compose_OneTriggerToTheChannelAndOneToDesktop_ReportsTheChannel()
    {
        // Mixed routing still reaches the phone for the trigger that matters, so this is not the
        // "desktop only" warning case.
        var line = AlertStatusLine.Compose(new DiscordConfig
        {
            DroppedOutDestination = AlertDestination.Mine,
            MemoryWarningDestination = AlertDestination.Local,
            MineWebhookUrl = Webhook,
        });

        Assert.Contains("Sending", line, StringComparison.OrdinalIgnoreCase);
    }
}
