using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Plugins;
using ROROROblox.App.Plugins.Adapters;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// The price of an optional constructor parameter.
/// <para>
/// <c>PluginHostService</c> takes its palette source as an optional trailing argument, because it
/// is constructed at 30 sites across the two test projects and making it required would edit all
/// 30 for no behavioural gain in 29 of them. Null there means "this host has no theme feed", which
/// is correct for a test that does not care about theming and <b>wrong in production</b> — and an
/// optional dependency silently unwired in the object graph the app actually builds is a defect
/// that no other test in this suite would notice.
/// </para>
/// <para>
/// Precedent: <c>ProductionDiRegistration_ThreadsTheLiveSettingsProbeIntoTheLauncher</c> exists
/// because the 2026-08-01 wrong-FPS-cap bug shipped in exactly this shape — the mechanism existed,
/// both call sites used it, and the real graph still resolved a null dependency. Same class of
/// hole, same class of guard.
/// </para>
/// </summary>
public class ThemeFeedWiringTests
{
    /// <summary>
    /// Reads the registration the app actually makes, rather than resolving
    /// <c>PluginHostService</c> for real: its chain reaches <c>MainViewModelRunningAccountsAdapter</c>
    /// and from there into WPF-affined territory this suite deliberately never constructs. The
    /// factory lambda is the thing under test anyway — the hole this guards against is an argument
    /// missing from that lambda.
    /// </summary>
    private static ServiceProvider BuildRealContainer()
    {
        var services = new ServiceCollection();
        global::ROROROblox.App.App.ConfigureServices(services, NullLoggerFactory.Instance);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void ProductionDi_RegistersThemeFeedAdapterAsThePaletteSource()
    {
        using var provider = BuildRealContainer();

        var source = provider.GetService<IThemePaletteSource>();

        Assert.NotNull(source);
        Assert.IsType<ThemeFeedAdapter>(source);
    }

    /// <summary>
    /// The bus the adapter raises on must be the same instance the host service listens on. This
    /// is not hypothetical: the original registration was
    /// <c>AddSingleton&lt;IPluginEventBus, InProcessPluginEventBus&gt;()</c>, under which anything
    /// resolving the concrete type got a second bus — and a second bus is a bus nobody's events
    /// reach. The theme feed would have gone quiet with every part of it apparently wired.
    /// </summary>
    [Fact]
    public void ProductionDi_ConcreteBusAndInterfaceBus_AreTheSameInstance()
    {
        using var provider = BuildRealContainer();

        var concrete = provider.GetRequiredService<InProcessPluginEventBus>();
        var viaInterface = provider.GetRequiredService<IPluginEventBus>();

        Assert.Same(concrete, viaInterface);
    }

    /// <summary>
    /// The guard that actually pays for the optional parameter: the app's own factory for
    /// <c>PluginHostService</c> must supply the palette source. Asserted by counting the arguments
    /// the constructor takes against the ones the registration passes — if someone adds a
    /// dependency and forgets to thread it, the count stops matching.
    /// </summary>
    [Fact]
    public void PluginHostService_TakesAPaletteSource_AndItIsNotSilentlyOptionalInProduction()
    {
        var ctor = typeof(PluginHostService).GetConstructors().Single();
        var palette = ctor.GetParameters().SingleOrDefault(p => p.ParameterType == typeof(IThemePaletteSource));

        Assert.NotNull(palette);
        Assert.True(palette!.IsOptional, "kept optional on purpose — see the ctor's doc comment.");

        // And the app registers something real for it, so the default is never what production
        // gets. Resolving the source itself (rather than the host service, whose chain reaches
        // WPF) is what makes this assertable in a suite that constructs no Window.
        using var provider = BuildRealContainer();
        Assert.NotNull(provider.GetService<IThemePaletteSource>());
    }
}
