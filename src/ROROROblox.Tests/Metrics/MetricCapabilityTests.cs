using ROROROblox.App.Plugins;

namespace ROROROblox.Tests.Metrics;

public class MetricCapabilityTests
{
    [Fact]
    public void TheCapability_IsKnownAndHostEnforced()
    {
        // host.* means the interceptor enforces it on every call. A system.* capability is
        // disclosed for consent but unenforceable, because the plugin is its own process.
        // Reporting a metric is a write into the alert system, so it must be the enforced kind.
        Assert.True(PluginCapability.IsKnown(PluginCapability.HostMetricsReport));
        Assert.True(PluginCapability.IsHostEnforced(PluginCapability.HostMetricsReport));
    }

    [Fact]
    public void TheRpc_IsInTheCapabilityMap_AndIsGated()
    {
        Assert.True(RpcMethodCapabilityMap.TryGetRequired("ReportMetric", out var required));
        Assert.Equal(PluginCapability.HostMetricsReport, required);
    }

    [Fact]
    public void TheCapability_HasATranslatedDisplayString()
    {
        // Display() resolves a resx KEY at call time. A missing key falls through to the
        // "unknown capability" format, which would put a raw dotted string in the consent
        // sheet -- the one screen where a user decides whether to trust a plugin.
        var shown = PluginCapability.Display(PluginCapability.HostMetricsReport);
        Assert.DoesNotContain("host.metrics.report", shown, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryHostMethodStillHasAnEntry()
    {
        // Duplicates RpcMethodCapabilityMapTests on purpose: this is the test that fails the
        // moment the proto gains a method and the map does not, and a reader of THIS file
        // should see that guard rather than have to know the other file exists.
        RpcMethodCapabilityMap.AssertExhaustive();
    }
}
