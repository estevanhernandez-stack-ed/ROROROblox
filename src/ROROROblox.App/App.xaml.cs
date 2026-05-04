using System.Net.Http.Headers;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ROROROblox.App.AppLifecycle;
using ROROROblox.App.CookieCapture;
using ROROROblox.App.Startup;
using ROROROblox.App.Tray;
using ROROROblox.App.Updates;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.App;

public partial class App : Application
{
    private SingleInstanceGuard? _singleInstance;
    private ServiceProvider? _services;

    /// <summary>
    /// True after the user explicitly Quits via the tray menu. MainWindow's Closing handler
    /// (item 9) checks this to decide between "minimize to tray" and "actually exit."
    /// </summary>
    public static bool IsShuttingDown { get; private set; }

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

        var tray = _services.GetRequiredService<ITrayService>();
        var mutex = _services.GetRequiredService<IMutexHolder>();
        var mainWindow = _services.GetRequiredService<MainWindow>();

        WireTrayEvents(tray, mutex, mainWindow);
        WireMutexLost(mutex, tray);

        // Default Multi-Instance ON. Acquire the ROBLOX_singletonEvent mutex at startup so the
        // user can launch alts immediately without clicking the tray toggle first. The tray menu
        // still lets them toggle OFF for single-instance behavior.
        var acquired = mutex.Acquire();
        tray.UpdateStatus(acquired ? MultiInstanceState.On : MultiInstanceState.Error);

        tray.Show();
        _singleInstance.StartListening(mainWindow);
        mainWindow.Show();

        // Fire-and-forget startup checks. Failures are silent; banner stays null on no-drift / no-network.
        _ = RunStartupChecksAsync();
    }

    private async Task RunStartupChecksAsync()
    {
        if (_services is null)
        {
            return;
        }

        try
        {
            var updateChecker = _services.GetRequiredService<IUpdateChecker>();
            await updateChecker.CheckForUpdatesAsync();
        }
        catch
        {
            // Best-effort — auto-update is comfort, not load-bearing.
        }

        try
        {
            var vm = _services.GetRequiredService<MainViewModel>();
            await vm.LoadCompatBannerAsync();
        }
        catch
        {
            // Best-effort — version-drift banner is comfort, not load-bearing.
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Singletons — long-lived state we want exactly one of.
        services.AddSingleton<IMutexHolder>(_ => new MutexHolder()); // Default name = Local\ROBLOX_singletonEvent
        services.AddSingleton<ITrayService, TrayService>();
        services.AddSingleton<IAppSettings>(_ => new AppSettings());
        services.AddSingleton<IFavoriteGameStore>(_ => new FavoriteGameStore());
        services.AddSingleton<IAccountStore>(_ => new AccountStore());
        services.AddSingleton<IStartupRegistration, StartupRegistration>();
        services.AddSingleton<IProcessStarter, ProcessStarter>();

        // RobloxApi over a managed HttpClient (factory handles lifetime + DNS rotation).
        services.AddHttpClient<IRobloxApi, RobloxApi>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.Clear();
            var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ROROROblox", version));
        });

        // Compat checker uses its own HttpClient — different UA + different host pattern.
        services.AddHttpClient<IRobloxCompatChecker, RobloxCompatChecker>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.Clear();
            var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ROROROblox", version));
        });

        services.AddSingleton<IRobloxLauncher, RobloxLauncher>();
        services.AddSingleton<ICookieCapture, CookieCapture.CookieCapture>();
        services.AddSingleton<IUpdateChecker, UpdateChecker>();

        // ViewModel + Window.
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }

    private void WireTrayEvents(ITrayService tray, IMutexHolder mutex, MainWindow mainWindow)
    {
        tray.RequestOpenMainWindow += (_, _) => SurfaceMainWindow(mainWindow);
        tray.RequestToggleMutex += (_, _) => ToggleMutex(mutex, tray);
        tray.RequestQuit += (_, _) => RequestShutdown();
    }

    private static void WireMutexLost(IMutexHolder mutex, ITrayService tray)
    {
        mutex.MutexLost += (_, _) =>
        {
            // MutexLost fires on a System.Timers.Timer thread; marshal to UI for tray update.
            Current.Dispatcher.Invoke(() => tray.UpdateStatus(MultiInstanceState.Error));
        };
    }

    private static void ToggleMutex(IMutexHolder mutex, ITrayService tray)
    {
        if (mutex.IsHeld)
        {
            mutex.Release();
            tray.UpdateStatus(MultiInstanceState.Off);
        }
        else
        {
            var acquired = mutex.Acquire();
            tray.UpdateStatus(acquired ? MultiInstanceState.On : MultiInstanceState.Error);
        }
    }

    private static void SurfaceMainWindow(Window mainWindow)
    {
        if (!mainWindow.IsVisible)
        {
            mainWindow.Show();
        }
        if (mainWindow.WindowState == WindowState.Minimized)
        {
            mainWindow.WindowState = WindowState.Normal;
        }
        mainWindow.Activate();
    }

    private void RequestShutdown()
    {
        IsShuttingDown = true;
        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsShuttingDown = true;
        _singleInstance?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }
}
