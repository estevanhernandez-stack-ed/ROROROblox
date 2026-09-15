using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Metrics;

public class MetricRoutingTests
{
    private static AlertTrigger Breach(Guid id, string metricId = "battle.points") =>
        // PrivateBytes stays null for this kind: the observed number rides MetricValue, because a
        // long? truncated every fraction. Routing ignores both; the fixture carries them so it does
        // not teach the next reader the convention this branch replaced.
        new(AlertKind.MetricBreach, id, "Masked", "Real", metricId, null, DateTimeOffset.UnixEpoch, 50);

    private static AlertTrigger Dropped(Guid id) =>
        new(AlertKind.AccountDroppedOut, id, "Masked", "Real", "Pet Simulator 99!", null, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Configured_RoutesToItsDestination()
    {
        var config = new DiscordConfig { MetricBreachDestinations = [AlertDestination.Phone] };

        var routed = AlertRouter.Route([Breach(Guid.NewGuid())], config,
            new Dictionary<AlertCooldownKey, DateTimeOffset>(), DateTimeOffset.UnixEpoch,
            phoneConfigured: true);

        Assert.Equal(AlertDestination.Phone, Assert.Single(routed).Destination);
    }

    [Fact]
    public void Unconfigured_FallsBackToDesktop()
    {
        var config = new DiscordConfig { MetricBreachDestinations = [AlertDestination.Phone] };

        var routed = AlertRouter.Route([Breach(Guid.NewGuid())], config,
            new Dictionary<AlertCooldownKey, DateTimeOffset>(), DateTimeOffset.UnixEpoch,
            phoneConfigured: false);

        Assert.Equal(AlertDestination.Local, Assert.Single(routed).Destination);
    }

    [Fact]
    public void OutOfTheBox_ABreachRoutesSomewhereRatherThanNowhere()
    {
        // The end-to-end claim rests on this one: a breach with NOTHING configured still lands
        // somewhere, which is what the desktop-toast default buys.
        //
        // Corrected 2026-09-11. This used to say nothing in production writes
        // MetricBreachDestinations — that the Settings page painted checkboxes for the four older
        // kinds only, so every test here configured by hand a list no user could ever set. The
        // metric-alerts section landed that same day: the row's four boxes write the list through
        // OnAlertRoutingChanged like every other kind's, and the switch above them writes the
        // opt-in to settings.json. What survives the correction is the reason this test exists —
        // with an empty default the assertion read Assert.Empty and the whole path died here, and
        // a user who never opens Settings still gets the toast.
        var routed = AlertRouter.Route([Breach(Guid.NewGuid())], new DiscordConfig(),
            new Dictionary<AlertCooldownKey, DateTimeOffset>(), DateTimeOffset.UnixEpoch);

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
            new Dictionary<AlertCooldownKey, DateTimeOffset>(), DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void InsideTheCooldown_IsSuppressed()
    {
        // The guarantee the whole design rests on: repeated breaches do not become repeated pages.
        var id = Guid.NewGuid();
        var config = new DiscordConfig { MetricBreachDestinations = [AlertDestination.Local] };
        var last = new Dictionary<AlertCooldownKey, DateTimeOffset>
        {
            [AlertCooldownKey.For(Breach(id))] = DateTimeOffset.UnixEpoch,
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
        var last = new Dictionary<AlertCooldownKey, DateTimeOffset>
        {
            [AlertCooldownKey.For(Dropped(id))] = DateTimeOffset.UnixEpoch,
        };

        Assert.Single(AlertRouter.Route([Breach(id)], config, last,
            DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void TheCooldownIsPerMetric_ADiamondsBreachRoutesWhilePointsIsCoolingDown()
    {
        // Controller ruling C1, 2026-09-15: a Points stall and a Diamonds alert in the same read are
        // two different things to know. Keyed per (account, kind), the first to send silenced the
        // other for five minutes.
        var id = Guid.NewGuid();
        var config = new DiscordConfig { MetricBreachDestinations = [AlertDestination.Local] };
        var last = new Dictionary<AlertCooldownKey, DateTimeOffset>
        {
            [AlertCooldownKey.For(Breach(id, "battle.points"))] = DateTimeOffset.UnixEpoch,
        };

        var routed = AlertRouter.Route([Breach(id, "ps99.diamonds")], config, last,
            DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(1));

        Assert.Equal("ps99.diamonds", Assert.Single(Assert.Single(routed).Triggers).GameName);
    }

    [Fact]
    public void TheCooldownIsPerMetric_ARepeatOfTheSameMetricIsStillSuppressed()
    {
        // The other half: splitting the key by metric must not weaken the guard. The same stat
        // breaching again for the same account inside five minutes is one flapping condition.
        var id = Guid.NewGuid();
        var config = new DiscordConfig { MetricBreachDestinations = [AlertDestination.Local] };
        var last = new Dictionary<AlertCooldownKey, DateTimeOffset>
        {
            [AlertCooldownKey.For(Breach(id, "battle.points"))] = DateTimeOffset.UnixEpoch,
        };

        Assert.Empty(AlertRouter.Route([Breach(id, "battle.points")], config, last,
            DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void AGroupedBatch_IsOneAlertPerDestination_AndAnAccountInCooldownDropsOut()
    {
        // MetricBreachBatcher raises one (metric, rule) group per call; this is what turns that call
        // into exactly one alert per destination, with an account cooling down for THIS metric
        // simply absent. An account cooling down for a DIFFERENT metric stays in (C1, 2026-09-15).
        var coolingForPoints = Guid.NewGuid();
        var coolingForDiamonds = Guid.NewGuid();
        var batch = new[] { Breach(Guid.NewGuid()), Breach(coolingForPoints), Breach(coolingForDiamonds) };
        var config = new DiscordConfig
        {
            MetricBreachDestinations = [AlertDestination.Local, AlertDestination.Mine, AlertDestination.Phone],
            // The router only asks whether a webhook is configured; no URL is needed to say yes.
            MineWebhookUrl = "configured",
        };
        var lastSent = new Dictionary<AlertCooldownKey, DateTimeOffset>
        {
            [AlertCooldownKey.For(Breach(coolingForPoints, "battle.points"))] = DateTimeOffset.UnixEpoch,
            [AlertCooldownKey.For(Breach(coolingForDiamonds, "ps99.diamonds"))] = DateTimeOffset.UnixEpoch,
        };

        var routed = AlertRouter.Route(batch, config, lastSent, DateTimeOffset.UnixEpoch.AddMinutes(1),
            phoneConfigured: true);

        Assert.Equal(
            new[] { AlertDestination.Local, AlertDestination.Mine, AlertDestination.Phone },
            routed.Select(r => r.Destination).ToArray());
        Assert.All(routed, r => Assert.Equal(2, r.Triggers.Count));
        Assert.All(routed, r => Assert.DoesNotContain(r.Triggers, t => t.AccountId == coolingForPoints));
        Assert.All(routed, r => Assert.Contains(r.Triggers, t => t.AccountId == coolingForDiamonds));
    }
}
