using ROROROblox.Core.Discord;
using ROROROblox.Core.Metrics;

namespace ROROROblox.Tests.Metrics;

public class MetricVocabularyTests
{
    [Fact]
    public void MetricBreach_IsAnAlertKind()
    {
        Assert.True(Enum.IsDefined(AlertKind.MetricBreach));
    }

    [Fact]
    public void DestinationsFor_MetricBreach_ReturnsItsOwnList()
    {
        var config = new DiscordConfig
        {
            MetricBreachDestinations = [AlertDestination.Phone],
            DroppedOutDestinations = [AlertDestination.Local],
        };

        Assert.Equal([AlertDestination.Phone], config.DestinationsFor(AlertKind.MetricBreach));
    }

    [Fact]
    public void DestinationsFor_MetricBreach_DefaultsToTheDesktopToast()
    {
        // This test said "DefaultsToNothing" until 2026-09-11, and nothing is exactly what the
        // feature did: no Settings control writes this list, so a breach resolved to no
        // destination and was logged as "routed nowhere" on every install. The default is the
        // reader the list never had. It cannot fire unasked — MetricAlertsEnabled is false, a
        // rules file must exist, and a plugin must hold a granted capability.
        Assert.Equal(AlertDestination.Local,
            Assert.Single(new DiscordConfig().DestinationsFor(AlertKind.MetricBreach)));
    }

    [Fact]
    public void Observation_CarriesAccountMetricValueAndInstant()
    {
        var id = Guid.NewGuid();
        var o = new MetricObservation(id, "battle.points", 48200, DateTimeOffset.UnixEpoch);

        Assert.Equal(id, o.AccountId);
        Assert.Equal("battle.points", o.MetricId);
        Assert.Equal(48200, o.Value);
        Assert.Equal(DateTimeOffset.UnixEpoch, o.ObservedAtUtc);
    }
}
