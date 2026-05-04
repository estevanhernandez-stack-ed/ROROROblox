using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ROROROblox.App.AppLifecycle;
using ROROROblox.App.CookieCapture;
using ROROROblox.App.Logging;
using ROROROblox.App.Startup;
using ROROROblox.App.Tray;
using ROROROblox.App.Updates;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.App;

public partial class App : Application
{
    private SingleInstanceGuard? _singleInstance;
    private ServiceProvider? _services;
    private ILoggerFactory? _loggerFactory;
    private ILogger<App>? _log;

    /// <summary>
    /// True after the user explicitly Quits via the tray menu. MainWindow's Closing handler
    /// (item 9) checks this to decide between "minimize to tray" and "actually exit."
    /// </summary>
    public static bool IsShuttingDown { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Configure logging FIRST — every other failure mode below benefits from a written record.
        _loggerFactory = AppLogging.Configure();
        _log = _loggerFactory.CreateLogger<App>();
        WireGlobalExceptionHandlers();

        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        _log.LogInformation("ROROROblox starting (v{Version}, OS {Os})", version, Environment.OSVersion);

        _singleInstance = new SingleInstanceGuard("ROROROblox-app-singleton");
        if (!_singleInstance.AcquireOrSignalExisting())
        {
            _log.LogInformation("Another instance is running; signaling and exiting.");
            Shutdown(0);
            return;
        }

        var services = new ServiceCollection();
        ConfigureServices(services, _loggerFactory);
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
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Update check threw; ignoring.");
        }

        try
        {
            var vm = _services.GetRequiredService<MainViewModel>();
            await vm.LoadCompatBannerAsync();
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Compat banner threw; ignoring.");
        }

        try
        {
            // Background validation — slight delay so the UI gets to render first.
            await Task.Delay(TimeSpan.FromSeconds(2));
            var vm = _services.GetRequiredService<MainViewModel>();
            await vm.ValidateSessionsAsync();
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Session validation threw; ignoring.");
        }
    }

    private static void ConfigureServices(IServiceCollection services, ILoggerFactory loggerFactory)
    {
        services.AddSingleton(loggerFactory);
        services.AddLogging();

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
        services.AddSingleton<IRobloxProcessTracker, RobloxProcessTracker>();
        services.AddSingleton<IPrivateServerStore>(_ => new PrivateServerStore());

        var dataDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ROROROblox");
        services.AddSingleton<IDiagnosticsCollector>(sp => new DiagnosticsCollector(
            sp.GetRequiredService<IAccountStore>(),
            sp.GetRequiredService<IRobloxProcessTracker>(),
            sp.GetRequiredService<IMutexHolder>(),
            AppLogging.LogDirectory,
            dataDir));

        // ViewModel + Window.
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }

    private void WireTrayEvents(ITrayService tray, IMutexHolder mutex, MainWindow mainWindow)
    {
        tray.RequestOpenMainWindow += (_, _) => SurfaceMainWindow(mainWindow);
        tray.RequestToggleMutex += (_, _) => ToggleMutex(mutex, tray);
        tray.RequestQuit += (_, _) => RequestShutdown();
        tray.RequestOpenDiagnostics += (_, _) => OpenDiagnosticsFromTray(mainWindow);
        tray.RequestOpenLogs += (_, _) => OpenLogsFolder();
    }

    private void OpenDiagnosticsFromTray(Window owner)
    {
        if (_services is null) return;
        try
        {
            var collector = _services.GetRequiredService<IDiagnosticsCollector>();
            var window = new Diagnostics.DiagnosticsWindow(collector);
            if (owner.IsLoaded)
            {
                window.Owner = owner;
            }
            SurfaceMainWindow(owner);
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Couldn't open Diagnostics window from tray");
        }
    }

    private void OpenLogsFolder()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = AppLogging.LogDirectory,
                UseShellExecute = true,
                Verb = "open",
            });
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Couldn't open log folder from tray");
        }
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
        _log?.LogInformation("ROROROblox exiting (code {Code}).", e.ApplicationExitCode);
        _singleInstance?.Dispose();
        _services?.Dispose();
        AppLogging.Shutdown();
        base.OnExit(e);
    }

    /// <summary>
    /// Three nets so a stray exception writes to the log instead of vanishing into a Windows
    /// Error Reporting dialog. None of these <em>recover</em> — they record. The app still
    /// shuts down on a true unhandled crash; we just have evidence afterward.
    /// </summary>
    private void WireGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            _log?.LogError(args.Exception, "Unhandled WPF dispatcher exception.");
            // Don't mark Handled — let the app crash visibly. Silent crash is worse than loud crash.
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                _log?.LogCritical(ex, "Unhandled AppDomain exception (terminating={IsTerminating}).", args.IsTerminating);
            }
            else
            {
                _log?.LogCritical("Unhandled non-Exception object thrown: {Object}", args.ExceptionObject);
            }
            // Best-effort flush before the runtime tears us down.
            try { AppLogging.Shutdown(); } catch { }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _log?.LogWarning(args.Exception, "Unobserved Task exception (already detached from its Task).");
            args.SetObserved(); // Don't escalate to AppDomain.UnhandledException for a fire-and-forget.
        };
    }
}
