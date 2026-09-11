using System.Reflection;
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
        //
        // So this RUNS the real factory rather than asserting it is non-null — the delegate has
        // existed since v1.4 and a non-null check would have passed before the sink was written.
        // The stub provider below answers the host's other eleven dependencies with do-nothing
        // doubles; the sink is the only one this test has an opinion about, and the only one it
        // reads back.
        var services = RealRegistrations();
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(PluginHostService));
        Assert.NotNull(descriptor.ImplementationFactory);

        var sink = new NoopSink();
        var host = (PluginHostService)descriptor.ImplementationFactory!(new StubProvider(sink));

        Assert.Same(sink, host.MetricSinkForTests);
    }

    private sealed class NoopSink : IMetricReportSink
    {
        public void Report(string subjectId, string metricId, double value, long observedAtUnixMs) { }
    }

    /// <summary>
    /// Hands back a do-nothing double for whatever the host's factory asks for, and the one real
    /// object this test cares about for <see cref="IMetricReportSink"/>.
    /// <para>
    /// <see cref="DispatchProxy"/> rather than eleven hand-written fakes: this test has no opinion
    /// about any other dependency — it asks one question, whether the sink arrives — and eleven
    /// empty classes asserting nothing would bury the line that does. Doubles are cached per type
    /// so a factory asking twice gets the same instance, which is what a container would do.
    /// </para>
    /// </summary>
    private sealed class StubProvider(IMetricReportSink sink) : IServiceProvider
    {
        private readonly Dictionary<Type, object> _made = [];

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IMetricReportSink)) return sink;
            if (_made.TryGetValue(serviceType, out var existing)) return existing;

            // PluginUITranslator is the factory's one concrete dependency, and it is sealed, so a
            // proxy cannot stand in for it — it gets built over a proxied host instead.
            var made = serviceType == typeof(PluginUITranslator)
                ? new PluginUITranslator((IPluginUIHost)Proxy(typeof(IPluginUIHost)))
                : Proxy(serviceType);

            _made[serviceType] = made;
            return made;
        }

        // The generic Create<T, TProxy>() reached by reflection, since the contract type is only
        // known at run time. Selected by shape rather than by GetMethod(name): .NET 10 ships more
        // than one Create overload and the simple lookup is ambiguous.
        private static readonly MethodInfo CreateProxy = typeof(DispatchProxy)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == nameof(DispatchProxy.Create)
                && m.IsGenericMethodDefinition
                && m.GetGenericArguments().Length == 2
                && m.GetParameters().Length == 0);

        private static object Proxy(Type contract) =>
            CreateProxy.MakeGenericMethod(contract, typeof(DoNothingProxy)).Invoke(null, null)!;
    }

    /// <summary>Every member returns default. Public because DispatchProxy generates a subclass of
    /// it into its own dynamic assembly.</summary>
    public class DoNothingProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }
}
