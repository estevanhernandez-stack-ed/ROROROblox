using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

public class AlertRouterTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 3, 14, 0, TimeSpan.Zero);
    private static readonly Guid AccountA = Guid.NewGuid();
    private static readonly Guid AccountB = Guid.NewGuid();

    private static AlertTrigger Trigger(AlertKind kind, Guid id, string name, DateTimeOffset? at = null) =>
        new(kind, id, name, "Pet Simulator 99!", 4_000_000_000, at ?? Now);

    private static readonly Dictionary<Guid, DateTimeOffset> NothingSentYet = new();

    [Fact]
    public void Route_TriggerSetToNone_ProducesNothing()
    {
        // The default. Nothing outbound until the user configures it.
        var routed = AlertRouter.Route(
            [Trigger(AlertKind.AccountDroppedOut, AccountA, "A")],
            new DiscordConfig(), NothingSentYet, Now);

        Assert.Empty(routed);
    }

    [Fact]
    public void Route_SendsEachTriggerToItsOwnConfiguredDestination()
    {
        // The whole point of per-trigger routing: health to me, other things elsewhere.
        var config = new DiscordConfig
        {
            DroppedOutDestination = AlertDestination.Mine,
            MemoryWarningDestination = AlertDestination.Local,
            MineWebhookUrl = "https://discord.com/api/webhooks/1/tok",
        };

        var routed = AlertRouter.Route(
            [Trigger(AlertKind.AccountDroppedOut, AccountA, "A"), Trigger(AlertKind.MemoryWarning, AccountB, "B")],
            config, NothingSentYet, Now);

        Assert.Equal(2, routed.Count);
        Assert.Equal(AlertDestination.Mine, routed.Single(r => r.Kind == AlertKind.AccountDroppedOut).Destination);
        Assert.Equal(AlertDestination.Local, routed.Single(r => r.Kind == AlertKind.MemoryWarning).Destination);
    }

    [Fact]
    public void Route_MutedAccount_ProducesNothingForThatAccountOnly()
    {
        var config = new DiscordConfig
        {
            DroppedOutDestination = AlertDestination.Local,
            MutedAccountIds = [AccountA],
        };

        var routed = AlertRouter.Route(
            [Trigger(AlertKind.AccountDroppedOut, AccountA, "Muted"),
             Trigger(AlertKind.AccountDroppedOut, AccountB, "Loud")],
            config, NothingSentYet, Now);

        var alert = Assert.Single(routed);
        Assert.Equal("Loud", Assert.Single(alert.Triggers).DisplayName);
    }

    [Fact]
    public void Route_ManyAccountsSameKind_CoalescesIntoOneAlert()
    {
        // Eight accounts crossing the memory threshold in one sweep is one message.
        var config = new DiscordConfig { MemoryWarningDestination = AlertDestination.Local };

        var routed = AlertRouter.Route(
            [Trigger(AlertKind.MemoryWarning, AccountA, "A"), Trigger(AlertKind.MemoryWarning, AccountB, "B")],
            config, NothingSentYet, Now);

        Assert.Equal(2, Assert.Single(routed).Triggers.Count);
    }

    [Fact]
    public void Route_AccountAlertedInsideTheCooldown_IsSuppressed()
    {
        // A flapping client must not page someone every thirty seconds.
        var config = new DiscordConfig { DroppedOutDestination = AlertDestination.Local };
        var lastSent = new Dictionary<Guid, DateTimeOffset> { [AccountA] = Now.AddMinutes(-1) };

        var routed = AlertRouter.Route(
            [Trigger(AlertKind.AccountDroppedOut, AccountA, "A")], config, lastSent, Now);

        Assert.Empty(routed);
    }

    [Fact]
    public void Route_AccountAlertedBeforeTheCooldownExpired_SendsAgain()
    {
        var config = new DiscordConfig { DroppedOutDestination = AlertDestination.Local };
        var lastSent = new Dictionary<Guid, DateTimeOffset> { [AccountA] = Now - AlertRouter.Cooldown.Add(TimeSpan.FromSeconds(1)) };

        var routed = AlertRouter.Route(
            [Trigger(AlertKind.AccountDroppedOut, AccountA, "A")], config, lastSent, Now);

        Assert.Single(routed);
    }

    [Fact]
    public void Route_MineDestinationWithNoWebhookConfigured_FallsBackToLocal()
    {
        // The silliest failure mode: routed to "my channel", no webhook pasted, alert vanishes.
        // Falling back to the desktop notification means the user still finds out.
        var config = new DiscordConfig { DroppedOutDestination = AlertDestination.Mine, MineWebhookUrl = null };

        var routed = AlertRouter.Route(
            [Trigger(AlertKind.AccountDroppedOut, AccountA, "A")], config, NothingSentYet, Now);

        Assert.Equal(AlertDestination.Local, Assert.Single(routed).Destination);
    }

    [Fact]
    public void Route_ClanDestinationWithNoWebhookConfigured_FallsBackToLocal()
    {
        var config = new DiscordConfig { MemoryWarningDestination = AlertDestination.Clan, ClanWebhookUrl = null };

        var routed = AlertRouter.Route(
            [Trigger(AlertKind.MemoryWarning, AccountA, "A")], config, NothingSentYet, Now);

        Assert.Equal(AlertDestination.Local, Assert.Single(routed).Destination);
    }

    [Fact]
    public void Route_OneKindOffAndTheOtherOn_SuppressesOnlyTheOffOne()
    {
        // Per-trigger routing has to be genuinely independent: a muted-by-None kind must not
        // consume the cooldown slot or swallow the kind that IS configured.
        var config = new DiscordConfig
        {
            DroppedOutDestination = AlertDestination.None,
            MemoryWarningDestination = AlertDestination.Local,
        };

        var routed = AlertRouter.Route(
            [Trigger(AlertKind.AccountDroppedOut, AccountA, "Silent"),
             Trigger(AlertKind.MemoryWarning, AccountB, "Loud")],
            config, NothingSentYet, Now);

        Assert.Equal(AlertKind.MemoryWarning, Assert.Single(routed).Kind);
    }
}
