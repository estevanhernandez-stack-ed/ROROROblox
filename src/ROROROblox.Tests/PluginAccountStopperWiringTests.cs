using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Plugins;
using ROROROblox.App.Plugins.Adapters;
using ROROROblox.App.ViewModels;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// Task 9, fix round 1. <c>App.OnStartup</c> now calls <c>WirePluginAccountStopper</c> right after
/// <c>StartPluginHostListener</c> — before the startup gate's modals — because that is exactly when
/// the plugin pipe becomes reachable. That move is only safe because <see cref="MainViewModel"/> is
/// already a constructed, cached singleton by the time <c>StartPluginHostListener</c> returns: an
/// unavoidable side effect of resolving <c>PluginHostStartupService</c>, which the review measured
/// via constructor-injection chasing, not something <c>WirePluginAccountStopper</c> forces on its
/// own. <c>WirePluginAccountStopper</c> still resolves <c>MainViewModel</c> unconditionally, so if
/// this chain ever breaks, it starts forcing early construction ahead of the startup gate instead
/// of finding the work already done -- a real behaviour change (whatever <c>MainViewModel</c>'s
/// constructor does -- file reads, timers -- would now run before the user answers the
/// already-running / leftover-processes dialog).
///
/// <para>
/// These facts are asserted by constructor-parameter reflection, never by resolving
/// <c>PluginHostStartupService</c> or <see cref="MainViewModel"/> from the real container:
/// building either one from the production registrations hits real user-data stores and WPF-affined
/// territory this suite deliberately never touches (see <c>SavedAccountsWiringTests</c> and
/// <c>ThemeFeedWiringTests</c> for the same reasoning). The chain: <c>PluginHostStartupService</c>
/// takes a <c>PluginHostService</c> directly &#8594; <c>PluginHostService</c> takes an
/// <see cref="IRunningAccountsProvider"/> directly &#8594; the app's only registration for that
/// interface, <see cref="MainViewModelRunningAccountsAdapter"/>, takes a <see cref="MainViewModel"/>
/// directly. Every link is a required constructor parameter, never a <c>Func&lt;T&gt;</c> or other
/// lazy seam, so building one forces the next.
/// </para>
/// </summary>
public class PluginAccountStopperWiringTests
{
    private static ServiceCollection RealRegistrations()
    {
        var services = new ServiceCollection();
        global::ROROROblox.App.App.ConfigureServices(services, NullLoggerFactory.Instance);
        return services;
    }

    [Fact]
    public void ProductionDi_RegistersTheAccountStopperSingleton()
    {
        var services = RealRegistrations();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IPluginAccountStopper));
        Assert.Equal(typeof(ProcessTrackerAccountStopper), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void ProductionDi_RegistersMainViewModelAsASingleton()
    {
        var services = RealRegistrations();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(MainViewModel));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    /// <summary>
    /// The load-bearing link. If this adapter ever takes a lazy accessor instead — a
    /// <c>Func&lt;MainViewModel&gt;</c>, say, to defer construction for some unrelated reason —
    /// <see cref="MainViewModel"/> stops being an unavoidable side effect of resolving
    /// <c>PluginHostService</c>, and the fix this test file guards would start forcing
    /// <see cref="MainViewModel"/> into existence ahead of the startup gate instead of finding it
    /// already built.
    /// </summary>
    [Fact]
    public void RunningAccountsAdapter_TakesMainViewModel_DirectlyNotLazily()
    {
        var ctor = typeof(MainViewModelRunningAccountsAdapter).GetConstructors().Single();
        var param = Assert.Single(ctor.GetParameters(), p => p.ParameterType == typeof(MainViewModel));
        Assert.False(param.IsOptional);
    }

    [Fact]
    public void PluginHostService_TakesARunningAccountsProvider_DirectlyNotLazily()
    {
        var ctor = typeof(PluginHostService).GetConstructors().Single();
        var param = ctor.GetParameters().SingleOrDefault(p => p.ParameterType == typeof(IRunningAccountsProvider));

        Assert.NotNull(param);
        Assert.False(param!.IsOptional);
    }

    [Fact]
    public void PluginHostStartupService_TakesAPluginHostService_DirectlyNotLazily()
    {
        var ctor = typeof(PluginHostStartupService).GetConstructors().Single();
        var param = ctor.GetParameters().SingleOrDefault(p => p.ParameterType == typeof(PluginHostService));

        Assert.NotNull(param);
        Assert.False(param!.IsOptional);
    }

    /// <summary>
    /// <c>PluginHostStartupService</c> itself must be a plain constructor-injected registration
    /// (ActivatorUtilities resolves every parameter before the constructor runs) rather than a
    /// custom factory that might defer <c>PluginHostService</c>'s resolution — the last link the
    /// three facts above depend on to actually fire when <c>StartPluginHostListener</c> resolves it.
    /// </summary>
    [Fact]
    public void PluginHostStartupService_IsABareConstructorRegistration_NotACustomFactory()
    {
        var services = RealRegistrations();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(PluginHostStartupService));
        Assert.Equal(typeof(PluginHostStartupService), descriptor.ImplementationType);
        Assert.Null(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }
}
