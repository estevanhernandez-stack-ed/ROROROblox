using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Metrics;

public class MetricRoutingTests
{
    private static AlertTrigger Breach(Guid id) =>
        new(AlertKind.MetricBreach, id, "Masked", "Real", "battle.points", 50, DateTimeOffset.UnixEpoch);

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
