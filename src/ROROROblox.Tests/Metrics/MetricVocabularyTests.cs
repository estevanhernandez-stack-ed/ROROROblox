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
    public void DestinationsFor_MetricBreach_DefaultsToNothing()
    {
        // Off until the user turns it on, same as every other kind. A new alert kind that
        // starts firing on upgrade is a bug, not a feature.
        Assert.Empty(new DiscordConfig().DestinationsFor(AlertKind.MetricBreach));
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
