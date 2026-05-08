using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Windows;
using System.Linq;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ROROROblox.App.AppLifecycle;
using ROROROblox.App.CookieCapture;
using ROROROblox.App.Logging;
using ROROROblox.App.Startup;
using ROROROblox.App.Theming;
using ROROROblox.App.Tray;
using ROROROblox.App.Updates;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
using ROROROblox.Core.Theming;

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

        // Force dark Win11 immersive title bar chrome on every plain Window created in this
        // process (Diagnostics, Settings, About, modals, etc.). MainWindow uses FluentWindow
        // and is unaffected. One-time class handler — covers windows opened later too.
        WindowTheming.RegisterGlobalDarkTitleBar();

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

        // Apply the saved theme synchronously so the first paint is already on the chosen
        // palette. The async path was deadlocking — IAppSettings.GetActiveThemeIdAsync
        // continues on the UI thread (ConfigureAwait(true)), and we were blocking the UI
        // thread with GetResult(). Sync file-read avoids the cycle entirely; the JSON parse
        // is small enough that startup latency stays imperceptible.
        try
        {
            _services.GetRequiredService<ThemeService>().ApplyAtStartup();
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Theme apply at startup threw; continuing with default brushes.");
        }

        // Cycle 4 hard-block: if Roblox is already running before RoRoRo started, multi-instance
        // is broken — every Launch As routes through the existing process which has a bound user
        // identity, ignoring our auth-ticket hand-off. Show the modal explaining the recovery path
        // (close both, restart RoRoRo) and exit cleanly. MainWindow never renders; tray never
        // shows; mutex never acquired against a hostile Roblox.
        // Placement note: must run AFTER ApplyAtStartup so BgBrush resolves before the modal
        // paints, but BEFORE mutex.Acquire so we never enter the broken state.
        var gate = _services.GetRequiredService<StartupGate>();
        if (!gate.ShouldProceed())
        {
            var modal = new Modals.RobloxAlreadyRunningWindow();
            modal.ShowDialog();
            Shutdown(0);
            return;
        }

        var tray = _services.GetRequiredService<ITrayService>();
        var mutex = _services.GetRequiredService<IMutexHolder>();
        var mainWindow = _services.GetRequiredService<MainWindow>();

        WireTrayEvents(tray, mutex, mainWindow);
        WireMutexLost(mutex, tray);
        WireMainAvatarTrayPainter();
        WireRobloxWindowDecorator();

        // Default Multi-Instance ON. Acquire the ROBLOX_singletonEvent mutex at startup so the
        // user can launch alts immediately without clicking the tray toggle first. The tray menu
        // still lets them toggle OFF for single-instance behavior.
        var acquired = mutex.Acquire();
        _log.LogInformation(
            "Mutex acquire at startup: name={Name}, acquired={Acquired}. Multi-instance is {State} (tray icon will reflect).",
            ROROROblox.Core.MutexHolder.DefaultMutexName,
            acquired,
            acquired ? "ON" : "ERROR");
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

        // Startup session validation REMOVED in v1.1.2.0.
        //
        // Previously called vm.ValidateSessionsAsync() here to proactively mark expired
        // sessions yellow on launch. In practice, hitting users.roblox.com/v1/users/authenticated
        // for every saved account at startup -- right after the cookie comes off disk and right
        // before any other authenticated endpoint has been touched -- pattern-matches Roblox's
        // anti-fraud heuristics. They flag the session for re-verification, our 401 handler maps
        // that to CookieExpiredException, the badge flips to "expired", and the user gets
        // surprise 2FA prompts on the next Launch As even though the cookie is minutes old.
        //
        // Pivoted to lazy validation: the actual GetAuthTicketAsync call inside Launch As
        // surfaces a real expiry (or a real reverification prompt) at the moment the user
        // chose to interact, where the friction is justified. Saves one fresh-context API
        // call per saved account on every startup -- exactly the surface Roblox flags as
        // suspicious. Track follow-up §4 in docs/store/next-revision-followups.md proposes a
        // proper NeedsReverification state distinct from SessionExpired for v1.2.0.0+.

        // FIRST: scan for already-running RobloxPlayerBeta windows + re-attach the ones whose
        // titles match saved accounts. After this, vm.Accounts[i].IsRunning correctly reflects
        // the world for any tagged session that survived our restart.
        ScanResult scan = new(0, 0);
        try
        {
            var scanner = _services.GetRequiredService<RunningRobloxScanner>();
            var tracker = _services.GetRequiredService<IRobloxProcessTracker>();
            var vm = _services.GetRequiredService<MainViewModel>();
            scan = scanner.Scan(vm.Accounts.ToList(), tracker);
            if (scan.MatchedAndAttached > 0)
            {
                _log?.LogInformation("Re-attached to {Count} existing Roblox window(s) by title.", scan.MatchedAndAttached);
            }
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Pre-existing Roblox scan threw; continuing without re-attach.");
        }

        try
        {
            // Auto-launch main on startup, but with strong guardrails so we never duplicate a
            // running session. Roblox enforces one-session-per-account server-side: if we
            // launch a second client for an already-signed-in account, the first gets kicked.
            // Skip when:
            //   — opt-in is off
            //   — no main is set
            //   — main's session expired
            //   — main is already running (scanner just attached, OR launching)
            //   — ANY Roblox window is running, even untagged (defensive — if the user manually
            //     opened Roblox before us, we don't know which account it is, and launching the
            //     main might kick them out if it happens to be them)
            var settings = _services.GetRequiredService<IAppSettings>();
            if (await settings.GetLaunchMainOnStartupAsync())
            {
                var vm = _services.GetRequiredService<MainViewModel>();
                var main = vm.MainAccount;
                if (main is null)
                {
                    _log?.LogDebug("LaunchMainOnStartup enabled but no main account picked.");
                }
                else if (main.IsRunning)
                {
                    _log?.LogInformation("Main {AccountId} ({Name}) already running — skipping auto-launch.", main.Id, main.DisplayName);
                    Dispatcher.Invoke(() =>
                        vm.StatusBanner = $"{main.DisplayName} is already running — skipped auto-launch.");
                }
                else if (scan.AnyRobloxRunning)
                {
                    _log?.LogInformation("Untagged Roblox window detected — skipping auto-launch to avoid kicking the user out.");
                    Dispatcher.Invoke(() =>
                        vm.StatusBanner = "Roblox is already open — skipped auto-launch so you don't get kicked out. Click Launch As when you're ready.");
                }
                else if (main.SessionExpired || main.IsLaunching || !vm.StartMainCommand.CanExecute(null))
                {
                    _log?.LogDebug("Main not eligible for auto-launch (expired={Expired}, launching={Launching}).", main.SessionExpired, main.IsLaunching);
                }
                else
                {
                    _log?.LogInformation("Auto-launching main account {AccountId} per LaunchMainOnStartup", main.Id);
                    Dispatcher.Invoke(() => vm.StartMainCommand.Execute(null));
                }
            }
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Auto-launch main threw; ignoring.");
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
        services.AddSingleton<IPrivateServerStore>(_ => new PrivateServerStore());
        services.AddSingleton<ISessionHistoryStore>(_ => new SessionHistoryStore());
        services.AddSingleton<IThemeStore>(_ => new ThemeStore());
        services.AddSingleton<ThemeService>();
        services.AddSingleton<IAccountStore>(_ => new AccountStore());
        services.AddSingleton<IStartupRegistration, StartupRegistration>();
        services.AddSingleton<IProcessStarter, ProcessStarter>();

        // RobloxApi over a managed HttpClient (factory handles lifetime + DNS rotation).
        // UseCookies=false is load-bearing: RobloxApi sets the .ROBLOSECURITY cookie manually
        // per-request via request.Headers.Add("Cookie", ...). With the default cookie container
        // enabled, the first auth-ticket request leaks its account's cookie into the container
        // and every subsequent Launch As routes through that same account — every alt opens as
        // the first user. This is the actual root cause of the multi-instance bug v1.2 saw.
        services.AddHttpClient<IRobloxApi, RobloxApi>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.Clear();
            var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RORORO", version));
        })
        .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
        {
            UseCookies = false,
        });

        // Compat checker uses its own HttpClient — different UA + different host pattern.
        services.AddHttpClient<IRobloxCompatChecker, RobloxCompatChecker>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.Clear();
            var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RORORO", version));
        });

        services.AddSingleton<IBloxstrapDetector, BloxstrapDetector>();
        services.AddSingleton<IClientAppSettingsWriter, ClientAppSettingsWriter>();
        services.AddSingleton<IGlobalBasicSettingsWriter, GlobalBasicSettingsWriter>();
        services.AddSingleton<IRobloxLauncher, RobloxLauncher>();
        services.AddSingleton<ICookieCapture, CookieCapture.CookieCapture>();
        services.AddSingleton<IUpdateChecker, UpdateChecker>();
        services.AddSingleton<IRobloxProcessTracker, RobloxProcessTracker>();
        services.AddSingleton<RobloxWindowDecorator>();
        services.AddSingleton<RunningRobloxScanner>();

        // Cycle 4 startup gate — detects RobloxPlayerBeta.exe running BEFORE RoRoRo so we
        // can hard-block instead of silently entering the broken state where every Launch As
        // routes through the existing process. Lives next to other Diagnostics-namespace
        // services in Core; consumed by App.OnStartup before mutex.Acquire.
        services.AddSingleton<IRobloxRunningProbe, RobloxRunningProbe>();
        services.AddSingleton<StartupGate>();

        // Tray avatar painter — owns its own HttpClient (default UA fine for public Roblox CDN
        // avatar URLs). TrayService is registered as ITrayService elsewhere; the painter takes
        // the concrete type because SetCustomStateIcons is App-internal.
        services.AddSingleton(sp =>
        {
            var tray = (Tray.TrayService)sp.GetRequiredService<ITrayService>();
            var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
            var http = httpFactory.CreateClient(nameof(Tray.MainAvatarTrayPainter));
            return new Tray.MainAvatarTrayPainter(
                tray,
                http,
                sp.GetRequiredService<ILogger<Tray.MainAvatarTrayPainter>>());
        });

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
        tray.RequestOpenPreferences += (_, _) => OpenPreferencesFromTray(mainWindow);
        tray.RequestActivateMain += (_, _) => ActivateMainFromTray(mainWindow);
        tray.RequestOpenHistory += (_, _) => OpenHistoryFromTray(mainWindow);
    }

    private void OpenHistoryFromTray(Window owner)
    {
        if (_services is null) return;
        try
        {
            var store = _services.GetRequiredService<ISessionHistoryStore>();
            var favorites = _services.GetRequiredService<IFavoriteGameStore>();
            var api = _services.GetRequiredService<IRobloxApi>();
            var window = new History.SessionHistoryWindow(store, favorites, api);
            if (owner.IsLoaded) window.Owner = owner;
            SurfaceMainWindow(owner);
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Couldn't open History window from tray");
        }
    }

    /// <summary>
    /// Tray double-click target. If a main is set + idle + not expired, launch it. Otherwise
    /// fall back to surfacing the main window so the user can pick one. Either way, this is
    /// the "do the most useful thing" path.
    /// </summary>
    private void ActivateMainFromTray(Window mainWindow)
    {
        if (_services is null)
        {
            SurfaceMainWindow(mainWindow);
            return;
        }
        try
        {
            var vm = _services.GetRequiredService<MainViewModel>();
            var main = vm.MainAccount;
            if (main is { SessionExpired: false, IsRunning: false, IsLaunching: false })
            {
                _log?.LogInformation("Tray double-click: launching main {AccountId}", main.Id);
                if (vm.StartMainCommand.CanExecute(null))
                {
                    vm.StartMainCommand.Execute(null);
                    return;
                }
            }
            // Fallback: open the window so the user can see/pick.
            SurfaceMainWindow(mainWindow);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "ActivateMainFromTray failed; falling back to window surface");
            SurfaceMainWindow(mainWindow);
        }
    }

    /// <summary>
    /// Subscribe to the ViewModel's MainAccount changes and ask the avatar painter to refresh
    /// the tray icons whenever the user picks a new main, unsets, or adds the first account.
    /// Painter runs off-thread; failures silently fall back to the bundled defaults.
    /// </summary>
    /// <summary>
    /// Hook the window decorator to <see cref="IRobloxProcessTracker"/> events: when a Roblox
    /// player process attaches to an account, push the title text + per-account caption color
    /// onto its main HWND (re-applied every 1.5s by the decorator's own timer to defeat
    /// Roblox's occasional self-rename). Untrack on exit so we don't leak entries.
    /// </summary>
    private void WireRobloxWindowDecorator()
    {
        if (_services is null) return;
        try
        {
            var tracker = _services.GetRequiredService<IRobloxProcessTracker>();
            var decorator = _services.GetRequiredService<RobloxWindowDecorator>();
            var vm = _services.GetRequiredService<MainViewModel>();

            tracker.ProcessAttached += (_, e) =>
            {
                var summary = vm.Accounts.FirstOrDefault(a => a.Id == e.AccountId);
                if (summary is null) return;
                decorator.Track(e.Pid, summary);
            };
            tracker.ProcessExited += (_, e) => decorator.Untrack(e.Pid);
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "WireRobloxWindowDecorator failed; window titles stay default.");
        }
    }

    private void WireMainAvatarTrayPainter()
    {
        if (_services is null) return;
        try
        {
            var vm = _services.GetRequiredService<MainViewModel>();
            var painter = _services.GetRequiredService<MainAvatarTrayPainter>();
            var dispatcher = Dispatcher;

            // Initial paint — runs after Load fills the accounts list (LoadAsync triggers
            // OnPropertyChanged for MainAccount, which we'll catch). But also kick once now
            // in case the VM was already populated synchronously.
            _ = painter.UpdateAsync(vm.MainAccount?.AvatarUrl, dispatcher);

            vm.PropertyChanged += async (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.MainAccount))
                {
                    await painter.UpdateAsync(vm.MainAccount?.AvatarUrl, dispatcher);
                }
            };
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "WireMainAvatarTrayPainter failed; tray stays default.");
        }
    }

    private void OpenPreferencesFromTray(Window owner)
    {
        if (_services is null) return;
        try
        {
            var settings = _services.GetRequiredService<IAppSettings>();
            var startup = _services.GetRequiredService<IStartupRegistration>();
            var themeStore = _services.GetRequiredService<IThemeStore>();
            var themeService = _services.GetRequiredService<ThemeService>();
            var window = new Preferences.PreferencesWindow(settings, startup, themeStore, themeService);
            if (owner.IsLoaded) window.Owner = owner;
            SurfaceMainWindow(owner);
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Couldn't open Preferences window from tray");
        }
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
