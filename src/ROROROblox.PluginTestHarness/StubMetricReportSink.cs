using ROROROblox.App.Plugins;

namespace ROROROblox.PluginTestHarness;

/// <summary>Records what reached the host, so a test can assert the difference between "the call
/// was denied" and "the call arrived and did nothing".</summary>
internal sealed class StubMetricReportSink : IMetricReportSink
{
    public List<(string Subject, string Metric, double Value, long At)> Reports { get; } = [];

    public void Report(string subjectId, string metricId, double value, long observedAtUnixMs)
        => Reports.Add((subjectId, metricId, value, observedAtUnixMs));
}
