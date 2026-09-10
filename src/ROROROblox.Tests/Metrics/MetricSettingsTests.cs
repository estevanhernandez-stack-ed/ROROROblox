using ROROROblox.Core;

namespace ROROROblox.Tests.Metrics;

public class MetricSettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rororo-metrics-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task MetricAlerts_DefaultOff()
    {
        // A feature that starts alerting on upgrade is a bug. Opt-in, like every alert kind.
        Assert.False(await new AppSettings(_path).GetMetricAlertsEnabledAsync());
    }

    [Fact]
    public async Task MetricAlerts_RoundTrip()
    {
        var s = new AppSettings(_path);
        await s.SetMetricAlertsEnabledAsync(true);
        Assert.True(await new AppSettings(_path).GetMetricAlertsEnabledAsync());
    }

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch (Exception) { /* temp file */ }
    }
}
