using ROROROblox.Core.Metrics;

namespace ROROROblox.Tests.Metrics;

public class MetricHistoryTests
{
    private static readonly Guid Acct = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Metric = "battle.points";
    private static DateTimeOffset T(int minutes) => DateTimeOffset.UnixEpoch.AddMinutes(minutes);

    private static MetricHistory Seeded(params (int Minute, double Value)[] samples)
    {
        var h = new MetricHistory();
        foreach (var (m, v) in samples) h.Add(new MetricObservation(Acct, Metric, v, T(m)));
        return h;
    }

    [Fact]
    public void OneSample_HasNoRate()
    {
        // null, never 0. Zero means "earning nothing", which IS the alert condition -- returning
        // it from a cold start would page every user the moment the app opens.
        var h = Seeded((0, 1000));
        Assert.Null(h.RatePerMinute(Acct, Metric, TimeSpan.FromMinutes(10), T(1)));
    }

    [Fact]
    public void TwoSamples_RateIsTheDerivative()
    {
        var h = Seeded((0, 1000), (10, 2000));   // +1000 over 10 minutes
        Assert.Equal(100d, h.RatePerMinute(Acct, Metric, TimeSpan.FromMinutes(10), T(10))!.Value, 3);
    }

    [Fact]
    public void OnlySamplesInsideTheWindowCount()
    {
        // The 0-minute sample is outside a 10-minute window ending at minute 20.
        var h = Seeded((0, 0), (12, 5000), (20, 5500));
        Assert.Equal(62.5d, h.RatePerMinute(Acct, Metric, TimeSpan.FromMinutes(10), T(20))!.Value, 3);
    }

    [Fact]
    public void ADecreaseIsTreatedAsAResetAndSuppressesTheRate()
    {
        // A battle ended and the counter went back to zero. Subtracting would report a large
        // negative rate, which reads as catastrophic underperformance.
        var h = Seeded((0, 9000), (5, 9500), (10, 0));
        Assert.Null(h.RatePerMinute(Acct, Metric, TimeSpan.FromMinutes(10), T(10)));
    }

    [Fact]
    public void AfterAResetTheRateReturnsOnceTheWindowRefills()
    {
        var h = Seeded((0, 9000), (10, 0), (20, 500));
        Assert.Equal(50d, h.RatePerMinute(Acct, Metric, TimeSpan.FromMinutes(15), T(20))!.Value, 3);
    }

    [Fact]
    public void AGapDoesNotExtrapolate()
    {
        // 30 minutes with no poll, then two fresh samples. The rate is measured across what was
        // actually observed inside the window, not inferred across the hole.
        var h = Seeded((0, 1000), (30, 1000), (40, 2000));
        Assert.Equal(100d, h.RatePerMinute(Acct, Metric, TimeSpan.FromMinutes(10), T(40))!.Value, 3);
    }

    [Fact]
    public void KeysAreIsolatedByAccountAndMetric()
    {
        var other = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var h = new MetricHistory();
        h.Add(new MetricObservation(Acct, Metric, 0, T(0)));
        h.Add(new MetricObservation(Acct, Metric, 1000, T(10)));
        h.Add(new MetricObservation(other, Metric, 0, T(0)));

        Assert.Equal(100d, h.RatePerMinute(Acct, Metric, TimeSpan.FromMinutes(10), T(10))!.Value, 3);
        Assert.Null(h.RatePerMinute(other, Metric, TimeSpan.FromMinutes(10), T(10)));
        Assert.Null(h.RatePerMinute(Acct, "other.metric", TimeSpan.FromMinutes(10), T(10)));
    }

    [Fact]
    public void HistoryIsBoundedPerKey()
    {
        var h = new MetricHistory(capacity: 4);
        for (var i = 0; i < 20; i++) h.Add(new MetricObservation(Acct, Metric, i * 100, T(i)));
        Assert.Equal(4, h.Count(Acct, Metric));
    }

    [Fact]
    public void LatestIsTheMostRecentValue()
    {
        Assert.Equal(2000d, Seeded((0, 1000), (10, 2000)).Latest(Acct, Metric));
        Assert.Null(new MetricHistory().Latest(Acct, Metric));
    }

    [Fact]
    public void AtCapacityWithTrimmedInWindowSamples_HasNoRate()
    {
        // capacity 4, but 10 samples land inside a 10-minute window: the earliest were trimmed,
        // so any rate computed here would be measured over a span shorter than asked for and
        // would read as underperformance that did not happen.
        var h = new MetricHistory(capacity: 4);
        for (var i = 0; i <= 9; i++) h.Add(new MetricObservation(Acct, Metric, i * 100, T(i)));

        Assert.Null(h.RatePerMinute(Acct, Metric, TimeSpan.FromMinutes(10), T(9)));
    }

    [Fact]
    public void AtCapacityButWindowFullyCovered_StillReportsARate()
    {
        // The guard must not fire when the retained samples DO span the whole window -- otherwise
        // a correctly-sized capacity would stop reporting.
        var h = new MetricHistory(capacity: 4);
        for (var i = 0; i <= 9; i++) h.Add(new MetricObservation(Acct, Metric, i * 100, T(i)));

        Assert.Equal(100d, h.RatePerMinute(Acct, Metric, TimeSpan.FromMinutes(3), T(9))!.Value, 3);
    }

    [Fact]
    public void ExactlyAtCapacityWithNoEviction_StillReportsARate()
    {
        // Full is not the same as trimmed. A series holding exactly `capacity` samples has
        // dropped nothing, so its history is complete and the rate is real.
        var h = new MetricHistory(capacity: 4);
        for (var i = 0; i <= 3; i++) h.Add(new MetricObservation(Acct, Metric, i * 100, T(i)));

        Assert.Equal(100d, h.RatePerMinute(Acct, Metric, TimeSpan.FromMinutes(10), T(3))!.Value, 3);
    }

    [Fact]
    public void SeriesCount_IsBounded_AndTheBoundDropsRatherThanEvicts()
    {
        // A reporter whose metric id varies -- a server id, a session suffix -- would otherwise
        // grow this dictionary for the lifetime of a tray app that runs for days.
        var h = new MetricHistory(capacity: 8, maxSeries: 3);
        var acct = Guid.NewGuid();

        for (var i = 0; i < 3; i++)
        {
            h.Add(new MetricObservation(acct, $"m{i}", 1, DateTimeOffset.UnixEpoch));
        }
        h.Add(new MetricObservation(acct, "overflow", 1, DateTimeOffset.UnixEpoch));

        // The three that got in are still there -- the bound refuses the newcomer rather than
        // evicting a series a rule may be actively watching.
        Assert.Equal(1, h.Count(acct, "m0"));
        Assert.Equal(1, h.Count(acct, "m2"));
        Assert.Equal(0, h.Count(acct, "overflow"));
    }

    [Fact]
    public void AnExistingSeries_KeepsAcceptingAfterTheBoundIsReached()
    {
        // The bound is on the number of series, not on writes. Refusing further samples to a
        // series a rule is watching would turn a junk-id flood into a silent outage of the
        // metric the user actually configured.
        var h = new MetricHistory(capacity: 8, maxSeries: 2);
        var acct = Guid.NewGuid();

        h.Add(new MetricObservation(acct, "real", 1, DateTimeOffset.UnixEpoch));
        h.Add(new MetricObservation(acct, "other", 1, DateTimeOffset.UnixEpoch));
        h.Add(new MetricObservation(acct, "junk", 1, DateTimeOffset.UnixEpoch));
        h.Add(new MetricObservation(acct, "real", 2, DateTimeOffset.UnixEpoch.AddMinutes(1)));

        Assert.Equal(2, h.Count(acct, "real"));
    }

    [Fact]
    public void TheSeriesBound_IsPerAccountPlusMetric_NotPerMetric()
    {
        // The key is the PAIR. Two accounts reporting the same metric id are two series, which
        // is what makes a per-account bound meaningful at all.
        var h = new MetricHistory(capacity: 8, maxSeries: 2);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        h.Add(new MetricObservation(a, "same", 1, DateTimeOffset.UnixEpoch));
        h.Add(new MetricObservation(b, "same", 1, DateTimeOffset.UnixEpoch));
        h.Add(new MetricObservation(Guid.NewGuid(), "same", 1, DateTimeOffset.UnixEpoch));

        Assert.Equal(1, h.Count(a, "same"));
        Assert.Equal(1, h.Count(b, "same"));
    }
}
