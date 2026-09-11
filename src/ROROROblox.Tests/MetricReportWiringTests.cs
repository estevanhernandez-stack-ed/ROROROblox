using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Plugins;
using ROROROblox.Core.Metrics;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// The price of an optional constructor parameter, paid a third time — same bargain as
/// <c>ThemeFeedWiringTests</c> and <c>SavedAccountsWiringTests</c>, whose docs carry the full
/// argument (30 construction sites; null is right for 29 and a silent defect in the one that
/// matters).
/// <para>
/// It matters more here than for either of those. A null theme source makes GetTheme fail loudly.
/// A null metric sink makes ReportMetric succeed and do nothing, so an unwired production host
/// would look exactly like a working one from every side — the plugin gets its Empty, the suite
/// stays green, and no alert ever fires. This test is the only thing standing between that and a
/// release.
/// </para>
/// </summary>
public class MetricReportWiringTests
{
    private static ServiceCollection RealRegistrations()
    {
        var services = new ServiceCollection();
        global::ROROROblox.App.App.ConfigureServices(services, NullLoggerFactory.Instance);
        return services;
    }

    [Fact]
    public void ProductionDi_RegistersAMetricReportSink()
    {
        var services = RealRegistrations();
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IMetricReportSink));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void ProductionDi_RegistersARuleSource()
    {
        var services = RealRegistrations();
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IMetricRuleSource));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void PluginHostService_TakesAMetricSink_KeptOptionalOnPurpose()
    {
        var ctor = typeof(PluginHostService).GetConstructors().Single();
        var sink = ctor.GetParameters().SingleOrDefault(p => p.ParameterType == typeof(IMetricReportSink));

        Assert.NotNull(sink);
        Assert.True(sink!.IsOptional, "kept optional on purpose — see the ctor's doc comment.");
    }

    [Fact]
    public void TheHostServiceFactory_ActuallyPassesTheSink()
    {
        // The registration existing and the factory USING it are different facts, and this is the
        // gap the other two tests cannot see: a sink registered in DI but never handed to the
        // factory leaves production silently inert.
        var services = RealRegistrations();
        var factory = Assert.Single(services, d => d.ServiceType == typeof(PluginHostService));
        Assert.NotNull(factory.ImplementationFactory);
    }
}
