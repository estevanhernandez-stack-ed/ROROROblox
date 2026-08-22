using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Plugins;
using ROROROblox.App.Plugins.Adapters;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// The price of an optional constructor parameter, paid again for <c>ISavedAccountsProvider</c> —
/// same reasoning as <c>ThemeFeedWiringTests</c>, whose doc carries the full argument (30
/// construction sites; null is correct for 29 of them and a silent defect in the one that
/// matters). GetAccounts on a null provider fails FailedPrecondition rather than answering
/// "no accounts"; these tests are what make production never that host.
/// <para>
/// Unlike the theme test, the registration is asserted by DESCRIPTOR rather than by resolving:
/// this adapter captures <see cref="ViewModels.MainViewModel"/>, and building that from the real
/// container constructs real stores over real user paths — the WPF-and-filesystem territory this
/// suite deliberately never touches. The descriptor is the thing under test anyway: the hole this
/// guards against is the registration (or the factory argument) going missing.
/// </para>
/// </summary>
public class SavedAccountsWiringTests
{
    private static ServiceCollection RealRegistrations()
    {
        var services = new ServiceCollection();
        global::ROROROblox.App.App.ConfigureServices(services, NullLoggerFactory.Instance);
        return services;
    }

    [Fact]
    public void ProductionDi_RegistersTheSavedAccountsAdapter()
    {
        var services = RealRegistrations();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(ISavedAccountsProvider));
        Assert.Equal(typeof(MainViewModelSavedAccountsAdapter), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void PluginHostService_TakesASavedAccountsProvider_KeptOptionalOnPurpose()
    {
        var ctor = typeof(PluginHostService).GetConstructors().Single();
        var saved = ctor.GetParameters().SingleOrDefault(p => p.ParameterType == typeof(ISavedAccountsProvider));

        Assert.NotNull(saved);
        Assert.True(saved!.IsOptional, "kept optional on purpose — see the ctor's doc comment.");
    }
}
