using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Metrics;

public class MetricRoutingTests
{
    private static AlertTrigger Breach(Guid id) =>
        // PrivateBytes stays null for this kind: the observed number rides MetricValue, because a
        // long? truncated every fraction. Routing ignores both; the fixture carries them so it does
        // not teach the next reader the convention this branch replaced.
        new(AlertKind.MetricBreach, id, "Masked", "Real", "battle.points", null, DateTimeOffset.UnixEpoch, 50);

    [Fact]
    public void Configured_RoutesToItsDestination()
    {
        var config = new DiscordConfig { MetricBreachDestinations = [AlertDestination.Phone] };

        var routed = AlertRouter.Route([Breach(Guid.NewGuid())], config,
            new Dictionary<(Guid, AlertKind), DateTimeOffset>(), DateTimeOffset.UnixEpoch,
            phoneConfigured: true);

        Assert.Equal(AlertDestination.Phone, Assert.Single(routed).Destination);
    }

    [Fact]
    public void Unconfigured_FallsBackToDesktop()
    {
        var config = new DiscordConfig { MetricBreachDestinations = [AlertDestination.Phone] };

        var routed = AlertRouter.Route([Breach(Guid.NewGuid())], config,
            new Dictionary<(Guid, AlertKind), DateTimeOffset>(), DateTimeOffset.UnixEpoch,
            phoneConfigured: false);

        Assert.Equal(AlertDestination.Local, Assert.Single(routed).Destination);
    }

    [Fact]
    public void OutOfTheBox_ABreachRoutesSomewhereRatherThanNowhere()
    {
        // The end-to-end claim rests on this one. Nothing in production writes
        // MetricBreachDestinations — the Settings page paints checkboxes for the four older kinds
        // only — so every test above was configuring by hand a list no user could ever set. With
        // an empty default this assertion read Assert.Empty and the whole path died here.
        var routed = AlertRouter.Route([Breach(Guid.NewGuid())], new DiscordConfig(),
            new Dictionary<(Guid, AlertKind), DateTimeOffset>(), DateTimeOffset.UnixEpoch);

        Assert.Equal(AlertDestination.Local, Assert.Single(routed).Destination);
    }

    [Fact]
    public void AMutedAccount_IsSilent()
    {
        var id = Guid.NewGuid();
        var config = new DiscordConfig
        {
            MetricBreachDestinations = [AlertDestination.Local],
            MutedAccountIds = [id],
        };

        Assert.Empty(AlertRouter.Route([Breach(id)], config,
            new Dictionary<(Guid, AlertKind), DateTimeOffset>(), DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void InsideTheCooldown_IsSuppressed()
    {
        // The guarantee the whole design rests on: repeated breaches do not become repeated pages.
        var id = Guid.NewGuid();
        var config = new DiscordConfig { MetricBreachDestinations = [AlertDestination.Local] };
        var last = new Dictionary<(Guid, AlertKind), DateTimeOffset>
        {
            [(id, AlertKind.MetricBreach)] = DateTimeOffset.UnixEpoch,
        };

        Assert.Empty(AlertRouter.Route([Breach(id)], config, last,
            DateTimeOffset.UnixEpoch + AlertRouter.Cooldown - TimeSpan.FromSeconds(1)));

        Assert.Single(AlertRouter.Route([Breach(id)], config, last,
            DateTimeOffset.UnixEpoch + AlertRouter.Cooldown + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void TheCooldownIsPerKind_ADropOutDoesNotSuppressABreach()
    {
        var id = Guid.NewGuid();
        var config = new DiscordConfig
        {
            MetricBreachDestinations = [AlertDestination.Local],
            DroppedOutDestinations = [AlertDestination.Local],
        };
        var last = new Dictionary<(Guid, AlertKind), DateTimeOffset>
        {
            [(id, AlertKind.AccountDroppedOut)] = DateTimeOffset.UnixEpoch,
        };

        Assert.Single(AlertRouter.Route([Breach(id)], config, last,
            DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(1)));
    }
}
