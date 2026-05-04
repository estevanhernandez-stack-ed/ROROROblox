using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ROROROblox.App.AppLifecycle;
using ROROROblox.App.Startup;

namespace ROROROblox.App;

public partial class App : Application
{
    private SingleInstanceGuard? _singleInstance;
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new SingleInstanceGuard("ROROROblox-app-singleton");
        if (!_singleInstance.AcquireOrSignalExisting())
        {
            Shutdown(0);
            return;
        }

        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        var mainWindow = _services.GetRequiredService<MainWindow>();
        _singleInstance.StartListening(mainWindow);
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IStartupRegistration, StartupRegistration>();
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }
}
