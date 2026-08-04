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
using ROROROblox.App.Discord;
using ROROROblox.App.Discord.Internal;
using ROROROblox.App.Logging;
using ROROROblox.App.Startup;
using ROROROblox.App.Theming;
using ROROROblox.App.Tray;
using ROROROblox.App.Updates;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
using ROROROblox.Core.Discord;
using ROROROblox.Core.Theming;

namespace ROROROblox.App;

public partial class App : Application
{
    private SingleInstanceGuard? _singleInstance;
    private ServiceProvider? _services;
    private ILoggerFactory? _loggerFactory;
    private ILogger<App>? _log;

    /// <summary>
    /// Owns the guarded parse-and-raise for both inbound-join paths (cold start + relay). See
    /// <see cref="InboundJoinRelay"/>'s remarks for why the guard lives there and not inline here.
    /// </summary>
    private InboundJoinRelay? _joinRelay;

    /// <summary>
    /// The plugin-host listener's bind task, set by <see cref="StartPluginHostListener"/> and
    /// awaited by <see cref="StartPluginAutostart"/>. Null when the host failed to start, in
    /// which case autostart is skipped: a plugin process that can't reach the pipe would fail
    /// its first handshake anyway.
    /// </summary>
    private Task? _pluginHostListening;

    /// <summary>
    /// True after the user explicitly Quits via the tray menu. MainWindow's Closing handler
    /// (item 9) checks this to decide between "minimize to tray" and "actually exit."
    /// </summary>
    public static bool IsShuttingDown { get; private set; }

    /// <summary>
    /// Raised when a Discord Join lands via the <c>roblox-rororo:</c> URI scheme — either this
    /// process was cold-started with the URI as an argument (Discord launched us fresh), or a
    /// second instance relayed one through <see cref="SingleInstanceGuard.JoinUriReceived"/>
    /// (we were already running). Distinct from <see cref="Discord.DiscordPresenceService.JoinRequested"/>,
    /// which covers Discord's in-client IPC Join button while connected; this event is the OS
    /// protocol-handler path Discord falls back to. Always raised on the UI thread. Task 8 owns
    /// what happens next (server-instance targeting + the private-server warning) — this task's
    /// job ends at handing over a decoded <see cref="LaunchTarget"/>.
    /// <para>
    /// Forwarded from <see cref="InboundJoinRelay.JoinRequested"/> — every raise of this event,
    /// including whatever a subscriber does with it, runs inside that relay's try/catch. A
    /// subscriber throwing here cannot escape and cannot wedge the single-instance pipe listener.
    /// </para>
    /// <para>
    /// FIX 9 (final whole-branch review, 2026-08-03): <c>internal</c>, not <c>public</c> — the one
    /// raise site is the forwarding subscription in <see cref="OnStartup"/>
    /// (<c>_joinRelay.JoinRequested += (_, target) =&gt; JoinRequested?.Invoke(this, target);</c>).
    /// Keeping it internal makes "every raise funnels through <see cref="InboundJoinRelay"/>'s
    /// exception boundary" a compiler-checked property rather than a convention a future direct
    /// raise from outside this assembly could quietly bypass.
    /// </para>
    /// </summary>
    internal event EventHandler<LaunchTarget>? JoinRequested;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Configure logging FIRST — every other failure mode below benefits from a written record.
        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        _loggerFactory = AppLogging.Configure(version);
        _log = _loggerFactory.CreateLogger<App>();
        WireGlobalExceptionHandlers();

        // Force dark Win11 immersive title bar chrome on every plain Window created in this
        // process (Diagnostics, Settings, About, modals, etc.). MainWindow uses FluentWindow
        // and is unaffected. One-time class handler — covers windows opened later too.
        WindowTheming.RegisterGlobalDarkTitleBar();

        _log.LogInformation("ROROROblox starting (v{Version}, OS {Os})", version, Environment.OSVersion);

        // Guarded parse-and-raise for inbound joins (see InboundJoinRelay's remarks). Wire the
        // forwarding subscription now, before either call site below can fire: it runs INSIDE
        // the relay's try/catch, so a throwing App.JoinRequested subscriber is caught here too,
        // not just a bad parse.
        _joinRelay = new InboundJoinRelay(_log);
        _joinRelay.JoinRequested += (_, target) => JoinRequested?.Invoke(this, target);

        // Discord Join, inbound half: if this process was launched with a roblox-rororo://join/
        // URI (Discord handed it to us via the registered protocol handler), pull the raw arg out
        // now so it can be relayed to an already-running instance below, or turned into a
        // LaunchTarget once we know we're the primary instance.
        var joinArg = e.Args.FirstOrDefault(a =>
            a is not null && a.StartsWith($"{JoinUriScheme.SchemeName}://join/", StringComparison.OrdinalIgnoreCase));

        _singleInstance = new SingleInstanceGuard("ROROROblox-app-singleton", _log);
        if (!_singleInstance.AcquireOrSignalExisting(joinArg))
        {
            _log.LogInformation("Another instance is running; signaling and exiting.");
            Shutdown(0);
            return;
        }

        // FIX 6 (final whole-branch review, 2026-08-03): read the application id BEFORE deciding
        // whether to register the URI scheme, not after — a dark build (no Discord:ApplicationId
        // configured, the default) has no way to ever accept a join, so registering a live
        // roblox-rororo: entry point in HKCU for it is a needless local attack surface for a
        // feature that cannot run. WireDiscordPresenceAsync reuses this same value below rather
        // than re-reading the file a second time.
        var discordApplicationId = ReadDiscordApplicationId();

        // Register the roblox-rororo: URI scheme (HKCU, no elevation) before anything Discord-
        // related can run. Two things that look optional and are not: the registry command value
        // must end in "%1" or Windows launches us with no argument at all and every inbound join
        // silently does nothing (a May-branch regression); and Discord refuses to accept a
        // presence carrying join secrets unless the scheme is registered first, so this has to
        // land before Task 9's SetPresence — which is why it runs this early, before any other
        // startup work. A registration failure must not block a normal Roblox-launching session.
        if (string.IsNullOrWhiteSpace(discordApplicationId))
        {
            _log.LogDebug("Discord:ApplicationId is empty; skipping join URI scheme registration — the feature can't run in this build.");
        }
        else
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (exePath is not null)
                {
                    JoinUriScheme.Register(exePath);
                }
                else
                {
                    _log.LogDebug("Environment.ProcessPath was null; skipped Discord join URI scheme registration.");
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Discord join URI scheme registration failed; inbound Join clicks won't reach RoRoRo until this succeeds.");
            }
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

        // Acquire-first: resolve the singleton mutex NAME first (unchanged chain), THEN acquire,
        // THEN gate on the result. The gate's verdict is guaranteed true (mutex held) at the
        // moment we proceed — no check-then-lose-the-lock race.
        // Placement note: must run AFTER ApplyAtStartup so BgBrush resolves before either modal
        // below paints.
        //
        // Resolve the singleton mutex NAME from remote config (data-only) BEFORE IMutexHolder is
        // first materialized below — write it into the holder the factory reads. 2s-bounded,
        // degrade-safe (valid remote -> last-known-good -> hardcoded default), never throws.
        // No ConfigureAwait(false): this continuation must stay on the UI thread for the
        // Dispatcher-affined modals/tray/window shows below. async void + await yields the UI
        // thread (never blocks), so the ApplyAtStartup GetResult() deadlock noted above cannot recur.
        var nameSource = MutexNameSource.Default;
        try
        {
            var compat = _services.GetRequiredService<IRobloxCompatChecker>();
            var resolved = await compat.ResolveMutexNameAsync();
            _services.GetRequiredService<ResolvedMutexName>().Value = resolved.Name;
            nameSource = resolved.Source;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Mutex-name resolve threw; binding the hardcoded default.");
        }

        var mutex = _services.GetRequiredService<IMutexHolder>();
        var gate = _services.GetRequiredService<StartupGate>();

        // TryAcquire, not Acquire: the gate needs to know WHY we lost the name. Roblox holding it
        // (as an Event) means multi-instance is genuinely off; a compatible tool holding it (as a
        // Mutex) means Roblox already lost its singleton and everything works. Same bool, opposite
        // user experience. See MutexAcquireOutcome.
        var acquireOutcome = mutex.TryAcquire();
        var acquired = acquireOutcome == MutexAcquireOutcome.Acquired;
        var verdict = gate.Evaluate(acquireOutcome);

        // Bind the plugin-host pipe BEFORE the gate's modals. Both modals below block OnStartup
        // in a nested message loop, and the leftover modal fires whenever stale Roblox clients
        // exist — the exact condition an agent is trying to recover from. Binding here means the
        // pipe is reachable while the dialog waits for a human. Multi-instance state is already
        // resolved (mutex.Acquire above), so a plugin reading GetHostInfo sees the truth.
        // Autostart is deliberately deferred until after the gate — see StartPluginAutostart.
        StartPluginHostListener();

        var startedWithoutMutex = false;
        if (verdict is StartupGateResult.SharedLock)
        {
            // Another RoRoRo or a compatible tool squats the name as a Mutex. Roblox therefore lost
            // its own singleton and multi-instance works — there is nothing to recover from, so no
            // modal. Proceed exactly as "Start anyway" would; the contested watcher banners the fact
            // that we don't hold the handle.
            startedWithoutMutex = true;
            _log.LogInformation("Singleton name held by a compatible tool; starting without the handle (multi-instance still works).");
        }
        else if (verdict is StartupGateResult.Blocked)
        {
            // Roblox holds the name (as its Event). If the ONLY thing holding it is a windowless
            // tray client — the Windows-startup --launch-to-tray process — take it over silently:
            // it has no game window, so nothing is lost. Only a windowed (in-game) client, which
            // may have unsaved progress, falls through to the confirming modal.
            if (await TrySeamlessTakeoverAsync())
            {
                acquired = true; // we now own the Event; a tray client was put back alongside us
            }
            else
            {
                // In-game client present, or takeover couldn't reclaim — offer in-place recovery
                // (Retry / Close Roblox, which re-attempt the name) plus Start anyway and Quit.
                var modal = new Modals.RobloxAlreadyRunningWindow(
                    onCloseForMe: () => TryRecoverMultiInstanceAsync(closeRobloxFirst: true),
                    onRetry: () => TryRecoverMultiInstanceAsync(closeRobloxFirst: false));
                modal.ShowDialog();
                var (proceed, holdsMutex) = AppLifecycle.BlockedStartupDecision.Resolve(modal.Outcome);
                if (!proceed)
                {
                    Shutdown(0);
                    return;
                }
                acquired = holdsMutex;              // Recovered holds it; Start anyway does not
                startedWithoutMutex = !holdsMutex;  // borrowed start — the contested watcher will banner it
            }
        }
        else if (verdict is StartupGateResult.Leftover leftover)
        {
            var info = new Modals.LeftoverProcessesWindow(leftover.Windowless, leftover.Windowed);
            info.ShowDialog();
            switch (info.Action)
            {
                case Modals.LeftoverCleanupAction.StopAll:
                    CleanUpLeftoverRoblox(leftover.Windowed > 0);
                    break;
                case Modals.LeftoverCleanupAction.ClearStrays:
                    ClearLeftoverStrays();
                    break;
            }
            // mutex already held — proceed regardless
        }

        var tray = _services.GetRequiredService<ITrayService>();
        var mainWindow = _services.GetRequiredService<MainWindow>();

        WireTrayEvents(tray, mutex, mainWindow);
        WireMainViewModelEvents(mainWindow);
        WireMutexLost(mutex, tray);
        WireMainAvatarTrayPainter();
        WireRobloxWindowDecorator();
        WirePluginEventBus();
        WireActivityMonitor();
        await WireMemoryWatchdogAsync();
        WireMemoryWarningTray(); // Task 8 — closes the loop WireMemoryWatchdogAsync doesn't own
        WireContestedWatcher(mainWindow); // Task 8
        // The gate has been answered by now (both modal branches above are blocking), so it is
        // safe to let plugin processes launch. The pipe they handshake against bound earlier.
        StartPluginAutostart();
        await InitializeIdleSettingsAsync();
        await InitializeStreamerModeAsync();

        // Discord presence + Join (Task 9). MUST run before mainWindow.Show() below: the
        // cold-start join dispatch (_joinRelay.Handle(joinArg, "cold start")) fires right after
        // Show(), and a join that launched this very process needs JoinRequested already wired
        // by then, or it's silently dropped. mainWindow and the DI graph are both available by
        // this point (mainWindow resolved above at var mainWindow = ...), so there's no ordering
        // reason to push this any later.
        // Preferences must never depend on an optional feature wiring up. This assignment lived
        // inside WireAlertsAsync for one build, and when a DI resolve failed in there (a
        // non-generic ILogger parameter), the factory was never set and the whole Settings window
        // stopped opening — an alerts problem taking out a core window. Alerts are a passenger;
        // this is not. Set it first, unconditionally, before anything that can fail.
        _services.GetRequiredService<MainViewModel>().PreferencesWindowFactory = BuildPreferencesWindow;

        await WireDiscordPresenceAsync(mainWindow, discordApplicationId);

        // Alerts, wired separately and unconditionally — see the AlertDispatcher registration for
        // why they must not ride on the Discord app id. Presence needs a Discord pipe; a desktop
        // notification needs nothing at all.
        await WireAlertsAsync();

        _log.LogInformation(
            "Startup mutex: name={Name}, source={Source}, acquired={Acquired}.",
            mutex.MutexName, nameSource, acquired);
        // Borrowed start (Start anyway): we don't hold the lock, but it's not an error — Off is the
        // honest tray state; the contested watcher's banner carries the nuance.
        tray.UpdateStatus(
            startedWithoutMutex ? MultiInstanceState.Off
            : acquired ? MultiInstanceState.On
            : MultiInstanceState.Error);

        // Discord Join relay: a second instance launched with a join URI forwards it here over
        // the existing single-instance pipe (see SingleInstanceGuard.JoinUriReceived). The guard
        // already marshals this onto the UI thread before invoking; _joinRelay.Handle owns the
        // exception boundary (see InboundJoinRelay's remarks) — a throw here must not kill the
        // pipe-listener thread this callback runs on.
        _singleInstance.JoinUriReceived += relayedUri => _joinRelay!.Handle(relayedUri, "relay");

        tray.Show();
        _singleInstance.StartListening(mainWindow);
        mainWindow.Show();

        // Cold-start Discord Join: this process itself was launched with the join URI (Discord
        // handed it straight to us rather than to an already-running instance). Raised after
        // mainWindow.Show() so anything Task 8 wires up earlier in OnStartup is already
        // subscribed by the time this fires.
        if (joinArg is not null)
        {
            _joinRelay.Handle(joinArg, "cold start");
        }

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
            // Crash-orphan cleanup: a capture interrupted by a crash leaves a fully logged-in
            // Roblox profile under webview2-data\. The post-capture sweep (CookieCapture)
            // handles the normal path; this catches whatever a dying msedgewebview2 pinned.
            // Safe here because no capture can be in flight this early in startup.
            var webViewData = _services.GetRequiredService<WebView2UserDataDirectory>();
            await Task.Run(() => webViewData.SweepStale(exclude: null));
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "WebView2 user-data startup sweep threw; ignoring.");
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
            // Cycle 5: eager one-time backfill of RobloxUserId for any saved account where
            // it's still null. Runs ~5s after MainWindow.Show to give the UI a chance to
            // paint and Multi-Instance to settle before any background HTTP traffic. Sequential
            // with 2.5s ± 500ms stagger between accounts (anti-fraud discipline per spec §5).
            // Idempotent: returning users with all accounts backfilled make zero API calls.
            var backfill = _services.GetRequiredService<AccountUserIdBackfillService>();
            await Task.Delay(5_000).ConfigureAwait(false);
            await backfill.RunAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "RobloxUserId backfill threw; ignoring.");
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
                        vm.StatusBanner = $"{main.RenderName} is already running — skipped auto-launch.");
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

    // internal (not private), fix round 1 finding 3: RobloxLauncherTests.cs's
    // ProductionDiRegistration_ThreadsTheLiveRunningProbeIntoTheLauncher calls this directly —
    // via the pre-existing InternalsVisibleTo("ROROROblox.Tests") in ROROROblox.App.csproj — so it
    // exercises the REAL composition root instead of a hand-mirrored copy of one registration line.
    // Every registration in this method is lazy (parameterless AddSingleton<T,U>, or a factory
    // lambda evaluated at resolve time, never at registration time) except the two path-string
    // computations below (DefaultPluginsRoot, consentPath, dataDir) — none of which touch disk;
    // verified by inspection before making this change.
    internal static void ConfigureServices(IServiceCollection services, ILoggerFactory loggerFactory)
    {
        services.AddSingleton(loggerFactory);
        services.AddLogging();

        // Singletons — long-lived state we want exactly one of.
        // The singleton mutex NAME is resolved from roblox-compat.json at startup (before this
        // singleton is first materialized) and written into ResolvedMutexName; the factory reads it.
        // Falls back to MutexHolder.DefaultMutexName when resolution hasn't run or yielded the default.
        services.AddSingleton<ResolvedMutexName>();
        services.AddSingleton<IMutexHolder>(sp => new MutexHolder(sp.GetRequiredService<ResolvedMutexName>().Value));
        services.AddSingleton<ITrayService, TrayService>();
        services.AddSingleton<IAppSettings>(_ => new AppSettings());
        services.AddSingleton<IFavoriteGameStore>(_ => new FavoriteGameStore());
        services.AddSingleton<IPrivateServerStore>(_ => new PrivateServerStore());
        services.AddSingleton<ISessionHistoryStore>(_ => new SessionHistoryStore());
        services.AddSingleton<IThemeStore>(_ => new ThemeStore());
        services.AddSingleton<ThemeService>();
        services.AddSingleton<IAccountStore>(_ => new AccountStore());

        // Streamer mode (v1.9) — fake-name/avatar substitution for on-stream safety. Pools are
        // stateless generators; the friend-identity store persists lazily-assigned friend
        // identities to disk (accounts persist through IAccountStore instead, via persistAccount
        // below). persistAccount is wrapped so a disk failure from the provider's fire-and-forget
        // lazy-assignment persist (see StreamerIdentityProvider.Resolve) logs instead of becoming
        // an unobserved Task exception.
        services.AddSingleton<ROROROblox.Core.StreamerMode.IStreamerNamePool>(_ => new ROROROblox.Core.StreamerMode.StreamerNamePool());
        services.AddSingleton<ROROROblox.Core.StreamerMode.IStreamerAvatarPool>(_ => new ROROROblox.Core.StreamerMode.StreamerAvatarPool());
        services.AddSingleton<ROROROblox.Core.StreamerMode.IStreamerIdentityStore>(_ => new ROROROblox.Core.StreamerMode.FileStreamerIdentityStore());
        services.AddSingleton<ROROROblox.Core.StreamerMode.IStreamerIdentityProvider>(sp =>
        {
            var store = sp.GetRequiredService<IAccountStore>();
            var logger = sp.GetRequiredService<ILogger<App>>();
            return new ROROROblox.Core.StreamerMode.StreamerIdentityProvider(
                sp.GetRequiredService<ROROROblox.Core.StreamerMode.IStreamerNamePool>(),
                sp.GetRequiredService<ROROROblox.Core.StreamerMode.IStreamerAvatarPool>(),
                sp.GetRequiredService<ROROROblox.Core.StreamerMode.IStreamerIdentityStore>(),
                sp.GetRequiredService<IAppSettings>(),
                persistAccount: async (id, identity) =>
                {
                    try
                    {
                        await store.UpdateStreamerIdentityAsync(id, identity.FakeName, identity.FakeAvatarId);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to persist streamer identity for {AccountId}", id);
                    }
                });
        });

        // v1.6.0 account transport (item 5). Pure-crypto bundle service (PBKDF2 + AES-256-GCM);
        // stateless, so a singleton is fine. The export/import dialogs consume this + IAccountStore.
        services.AddSingleton<ROROROblox.Core.Transport.IAccountTransport,
            ROROROblox.Core.Transport.AccountTransportService>();
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

        // v1.7.0 install-deferral probe (item 1). Its own typed HttpClient — the CDN GET against
        // clientsettingscdn.roblox.com carries the ROROROblox UA (never a browser spoof). The probe
        // resolves its other two seams itself (live installer process scan + GetInstalledRobloxVersion).
        // Same IHttpClientFactory pattern as RobloxApi; both members degrade-safe to "no update".
        services.AddHttpClient<IRobloxUpdateProbe, RobloxUpdateProbe>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.Clear();
            var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RORORO", version));
        });

        services.AddSingleton<IClientAppSettingsWriter, ClientAppSettingsWriter>();
        services.AddSingleton<IGlobalBasicSettingsWriter, GlobalBasicSettingsWriter>();
        services.AddSingleton<IGlobalBasicSettingsProbe, GlobalBasicSettingsProbe>();

        // Explicit factory (2026-08-02, settings-quiet-window fix, Task 3) rather than bare
        // AddSingleton<IRobloxLauncher, RobloxLauncher>(): the bare form DOES still auto-resolve
        // IGlobalBasicSettingsProbe from DI today (verified empirically — the built-in container
        // fills a registered optional ctor parameter before falling back to its default), but
        // that's an implicit property of constructor-selection, not a guarantee anyone reading this
        // line would see. Spelling it out means removing the probe registration above fails loudly
        // here (GetRequiredService throws at resolve time) instead of silently reverting to a
        // write-and-hope launch with no settle — which is exactly how the 2026-08-01 wrong-FPS-cap
        // bug shipped once before (the mechanism existed; nothing wired it to a live dependency).
        // Same for a future edit that adds a second RobloxLauncher constructor — the compiler
        // forces this call site to disambiguate. It does NOT protect against every drift: a new
        // *optional* constructor parameter added to the existing constructor takes its default here
        // silently, with nothing failing (fix wave, 2026-08-02 review finding 4 — narrowed this
        // comment's claim to what it actually covers).
        //
        // IRobloxRunningProbe is NOT threaded in here anymore (Task 3): the previous pid-based
        // post-launch gate is retired — it was aimed at the wrong writer (see FpsCapSettler's
        // remarks). IRobloxRunningProbe itself stays registered below for its other consumers
        // (memory watchdog, stray-client cleanup, StartupGate).
        services.AddSingleton<IRobloxLauncher>(sp => new RobloxLauncher(
            sp.GetRequiredService<IRobloxApi>(),
            sp.GetRequiredService<IAppSettings>(),
            sp.GetRequiredService<IProcessStarter>(),
            favorites: sp.GetService<IFavoriteGameStore>(),
            clientAppSettings: sp.GetService<IClientAppSettingsWriter>(),
            globalBasicSettings: sp.GetService<IGlobalBasicSettingsWriter>(),
            settingsProbe: sp.GetRequiredService<IGlobalBasicSettingsProbe>(),
            logger: sp.GetRequiredService<ILogger<RobloxLauncher>>()));

        // Per-capture WebView2 user-data dir manager. Each Add Account gets a fresh GUID dir
        // under %LOCALAPPDATA%\ROROROblox\webview2-data\<guid>\; siblings are best-effort swept
        // on each capture. Replaces the pre-1.3.4 single-shared-dir + pre-wipe pattern, which
        // failed silently when the previous capture's msedgewebview2.exe children still pinned
        // files — causing every subsequent Add Account to re-capture the first account.
        services.AddSingleton(sp => new WebView2UserDataDirectory(
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ROROROblox",
                "webview2-data"),
            sp.GetRequiredService<ILogger<WebView2UserDataDirectory>>()));
        services.AddSingleton<ICookieCapture, CookieCapture.CookieCapture>();
        services.AddSingleton<IUpdateChecker, UpdateChecker>();
        services.AddSingleton<IRobloxProcessTracker, RobloxProcessTracker>();
        services.AddSingleton<RobloxWindowDecorator>();
        services.AddSingleton<RunningRobloxScanner>();

        // Activity Monitor (v1.8) — per-account idle detection. Foreground-window + system-input
        // Win32 probes are read-only (GetForegroundWindow / GetWindowThreadProcessId /
        // GetLastInputInfo); no focus-stealing, no injection. IForegroundAccountResolver points at
        // the same IRobloxProcessTracker singleton, so pid->account lookups stay consistent with
        // the tracker's claimed-pid map.
        services.AddSingleton<IForegroundWindowProbe, Win32ForegroundWindowProbe>();
        services.AddSingleton<ISystemInputClock, Win32SystemInputClock>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IForegroundAccountResolver>(sp =>
            (IForegroundAccountResolver)sp.GetRequiredService<IRobloxProcessTracker>());
        services.AddSingleton<IActivityMonitor>(sp => new ActivityMonitor(
            sp.GetRequiredService<IForegroundWindowProbe>(),
            sp.GetRequiredService<ISystemInputClock>(),
            sp.GetRequiredService<IForegroundAccountResolver>(),
            sp.GetRequiredService<IClock>()));

        // v1.8 idle-alert toast presenter — turns a coalesced warn-threshold crossing into one
        // mutable tray toast. Stateless beyond ITrayService; singleton for consistency with the
        // rest of the notification-adjacent services.
        services.AddSingleton<Notifications.IdleAlertPresenter>();

        // v1.5.0 presence poller (the ghost fix). Singleton — one poll loop for the process.
        // The snapshot-provider delegate reads live accounts from MainViewModel at POLL time,
        // never at construction: MainViewModel also depends on IPresenceService, so resolving it
        // eagerly here would form a construction cycle. The lazy capture below resolves
        // MainViewModel only when the delegate fires (first tick / fast-confirm), well after the
        // graph is built. It snapshots non-expired, userId-resolved accounts as PresenceTargets.
        services.AddSingleton<IPresenceService>(sp => new PresenceService(
            sp.GetRequiredService<IRobloxApi>(),
            sp.GetRequiredService<IAccountStore>(),
            // AccountsSnapshot, not Accounts: this delegate fires on the poll loop's threadpool
            // thread, and enumerating the UI-owned ObservableCollection there races a concurrent
            // Add/Remove into "Collection was modified" — the fault that silently killed the
            // presence loop (2026-06-12 review).
            () => sp.GetRequiredService<MainViewModel>().AccountsSnapshot
                    .Where(a => !a.SessionExpired && a.RobloxUserId is > 0)
                    .Select(a => new PresenceTarget(a.Id, a.RobloxUserId!.Value))
                    .ToList(),
            sp.GetRequiredService<ILogger<PresenceService>>()));

        // Cycle 4 startup gate — detects RobloxPlayerBeta.exe running BEFORE RoRoRo so we
        // can hard-block instead of silently entering the broken state where every Launch As
        // routes through the existing process. Lives next to other Diagnostics-namespace
        // services in Core; consumed by App.OnStartup before mutex.Acquire.
        services.AddSingleton<IRobloxRunningProbe, RobloxRunningProbe>();
        services.AddSingleton<IRobloxInstanceStopper, RobloxInstanceStopper>();
        services.AddSingleton<IRobloxTrayLauncher>(sp =>
            new RobloxTrayLauncher(sp.GetService<ILogger<RobloxTrayLauncher>>()));
        services.AddSingleton<StartupGate>();

        // Memory watchdog (v1.11, Task 7) — samples per-account private bytes + projects time
        // to machine RAM exhaustion. IClock is already registered above (Activity Monitor).
        // Singleton so WireRobloxWindowDecorator's launch/exit bookkeeping and MainViewModel's
        // chip painting share the SAME instance. Started conditionally by WireMemoryWatchdogAsync
        // once the enabled flag + derived defaults are read from IAppSettings.
        services.AddSingleton<IProcessMemoryProbe, ProcessMemoryProbe>();
        services.AddSingleton<ISystemMemoryProbe, SystemMemoryProbe>();
        services.AddSingleton<IMemoryWatchdog, MemoryWatchdog>();

        // Runtime contested-mutex watcher (Task 8) — polls only while we don't hold the mutex,
        // surfacing when the tray-resident Roblox releases it so the runtime banner can offer
        // in-place recovery without a restart. Registered here so its lifetime matches the other
        // Diagnostics singletons; wired to the UI in WireContestedWatcher.
        services.AddSingleton<MutexContestedWatcher>(sp =>
            new MutexContestedWatcher(sp.GetRequiredService<IMutexHolder>()));

        // Cycle 5 RobloxUserId backfill — fire-and-forget worker that resolves and persists
        // any saved account where RobloxUserId is null. Triggered post-MainWindow.Show in
        // RunStartupChecksAsync; idempotent on subsequent runs. Lives in Core for testability.
        services.AddSingleton<AccountUserIdBackfillService>();

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

        // Discord presence + Join config (Task 9) — DPAPI-encrypted, alongside accounts.dat and
        // consent.dat under the same data dir. Registered unconditionally, independent of whether
        // Discord:ApplicationId is configured: Preferences needs to read/write the toggle state
        // (see PreferencesWindow) even in a build where the feature itself can't run yet, so a
        // saved preference is honored automatically the moment a future build ships an app id.
        services.AddSingleton(_ => new DiscordConfigStore(
            System.IO.Path.Combine(dataDir, "discord.dat")));

        // Alerts are registered unconditionally and — unlike presence — do NOT depend on
        // Discord:ApplicationId or on Discord being installed at all. The desktop-notification
        // destination is the whole point: someone who never makes a webhook still gets told when
        // a client drops. Gating this on the Discord app id would silently remove the only part
        // of the feature that needs no setup.
        services.AddHttpClient<DiscordWebhookSender>(client =>
        {
            // Short timeout: an alert that has not landed in ten seconds has missed its moment,
            // and a hung POST must never be what keeps the app alive at shutdown.
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.Clear();
            var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RORORO", version));
        });

        services.AddHttpClient<WebhookProbe>(client =>
        {
            // Setup help is best-effort and blocks a visible field, so it fails fast.
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.UserAgent.Clear();
            var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RORORO", version));
        });

        services.AddSingleton<DiscordConfigCache>();

        services.AddSingleton(sp => new AlertDispatcher(
            sp.GetRequiredService<DiscordWebhookSender>(),
            sp.GetRequiredService<ITrayService>(),
            () => sp.GetRequiredService<DiscordConfigCache>().Current,
            TimeProvider.System,
            sp.GetRequiredService<ILogger<AlertDispatcher>>()));

        services.AddSingleton<IDiagnosticsCollector>(sp => new DiagnosticsCollector(
            sp.GetRequiredService<IAccountStore>(),
            sp.GetRequiredService<IRobloxProcessTracker>(),
            sp.GetRequiredService<IMutexHolder>(),
            AppLogging.LogDirectory,
            dataDir,
            sp.GetRequiredService<ISystemMemoryProbe>(),
            sp.GetRequiredService<IMemoryWatchdog>()));

        // ViewModel + Window.
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        // === Plugins (v1.4) =========================================================
        // Wires the gRPC plugin host (items 5-14) into the App lifecycle. The host runs
        // on a per-user named pipe; failure to start MUST NOT break RoRoRo's normal
        // launch — every plugin-side service is wrapped at the App.OnStartup hook so a
        // broken plugins root or a corrupt consent file just disables plugin support.
        var pluginsRoot = ROROROblox.App.Plugins.PluginRegistry.DefaultPluginsRoot;
        var consentPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ROROROblox", "consent.dat");

        services.AddSingleton(_ => new ROROROblox.App.Plugins.ConsentStore(consentPath));
        services.AddSingleton(sp => new ROROROblox.App.Plugins.PluginRegistry(
            pluginsRoot,
            sp.GetRequiredService<ROROROblox.App.Plugins.ConsentStore>()));
        services.AddSingleton<ROROROblox.App.Plugins.IInstalledPluginsLookup>(sp =>
            new ROROROblox.App.Plugins.Adapters.InstalledPluginsLookupAdapter(
                sp.GetRequiredService<ROROROblox.App.Plugins.PluginRegistry>()));

        // PluginInstaller takes (HttpClient, pluginsRoot, stopRunningPluginAsync) — register
        // the typed HttpClient with the right UA, then build the installer with the resolved
        // client + a stop-hook that kills a running instance before a re-install touches its dir.
        services.AddHttpClient(nameof(ROROROblox.App.Plugins.PluginInstaller), client =>
        {
            var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("ROROROblox-PluginInstaller", version));
        });
        services.AddSingleton(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>()
                .CreateClient(nameof(ROROROblox.App.Plugins.PluginInstaller));
            var supervisor = sp.GetRequiredService<ROROROblox.App.Plugins.PluginProcessSupervisor>();
            // Host version sourced from the App assembly — the manifest minHostVersion
            // gate compares against this. Fallback of 0.0.0.0 means a missing AssemblyVersion
            // refuses every minHostVersion-bearing manifest, which is the safe default.
            var hostVersion = typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
            return new ROROROblox.App.Plugins.PluginInstaller(http, pluginsRoot, (pluginId, installDir) =>
                // Re-installing fails on Directory.Delete if anything is still running out of
                // the plugin's dir — a tracked instance OR an orphan that outlived a prior
                // RoRoRo session. StopByInstallDirAsync kills both (orphans found by image
                // path) and polls until their file handles release.
                supervisor.StopByInstallDirAsync(pluginId, installDir),
                hostVersion);
        });

        services.AddSingleton<ROROROblox.App.Plugins.IPluginProcessStarter,
            ROROROblox.App.Plugins.Adapters.DefaultPluginProcessStarter>();
        services.AddSingleton<ROROROblox.App.Plugins.PluginProcessSupervisor>();

        services.AddSingleton<ROROROblox.App.Plugins.IPluginEventBus,
            ROROROblox.App.Plugins.InProcessPluginEventBus>();
        services.AddSingleton<ROROROblox.App.Plugins.IPluginHostStateProvider,
            ROROROblox.App.Plugins.Adapters.MutexHostStateAdapter>();
        services.AddSingleton<ROROROblox.App.Plugins.IRunningAccountsProvider,
            ROROROblox.App.Plugins.Adapters.MainViewModelRunningAccountsAdapter>();
        services.AddSingleton<ROROROblox.App.Plugins.IActivitySnapshotProvider,
            ROROROblox.App.Plugins.ActivitySnapshotProvider>();
        services.AddSingleton<ROROROblox.App.Plugins.IAccountActivityMarker>(sp =>
            new ROROROblox.App.Plugins.AccountActivityMarker(
                sp.GetRequiredService<IActivityMonitor>(),
                sp.GetRequiredService<IClock>()));
        services.AddSingleton<ROROROblox.App.Plugins.IPluginLaunchInvoker,
            ROROROblox.App.Plugins.Adapters.MainViewModelLaunchInvokerAdapter>();
        services.AddSingleton<ROROROblox.App.Plugins.IPluginUIHost,
            ROROROblox.App.Plugins.Adapters.WpfPluginUIHost>();
        services.AddSingleton<ROROROblox.App.Plugins.PluginUITranslator>();
        services.AddSingleton<ROROROblox.App.Plugins.IPluginAccountStopper,
            ROROROblox.App.Plugins.Adapters.ProcessTrackerAccountStopper>();

        services.AddSingleton(sp => new ROROROblox.App.Plugins.PluginHostService(
            sp.GetRequiredService<ROROROblox.App.Plugins.IInstalledPluginsLookup>(),
            typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            "1.0",
            sp.GetRequiredService<ROROROblox.App.Plugins.IPluginHostStateProvider>(),
            sp.GetRequiredService<ROROROblox.App.Plugins.IRunningAccountsProvider>(),
            sp.GetRequiredService<ROROROblox.App.Plugins.IPluginEventBus>(),
            sp.GetRequiredService<ROROROblox.App.Plugins.IPluginLaunchInvoker>(),
            sp.GetRequiredService<ROROROblox.App.Plugins.PluginUITranslator>(),
            sp.GetRequiredService<ROROROblox.App.Plugins.IActivitySnapshotProvider>(),
            sp.GetRequiredService<ROROROblox.App.Plugins.IAccountActivityMarker>(),
            sp.GetRequiredService<ROROROblox.App.Plugins.IPluginAccountStopper>()));

        // CapabilityInterceptor: per-connection plugin id binding is deferred to v1.5+
        // (the gRPC interceptor sees the call before any plugin-id metadata is bound).
        // For v1.4 we pass a no-op accessor; the consent lookup goes through the
        // (decrypted) ConsentStore each call. Wrapped in try/catch so a corrupt
        // consent.dat returns empty capabilities rather than throwing into the host.
        services.AddSingleton(sp => new ROROROblox.App.Plugins.CapabilityInterceptor(
            currentPluginAccessor: () => null,
            consentLookup: pluginId =>
            {
                try
                {
                    var consent = sp.GetRequiredService<ROROROblox.App.Plugins.ConsentStore>();
                    var records = consent.ListAsync().GetAwaiter().GetResult();
                    return records.FirstOrDefault(r => r.PluginId == pluginId)?.GrantedCapabilities
                        ?? Array.Empty<string>();
                }
                catch
                {
                    return Array.Empty<string>();
                }
            }));
        services.AddSingleton<ROROROblox.App.Plugins.PluginHostStartupService>();
    }

    private void WireTrayEvents(ITrayService tray, IMutexHolder mutex, MainWindow mainWindow)
    {
        tray.RequestOpenMainWindow += (_, _) => SurfaceMainWindow(mainWindow);
        tray.RequestToggleMutex += (_, _) => ToggleMutex(mutex, tray);
        tray.RequestStopAllInstances += (_, _) => StopAllInstances();
        tray.RequestQuit += (_, _) => RequestShutdown();
        tray.RequestOpenDiagnostics += (_, _) => OpenDiagnosticsFromTray(mainWindow);
        tray.RequestOpenLogs += (_, _) => OpenLogsFolder();
        tray.RequestOpenPreferences += (_, _) => OpenPreferencesFromTray(mainWindow);
        tray.RequestActivateMain += (_, _) => ActivateMainFromTray(mainWindow);
        tray.RequestOpenHistory += (_, _) => OpenHistoryFromTray(mainWindow);
        tray.RequestOpenPlugins += (_, _) => OpenPluginsFromTray(mainWindow);
        // Task 8 — the whole reason ShowMemoryWarning carries an accountId: a balloon click that
        // goes nowhere wastes it. TrayBalloonTipClicked is a WPF-originated UI event (unlike
        // PressureCrossed), so no dispatcher marshaling is needed here.
        tray.RequestFocusAccount += (_, accountId) =>
        {
            try
            {
                SurfaceMainWindow(mainWindow);
                mainWindow.FocusAccountRow(accountId);
            }
            catch (Exception ex)
            {
                _log?.LogDebug(ex, "RequestFocusAccount handling threw; window surfaced (if it got that far) but the row may not be highlighted.");
            }
        };
    }

    private void WireMainViewModelEvents(MainWindow mainWindow)
    {
        if (_services is null) return;
        var vm = _services.GetRequiredService<MainViewModel>();
        vm.RequestOpenPlugins += (_, _) => OpenPluginsFromTray(mainWindow);
    }

    private void OpenPluginsFromTray(Window owner)
    {
        if (_services is null) return;
        try
        {
            var registry = _services.GetRequiredService<ROROROblox.App.Plugins.PluginRegistry>();
            var registryAdapter = (ROROROblox.App.Plugins.Adapters.InstalledPluginsLookupAdapter)
                _services.GetRequiredService<ROROROblox.App.Plugins.IInstalledPluginsLookup>();
            var consentStore = _services.GetRequiredService<ROROROblox.App.Plugins.ConsentStore>();
            var installer = _services.GetRequiredService<ROROROblox.App.Plugins.PluginInstaller>();
            var supervisor = _services.GetRequiredService<ROROROblox.App.Plugins.PluginProcessSupervisor>();

            // Show the manifest consent sheet so the user can review + per-capability opt
            // out (system.* default-off, host.* default-on). Cancel returns null and the VM
            // rolls back the install dir.
            Func<ROROROblox.App.Plugins.PluginManifest, Task<IReadOnlyList<string>?>> showSheet =
                manifest => ROROROblox.App.Plugins.ConsentSheet.ShowAndAwaitDecisionAsync(owner, manifest);

            var catalogHttp = _services.GetRequiredService<IHttpClientFactory>().CreateClient();
            catalogHttp.DefaultRequestHeaders.UserAgent.Clear();
            var catalogVersion = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            catalogHttp.DefaultRequestHeaders.UserAgent.Add(
                new System.Net.Http.Headers.ProductInfoHeaderValue("RORORO", catalogVersion));
            var catalogClient = new ROROROblox.App.Plugins.PluginCatalogClient(
                catalogHttp,
                "https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/plugins-catalog.json");
            var vm = new ROROROblox.App.Plugins.PluginsViewModel(
                registry, registryAdapter, consentStore, installer, supervisor, showSheet,
                new ROROROblox.App.Distribution.Win32DistributionMode(),
                catalogClient,
                typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0, 0),
                _services.GetRequiredService<ILogger<ROROROblox.App.Plugins.PluginsViewModel>>());
            var window = new ROROROblox.App.Plugins.PluginsWindow(vm);
            if (owner.IsLoaded) window.Owner = owner;
            SurfaceMainWindow(owner);
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Couldn't open Plugins window from tray");
        }
    }

    private void OpenHistoryFromTray(Window owner)
    {
        if (_services is null) return;
        try
        {
            var store = _services.GetRequiredService<ISessionHistoryStore>();
            var favorites = _services.GetRequiredService<IFavoriteGameStore>();
            var api = _services.GetRequiredService<IRobloxApi>();
            // Same streamer-identity singleton MainViewModel.OpenHistory threads through — the tray
            // entry point is the OTHER way to open this window, and without this it would show the
            // real roster even while streamer mode is active (the exact "one tray-click reveals the
            // whole real roster" leak).
            var streamerIdentity = _services.GetRequiredService<ROROROblox.Core.StreamerMode.IStreamerIdentityProvider>();
            var window = new History.SessionHistoryWindow(store, favorites, api, streamerIdentity);
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
    /// <para>
    /// Also the single place the memory watchdog (Task 7) learns about launches/exits —
    /// <see cref="IMemoryWatchdog.OnAccountLaunched"/>/<see cref="IMemoryWatchdog.OnAccountExited"/>
    /// fire right alongside <c>decorator.Track</c>/<c>Untrack</c> so ALL per-account process
    /// bookkeeping stays in one call site instead of a second parallel subscription. Cheap
    /// dictionary ops even when the watchdog's sample timer was never started (disabled in
    /// settings) — see <see cref="WireMemoryWatchdogAsync"/>.
    /// </para>
    /// </summary>
    private void WireRobloxWindowDecorator()
    {
        if (_services is null) return;
        try
        {
            var tracker = _services.GetRequiredService<IRobloxProcessTracker>();
            var decorator = _services.GetRequiredService<RobloxWindowDecorator>();
            var vm = _services.GetRequiredService<MainViewModel>();
            var watchdog = _services.GetRequiredService<IMemoryWatchdog>();

            tracker.ProcessAttached += (_, e) =>
            {
                // AccountsSnapshot: tracker events fire on threadpool continuations.
                var summary = vm.AccountsSnapshot.FirstOrDefault(a => a.Id == e.AccountId);
                if (summary is null) return;
                decorator.Track(e.Pid, summary);
                watchdog.OnAccountLaunched(e.AccountId, e.Pid);
            };
            tracker.ProcessExited += (_, e) =>
            {
                decorator.Untrack(e.Pid);
                watchdog.OnAccountExited(e.AccountId, e.Pid);
            };
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "WireRobloxWindowDecorator failed; window titles stay default.");
        }
    }

    /// <summary>
    /// Seeds <see cref="IActivityMonitor"/> with any already-attached accounts (e.g. re-attached
    /// by <see cref="RunningRobloxScanner"/> before this runs), subscribes it to
    /// <see cref="IRobloxProcessTracker"/>'s launch/exit lifecycle, and starts its 1s sample
    /// timer. Wrapped defensively — a wiring failure here must not block RoRoRo's normal launch;
    /// worst case is idle-warning simply doesn't fire this session.
    /// </summary>
    private void WireActivityMonitor()
    {
        if (_services is null) return;
        try
        {
            var tracker = _services.GetRequiredService<IRobloxProcessTracker>();
            var monitor = _services.GetRequiredService<IActivityMonitor>();

            foreach (var id in tracker.Attached.Keys)
            {
                monitor.OnAccountLaunched(id);
            }

            tracker.ProcessAttached += (_, e) => monitor.OnAccountLaunched(e.AccountId);
            tracker.ProcessExited += (_, e) => monitor.OnAccountExited(e.AccountId);
            monitor.Start();
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "WireActivityMonitor failed; idle warnings disabled this session.");
        }
    }

    /// <summary>
    /// Memory watchdog (v1.11, Task 7) — reads the enabled flag + reserve/cap/projection settings
    /// from <see cref="IAppSettings"/> and starts <see cref="IMemoryWatchdog"/>'s sample timer.
    /// <para>
    /// <b>Derive ONCE, never re-derive over an explicit value.</b> <c>MemoryReserveMb</c>/
    /// <c>MemoryCapMb</c> are <c>int?</c>: <see langword="null"/> means the user never touched the
    /// setting (derive from installed RAM via <see cref="MemoryDefaults"/>), any value — including
    /// <c>0</c> for the cap — is a deliberate user choice that must be honoured verbatim (<c>0</c>
    /// disables the cap trigger; see <see cref="MemoryDefaults.CapMb"/> doc). The pattern is
    /// <c>settings.Value ?? MemoryDefaults.XMb(total)</c>, converted MB -&gt; bytes with
    /// <c>* 1024L * 1024L</c>.
    /// </para>
    /// <para>
    /// <see cref="IMemoryWatchdog.OnAccountLaunched"/>/<c>OnAccountExited</c> bookkeeping is wired
    /// separately, in <see cref="WireRobloxWindowDecorator"/>, at the same
    /// <see cref="IRobloxProcessTracker.ProcessAttached"/>/<c>ProcessExited</c> call sites the
    /// decorator uses — this method only owns settings + <see cref="IMemoryWatchdog.Start"/>.
    /// </para>
    /// <para>
    /// <c>MemoryWatchdogEnabled == false</c> returns before <c>Start()</c> — no timer, no sampling,
    /// no cost. Wrapped defensively like every other Wire*/Initialize* startup step: a watchdog
    /// wiring failure must never block a user from launching Roblox.
    /// </para>
    /// </summary>
    private async Task WireMemoryWatchdogAsync()
    {
        if (_services is null) return;
        try
        {
            var settings = _services.GetRequiredService<IAppSettings>();
            if (!await settings.GetMemoryWatchdogEnabledAsync().ConfigureAwait(true))
            {
                return;
            }

            var watchdog = _services.GetRequiredService<IMemoryWatchdog>();
            var systemProbe = _services.GetRequiredService<ISystemMemoryProbe>();
            systemProbe.TryRead(out var totalPhysicalBytes, out _);

            var reserveMb = await settings.GetMemoryReserveMbAsync().ConfigureAwait(true);
            var capMb = await settings.GetMemoryCapMbAsync().ConfigureAwait(true);
            watchdog.ReserveBytes = (reserveMb ?? MemoryDefaults.ReserveMb(totalPhysicalBytes)) * 1024L * 1024L;
            watchdog.CapBytes = (capMb ?? MemoryDefaults.CapMb(totalPhysicalBytes)) * 1024L * 1024L;
            watchdog.ProjectionWarnMinutes = await settings.GetProjectionWarnMinutesAsync().ConfigureAwait(true);

            watchdog.Start();
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Memory watchdog wiring failed; continuing without it.");
        }
    }

    /// <summary>
    /// Bridges <see cref="IMemoryWatchdog.PressureCrossed"/> to the tray warning badge + balloon
    /// (Task 8). Deliberately its own Wire* method — <see cref="WireMemoryWatchdogAsync"/> only
    /// owns settings + <see cref="IMemoryWatchdog.Start"/>, matching that method's own doc note
    /// that <c>OnAccountLaunched</c>/<c>OnAccountExited</c> bookkeeping lives elsewhere too.
    /// <para>
    /// <see cref="IMemoryWatchdog.PressureCrossed"/> fires from <c>MemoryWatchdog.Sample()</c>,
    /// which runs on the watchdog's own <see cref="System.Threading.Timer"/> callback — NOT the UI
    /// thread. <see cref="TrayService.SetMemoryWarning"/>/<see cref="TrayService.ShowMemoryWarning"/>
    /// marshal internally, so this handler doesn't need to; it still wraps in try/catch because an
    /// unhandled exception on a threadpool timer callback takes the whole process down, and this is
    /// the one code path that only runs when a user is already in trouble.
    /// </para>
    /// <para>
    /// Only ever calls <c>SetMemoryWarning(true)</c> here — edge-triggered, once per latched
    /// crossing, matching <c>ShowMemoryWarning</c>'s own contract. Clearing back to <c>false</c> is
    /// NOT this method's job: <see cref="MainViewModel"/>'s existing 30s ticker re-evaluates
    /// <see cref="MemoryPressureEvaluator.IsClear"/> against the watchdog's latest snapshot and
    /// clears the badge once the warning condition actually recedes.
    /// </para>
    /// </summary>
    private void WireMemoryWarningTray()
    {
        if (_services is null) return;
        try
        {
            var watchdog = _services.GetRequiredService<IMemoryWatchdog>();
            var tray = _services.GetRequiredService<ITrayService>();
            var vm = _services.GetRequiredService<MainViewModel>();

            watchdog.PressureCrossed += (_, snap) =>
            {
                try
                {
                    tray.SetMemoryWarning(true);

                    if (snap.TargetAccountId is not { } targetId)
                    {
                        // Shouldn't happen alongside a crossing -- Sample() only crosses an
                        // account it could actually read a byte count for -- but degrade to
                        // "badge only, no balloon" rather than guess at an account to name.
                        _log?.LogDebug("PressureCrossed fired with no TargetAccountId; badge set, balloon skipped.");
                        return;
                    }

                    // AccountsSnapshot: this handler runs on the watchdog's timer thread.
                    var name = vm.AccountsSnapshot.FirstOrDefault(a => a.Id == targetId)?.RenderName ?? "An account";
                    tray.ShowMemoryWarning(
                        "RoRoRo — memory warning",
                        $"{name} is using a lot of memory. Click to jump to it, then hit Recycle to close and relaunch into the same game.",
                        targetId);
                }
                catch (Exception ex)
                {
                    _log?.LogWarning(ex, "Memory-warning tray bridge threw; the warning may not have surfaced for this crossing.");
                }
            };
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Memory-warning tray wiring failed; the tray badge/balloon won't fire this session.");
        }
    }

    /// <summary>
    /// Pushes the cached idle-warn threshold + mute flag from <see cref="IAppSettings"/> into
    /// <see cref="MainViewModel"/> (which forwards the threshold into
    /// <see cref="IActivityMonitor.WarnThreshold"/>). Called once at startup after
    /// <see cref="WireActivityMonitor"/> has started the monitor, and again from
    /// <see cref="OpenPreferencesFromTray"/> after the Preferences dialog closes so an edited
    /// threshold/mute takes effect without a restart. Wrapped defensively — a settings-read
    /// failure must not block startup; idle awareness just falls back to the 15-minute default.
    /// </summary>
    private async Task InitializeIdleSettingsAsync()
    {
        if (_services is null) return;
        try
        {
            var settings = _services.GetRequiredService<IAppSettings>();
            var vm = _services.GetRequiredService<MainViewModel>();
            await vm.InitializeIdleSettingsAsync(settings);
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "InitializeIdleSettingsAsync failed; idle awareness stays at defaults.");
        }
    }

    /// <summary>
    /// Streamer mode (v1.9) — seed the identity provider with every saved account's persisted
    /// fake identity before the provider is ever consulted. Reads accounts directly from
    /// <see cref="IAccountStore"/> rather than <see cref="MainViewModel.AccountsSnapshot"/>: the
    /// VM's own load is kicked off by MainWindow's Loaded handler and isn't guaranteed to have
    /// completed by this point, while the store read here is independent and authoritative.
    /// Guarded like every other OnStartup init step — a seeding failure must not block startup;
    /// worst case, identities lazily reassign (and re-persist) the first time streamer mode
    /// resolves an account that missed the seed.
    /// </summary>
    private async Task InitializeStreamerModeAsync()
    {
        if (_services is null) return;
        try
        {
            var accountStore = _services.GetRequiredService<IAccountStore>();
            var provider = _services.GetRequiredService<ROROROblox.Core.StreamerMode.IStreamerIdentityProvider>();
            var accounts = await accountStore.ListAsync();
            var seed = accounts
                .Where(a => !string.IsNullOrEmpty(a.StreamerName))
                .Select(a => (a.Id, new ROROROblox.Core.StreamerMode.StreamerIdentity(a.StreamerName!, a.StreamerAvatarId ?? "noodle")))
                .ToList();
            await provider.InitializeAsync(seed);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Streamer-mode identity provider seed failed; identities will lazily assign on first resolve.");
        }
    }

    /// <summary>
    /// Discord presence + Join, startup half (Task 9). Skips the whole feature — no client, no
    /// presence service, nothing assigned to <see cref="MainViewModel.DiscordPresence"/> — when
    /// <c>Discord:ApplicationId</c> (appsettings.json) is empty, which it is by default.
    /// <see cref="LacheeDiscordRpcClientAdapter.Initialize"/> already guards the same case
    /// defensively, but constructing and starting a presence service that can never connect is
    /// pointless ceremony, and Preferences needs to know NOW (not discover it lazily on first
    /// toggle) so it can disable the checkboxes and say why instead of showing controls that
    /// quietly do nothing. <see cref="JoinUriScheme.Register"/> already ran earlier in
    /// <see cref="OnStartup"/>, well before this — Discord refuses to accept a presence carrying
    /// join secrets until that registration exists.
    /// <para>
    /// Subscribes BOTH inbound-join paths to the same <see cref="MainViewModel.HandleDiscordJoinAsync"/>
    /// — the brief for this task only mentions <see cref="DiscordPresenceService.JoinRequested"/>
    /// (the in-client Join button), but <see cref="JoinRequested"/> (this class's own event, the
    /// <c>roblox-rororo:</c> OS protocol-handler path Task 7 built) has no other subscriber
    /// anywhere in the codebase. Without this second subscription, a Join click that arrives while
    /// RoRoRo is closed (Discord cold-starts us with the URI) decodes correctly, raises the event,
    /// and then goes nowhere — the entire protocol-handler path would ship as dead code.
    /// </para>
    /// <para>
    /// The two events are marshalled differently, per each one's own doc remarks:
    /// <see cref="JoinRequested"/> already arrives on the UI thread (<c>SingleInstanceGuard</c>
    /// raises its relay inside <c>Dispatcher.Invoke</c>; cold start runs from <c>OnStartup</c>,
    /// itself on the UI thread) and is wired directly. <see cref="DiscordPresenceService.JoinRequested"/>
    /// arrives on Lachee's background RPC thread with nothing in its chain that marshals, so it is
    /// hopped onto the UI thread via <c>Dispatcher.InvokeAsync</c> before ever touching
    /// <see cref="MainViewModel"/> or showing <see cref="Modals.JoinRequestWindow"/> — skipping
    /// that hop is the common crash here (every in-client Join goes through this path), not a rare
    /// edge case.
    /// </para>
    /// <para>
    /// No account name enters the presence payload today — <c>PresencePayloadBuilder.Build</c>
    /// only ever reads <c>InGame</c>/<c>Server</c>/<c>GameName</c>/<c>InGameSinceUtc</c> off each
    /// <see cref="ROROROblox.Core.Discord.RosterAccount"/>, never <c>DisplayName</c> — so streamer
    /// mode flipping <c>RenderName</c> has nothing to leak outbound and does not need to trigger
    /// <see cref="DiscordPresenceService.Refresh"/>. If a future change puts a name on the wire,
    /// that same commit must wire <c>IStreamerIdentityProvider.Changed</c> to
    /// <c>DiscordPresence?.Refresh()</c> (Task 6 review finding).
    /// </para>
    /// <para>
    /// <b>Fix round 1, Finding 1.</b> Both events are dispatched through one
    /// <see cref="InboundJoinDispatcher"/> instance rather than calling
    /// <c>HandleDiscordJoinAsync</c> directly, so both gate on the LIVE <c>JoinEnabled</c> setting
    /// (<see cref="DiscordPresenceService.JoinEnabled"/>) before dispatching anything. Before this
    /// fix, the <see cref="JoinRequested"/> (protocol-handler) path had no such gate anywhere in its
    /// chain — <see cref="JoinUriScheme.Register"/> runs unconditionally on every install, and
    /// neither <c>InboundJoinRelay.Handle</c> nor <c>HandleDiscordJoinAsync</c> check
    /// <c>JoinEnabled</c> — so once a real application id shipped, the URI alone could launch a
    /// saved account (and take over a running session) whether or not the user ever turned Join on.
    /// </para>
    /// </summary>
    /// <summary>
    /// Bridges <see cref="MainViewModel.AlertsRaised"/> to <see cref="AlertDispatcher"/>.
    /// <para>
    /// Deliberately independent of Discord presence: this runs whether or not Discord is
    /// installed, configured, or running, because the desktop-notification destination is the one
    /// that needs no setup and therefore the one most users will actually have.
    /// </para>
    /// </summary>
    private async Task WireAlertsAsync()
    {
        if (_services is null) return;
        try
        {
            await RefreshDiscordConfigAsync().ConfigureAwait(true);

            var vm = _services.GetRequiredService<MainViewModel>();
            var dispatcher = _services.GetRequiredService<AlertDispatcher>();

            // Paint the saved mutes onto the rows. Without this the preference persists but the
            // row shows unmuted after every restart — the user re-mutes an account that was never
            // going to alert, and stops trusting the toggle.
            var muted = _services.GetRequiredService<DiscordConfigCache>().Current.MutedAccountIds.ToHashSet();
            foreach (var row in vm.Accounts)
            {
                row.AlertsMuted = muted.Contains(row.Id);
            }

            // Fire-and-forget on purpose: AlertsRaised is raised from ApplyPresence and the
            // watchdog's crossing handler, both of which are on the UI thread and neither of which
            // may be made to wait on an HTTP POST to Discord. DispatchAsync swallows its own
            // failures (see its remarks), so there is no unobserved-exception hazard here.
            vm.AlertsRaised += (_, triggers) => _ = dispatcher.DispatchAsync(triggers);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Couldn't wire Discord alerts; the app runs without them.");
        }
    }

    /// <summary>Reload <see cref="DiscordConfigCache"/> from the store. Called at startup and on
    /// Preferences close — the only two moments these settings change.</summary>
    private async Task RefreshDiscordConfigAsync()
    {
        if (_services is null) return;
        try
        {
            _services.GetRequiredService<DiscordConfigCache>().Current =
                await _services.GetRequiredService<DiscordConfigStore>().LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Couldn't read Discord settings; alerts stay at their defaults (off).");
        }
    }

    private async Task WireDiscordPresenceAsync(MainWindow mainWindow, string applicationId)
    {
        if (_services is null) return;
        try
        {
            // FIX 6: applicationId is now read once, by OnStartup, BEFORE deciding whether to
            // register the URI scheme — see that call site's remarks. Re-checking here (rather
            // than trusting the caller) keeps this method safe to call on its own.
            if (string.IsNullOrWhiteSpace(applicationId))
            {
                _log?.LogDebug("Discord:ApplicationId is empty; Discord presence disabled this session.");
                return;
            }

            var vm = _services.GetRequiredService<MainViewModel>();
            var configStore = _services.GetRequiredService<DiscordConfigStore>();
            var config = await configStore.LoadAsync().ConfigureAwait(true);

            var client = new LacheeDiscordRpcClientAdapter(
                applicationId, _services.GetRequiredService<ILogger<LacheeDiscordRpcClientAdapter>>());
            var presence = new DiscordPresenceService(
                client,
                vm.BuildRosterSnapshot,
                _services.GetRequiredService<ILogger<DiscordPresenceService>>());
            vm.DiscordPresence = presence;

            await presence.ApplyAsync(config).ConfigureAwait(true);

            // Fix round 1, Finding 1: both inbound-join paths gate on the LIVE JoinEnabled setting.
            // Fix round 2: they're two SEPARATE InboundJoinDispatcher instances now, not one shared
            // one — origin is fixed per call site and, as of round 2, changes what
            // HandleDiscordJoinAsync does (a UriHandler join always confirms; a DiscordClient join
            // only confirms for a private server). See InboundJoinDispatcher's + HandleDiscordJoinAsync's
            // remarks for the full reasoning.
            //
            // FIX 4 (final whole-branch review, 2026-08-03): the two origins are NOT equally
            // visible when their confirm fires. Both URI paths already surface the window before
            // this delegate can run — SingleInstanceGuard's relay calls SurfaceWindow inside the
            // same Dispatcher.Invoke that raises JoinUriReceived, and cold start's mainWindow.Show()
            // runs before OnStartup ever dispatches the cold-start join. The in-client Discord Join
            // button has no such guarantee: RoRoRo's steady state is minimized to tray, and nothing
            // upstream of DiscordPresenceService.JoinRequested touches the window. Without this,
            // JoinRequestWindow.Confirm centers on an owner that is not on screen — surface it first.
            var discordClientConfirm = new Func<string, bool>(msg =>
            {
                SurfaceMainWindow(mainWindow);
                return Modals.JoinRequestWindow.Confirm(mainWindow, msg);
            });
            var uriHandlerConfirm = new Func<string, bool>(msg => Modals.JoinRequestWindow.Confirm(mainWindow, msg));
            var inClientJoin = new InboundJoinDispatcher(
                joinEnabled: () => presence.JoinEnabled,
                viewModel: vm,
                origin: JoinOrigin.DiscordClient,
                confirm: discordClientConfirm,
                log: _log);
            var uriHandlerJoin = new InboundJoinDispatcher(
                joinEnabled: () => presence.JoinEnabled,
                viewModel: vm,
                origin: JoinOrigin.UriHandler,
                confirm: uriHandlerConfirm,
                log: _log);

            // In-client Join button — Lachee's background RPC thread. Must hop to the UI thread
            // before reaching InboundJoinDispatcher.HandleAsync (see this method's remarks + both
            // types' own doc comments) — it ends up at HandleDiscordJoinAsync, which can show a
            // modal (JoinRequestWindow.Confirm) and reach LaunchAccountAsync, neither of which
            // should run on the RPC thread. InvokeAsync with an async lambda, not a bare Invoke, so
            // the hop doesn't block that thread either.
            presence.JoinRequested += (_, target) =>
                Dispatcher.InvokeAsync(async () => await inClientJoin.HandleAsync(target).ConfigureAwait(true));

            // roblox-rororo: URI (cold start or relayed from a second instance) — already
            // UI-thread-marshalled by the time it reaches here; see JoinRequested's own remarks.
            // Fire-and-forget is safe: HandleAsync never throws (see its own doc comment).
            JoinRequested += (_, target) => _ = uriHandlerJoin.HandleAsync(target);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Discord presence wiring failed; continuing without it.");
        }
    }

    /// <summary>
    /// Reads <c>Discord:ApplicationId</c> straight out of appsettings.json next to the exe. No
    /// <c>IConfiguration</c> binder runs anywhere in this process —
    /// <see cref="ROROROblox.App.Plugins.PluginHostStartupService"/> deliberately uses
    /// <c>WebApplication.CreateSlimBuilder</c> specifically to skip that config stack (see its own
    /// remarks) — so this is a direct, minimal JSON read rather than pulling in
    /// Microsoft.Extensions.Configuration.Json for one string. A missing file, malformed JSON, or
    /// a missing/non-string key all resolve to <see cref="string.Empty"/> (feature off), same
    /// tamper-tolerant shape as <see cref="DiscordConfigStore"/>.
    /// </summary>
    /// <summary>
    /// The single place that knows what the Preferences dialog needs. Both entry points — the tray
    /// menu and the main window's own command (via
    /// <see cref="MainViewModel.PreferencesWindowFactory"/>) — go through here, so a dependency
    /// added to that window is added once, in the composition root, rather than in every caller.
    /// </summary>
    private Preferences.PreferencesWindow BuildPreferencesWindow()
    {
        ArgumentNullException.ThrowIfNull(_services);
        return new Preferences.PreferencesWindow(
            _services.GetRequiredService<IAppSettings>(),
            _services.GetRequiredService<IStartupRegistration>(),
            _services.GetRequiredService<IThemeStore>(),
            _services.GetRequiredService<ThemeService>(),
            _services.GetRequiredService<IAccountStore>(),
            _services.GetRequiredService<ROROROblox.Core.Transport.IAccountTransport>(),
            _services.GetRequiredService<MainViewModel>(),
            _services.GetRequiredService<DiscordConfigStore>(),
            _services.GetRequiredService<AlertDispatcher>(),
            _services.GetRequiredService<DiscordWebhookSender>(),
            _services.GetRequiredService<WebhookProbe>(),
            _services.GetRequiredService<DiscordConfigCache>());
    }

    private static string ReadDiscordApplicationId()
    {
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!System.IO.File.Exists(path))
            {
                return string.Empty;
            }

            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("Discord", out var discord) &&
                discord.TryGetProperty("ApplicationId", out var appId) &&
                appId.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return appId.GetString() ?? string.Empty;
            }

            return string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
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
            var accountStore = _services.GetRequiredService<IAccountStore>();
            var transport = _services.GetRequiredService<ROROROblox.Core.Transport.IAccountTransport>();
            var mainViewModel = _services.GetRequiredService<MainViewModel>();
            var discordConfigStore = _services.GetRequiredService<DiscordConfigStore>();
            var window = BuildPreferencesWindow();
            if (owner.IsLoaded) window.Owner = owner;
            SurfaceMainWindow(owner);
            window.ShowDialog();

            // v1.8 — the Preferences dialog persists idle-awareness edits (mute + threshold)
            // immediately on click, mirroring every other toggle in that window. Re-push into
            // the monitor + VM on close so a changed threshold/mute takes effect without a
            // restart, regardless of which control the user touched last.
            _ = InitializeIdleSettingsAsync();

            // Same reasoning as the idle re-push above: Preferences persists Discord alert routing
            // and webhook edits on click, so re-read them on close or the dispatcher keeps routing
            // against whatever was loaded at startup until the next restart.
            _ = RefreshDiscordConfigAsync();
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

    private void WireMutexLost(IMutexHolder mutex, ITrayService tray)
    {
        mutex.MutexLost += (_, _) =>
        {
            // MutexLost fires on a System.Timers.Timer thread; marshal to UI for tray update.
            Current.Dispatcher.Invoke(() => tray.UpdateStatus(MultiInstanceState.Error));
            // Plugin bus is thread-safe (synchronous Action invoke) — fire from the
            // timer thread directly so subscribers see the event without the UI marshal.
            TryRaiseMutexBusEvent("Error");
        };
    }

    private void ToggleMutex(IMutexHolder mutex, ITrayService tray)
    {
        if (mutex.IsHeld)
        {
            mutex.Release();
            tray.UpdateStatus(MultiInstanceState.Off);
            TryRaiseMutexBusEvent("Off");
        }
        else
        {
            var acquired = mutex.Acquire();
            tray.UpdateStatus(acquired ? MultiInstanceState.On : MultiInstanceState.Error);
            TryRaiseMutexBusEvent(acquired ? "On" : "Error");
        }
    }

    /// <summary>How long Retry keeps re-attempting the singleton name before giving up.</summary>
    private static readonly TimeSpan RecoveryWindow = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Seamless takeover: when Roblox holds the singleton Event but ONLY via windowless tray
    /// clients, close them, reclaim the name, and put a tray client back — no modal. Returns true
    /// when RoRoRo now owns the Event; false means the caller should fall through to the BLOCKED
    /// modal (an in-game client is present, or the reclaim failed).
    ///
    /// <para>The windowless-only gate (<see cref="SeamlessTakeover.WindowlessOnly"/>) is the whole
    /// safety story: a windowed client may be mid-game, so it is never closed without the confirming
    /// modal. A tray client has nothing to lose, so closing it silently is safe — and it is exactly
    /// what makes RoRoRo coexist with Roblox-on-startup instead of blocking on every launch.</para>
    /// </summary>
    private async Task<bool> TrySeamlessTakeoverAsync()
    {
        if (_services is null) return false;

        IReadOnlyList<RobloxProcessInfo> players;
        try
        {
            players = _services.GetRequiredService<IRobloxRunningProbe>().GetRunningPlayers();
        }
        catch (Exception ex)
        {
            // Can't tell windowed from windowless — be conservative and let the modal handle it.
            _log?.LogDebug(ex, "Seamless takeover: player scan threw; deferring to the modal.");
            return false;
        }

        if (!SeamlessTakeover.WindowlessOnly(players))
        {
            return false; // in-game client (or nothing) — not eligible for a silent close
        }

        _log?.LogInformation(
            "Seamless takeover: {Count} windowless tray Roblox client(s) hold the Event; closing and reclaiming.",
            players.Count);

        try { _services.GetRequiredService<IRobloxInstanceStopper>().StopAll(); }
        catch (Exception ex) { _log?.LogWarning(ex, "Seamless takeover: StopAll failed; still attempting to acquire."); }

        var mutex = _services.GetRequiredService<IMutexHolder>();
        var outcome = await mutex
            .TryAcquireWithRetryAsync(RecoveryWindow, RecoveryInterval)
            .ConfigureAwait(true);

        if (outcome != MutexAcquireOutcome.Acquired)
        {
            _log?.LogInformation(
                "Seamless takeover: could not reclaim the name after closing tray clients ({Outcome}); deferring to the modal.",
                outcome);
            return false;
        }

        // Preserve the user's Roblox-at-startup intent: put a tray client back. It finds our Event,
        // fails CreateEvent, and runs to tray in multi-instance mode alongside us. Best-effort — a
        // failure here does not undo the takeover.
        try
        {
            if (_services.GetRequiredService<IRobloxTrayLauncher>().RelaunchToTray())
            {
                _log?.LogInformation("Seamless takeover: relaunched Roblox to tray alongside RoRoRo.");
            }
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Seamless takeover: tray relaunch failed; RoRoRo holds the Event regardless.");
        }

        var tray = _services.GetRequiredService<ITrayService>();
        tray.UpdateStatus(MultiInstanceState.On);
        TryRaiseMutexBusEvent("On");
        return true;
    }

    /// <summary>Shared multi-instance recovery: optionally close all Roblox first (with the
    /// unsaved-state confirm when windowed clients exist), then re-attempt the singleton name.
    /// Returns whether RoRoRo may proceed with multi-instance working. Used by the BLOCKED startup
    /// modal and the runtime banner.
    ///
    /// <para><b>Why this polls.</b> The named kernel object dies only when its LAST handle closes,
    /// and a just-quit (or just-killed) RobloxPlayerBeta takes a beat to tear down. The old code
    /// made one instantaneous attempt, which is why "quit Roblox from the tray, then hit Retry"
    /// so often failed the first press and worked the second. A bounded poll absorbs the lag.</para>
    ///
    /// <para>Returns true for <see cref="MutexAcquireOutcome.HeldByCompatibleTool"/> too: we don't
    /// own the handle, but Roblox lost its singleton to that tool, so multi-instance works and
    /// there is nothing left to recover.</para>
    /// </summary>
    internal async Task<bool> TryRecoverMultiInstanceAsync(bool closeRobloxFirst)
    {
        if (_services is null) return false;
        var mutex = _services.GetRequiredService<IMutexHolder>();
        if (mutex.IsHeld) return true;

        if (closeRobloxFirst)
        {
            var probe = _services.GetRequiredService<IRobloxRunningProbe>();
            var players = probe.GetRunningPlayers();
            var windowed = players.Count(p => p.HasWindow);
            if (windowed > 0)
            {
                // Live game windows among the processes — confirm before killing.
                var confirm = new Modals.StopAllConfirmWindow(players.Count);
                if (confirm.ShowDialog() != true) return mutex.IsHeld; // user cancelled
            }
            try { _services.GetRequiredService<IRobloxInstanceStopper>().StopAll(); }
            catch (Exception ex) { _log?.LogWarning(ex, "Close-for-me StopAll failed; retrying acquire anyway."); }
        }

        var outcome = await mutex
            .TryAcquireWithRetryAsync(RecoveryWindow, RecoveryInterval)
            .ConfigureAwait(true); // continue on the UI thread — tray + modal touch it below

        var state = outcome switch
        {
            MutexAcquireOutcome.Acquired => MultiInstanceState.On,
            // A peer tool holds the name: multi-instance works, we just don't own the handle.
            // "Off" (not "Error") is the honest tray state — same as a Start-anyway launch.
            MutexAcquireOutcome.HeldByCompatibleTool => MultiInstanceState.Off,
            _ => MultiInstanceState.Error,
        };

        var tray = _services.GetRequiredService<ITrayService>();
        tray.UpdateStatus(state);
        TryRaiseMutexBusEvent(state.ToString());

        _log?.LogInformation("Multi-instance recovery finished: {Outcome} (closeRobloxFirst={CloseFirst}).",
            outcome, closeRobloxFirst);

        return outcome is MutexAcquireOutcome.Acquired or MutexAcquireOutcome.HeldByCompatibleTool;
    }

    /// <summary>LEFTOVER "Clean up + continue": stop leftover clients, with the unsaved-state
    /// confirm only when windowed clients exist. The mutex is already held here.</summary>
    private void CleanUpLeftoverRoblox(bool hasWindowedClients)
    {
        if (_services is null) return;
        try
        {
            if (hasWindowedClients)
            {
                var count = _services.GetRequiredService<IRobloxRunningProbe>().GetRunningPlayerPids().Count;
                var confirm = new Modals.StopAllConfirmWindow(count);
                if (confirm.ShowDialog() != true) return;
            }
            _services.GetRequiredService<IRobloxInstanceStopper>().StopAll();
        }
        catch (Exception ex) { _log?.LogWarning(ex, "Leftover clean-up failed."); }
    }

    /// <summary>
    /// LEFTOVER modal's "Clear strays" action: stops only the windowless orphans and leaves any
    /// windowed clients running untouched — no confirmation, because a windowless orphan has
    /// nothing to lose (same reasoning as <see cref="SeamlessTakeover.WindowlessOnly"/>).
    /// </summary>
    private void ClearLeftoverStrays()
    {
        if (_services is null) return;
        try
        {
            var stopped = _services.GetRequiredService<IRobloxInstanceStopper>().StopWindowless();
            _log?.LogInformation("Leftover clean-up: cleared {Stopped} windowless stray Roblox process(es).", stopped);
        }
        catch (Exception ex) { _log?.LogWarning(ex, "Leftover stray clean-up failed."); }
    }

    /// <summary>
    /// Wires the runtime contested-mutex watcher (Task 4/8): when Roblox grabs the lock while
    /// RoRoRo is running (tray-resident), <see cref="MutexContestedWatcher.ContestedChanged"/>
    /// flips the banner on; the banner's two actions re-enter the same
    /// <see cref="TryRecoverMultiInstance"/> recovery path the startup BLOCKED modal uses, and
    /// clear the banner on success. The watcher's own timer thread marshals to the UI thread via
    /// Dispatcher; VM event handlers already run on the UI thread (RelayCommand.Execute), so no
    /// further marshaling is needed there.
    /// </summary>
    private void WireContestedWatcher(MainWindow mainWindow)
    {
        if (_services is null) return;
        var watcher = _services.GetRequiredService<MutexContestedWatcher>();
        var vm = _services.GetRequiredService<MainViewModel>();

        watcher.ContestedChanged += (_, contested) =>
            Dispatcher.Invoke(() => vm.SetContested(contested));

        // async void by necessity — these are fire-and-forget VM events raised on the UI thread.
        // TryRecoverMultiInstanceAsync polls for up to RecoveryWindow, so awaiting keeps the banner
        // responsive instead of freezing the window while Roblox's handles drain.
        vm.RequestCloseRobloxForMe += async () =>
        {
            if (await TryRecoverMultiInstanceAsync(closeRobloxFirst: true)) vm.SetContested(false);
        };
        vm.RequestRetryMutex += async () =>
        {
            if (await TryRecoverMultiInstanceAsync(closeRobloxFirst: false)) vm.SetContested(false);
        };

        watcher.Start();
    }

    private void StopAllInstances()
    {
        if (_services is null) return;
        try
        {
            var running = _services.GetRequiredService<IRobloxRunningProbe>().GetRunningPlayerPids().Count;
            if (running == 0)
            {
                _log?.LogInformation("Stop-all requested from tray: no Roblox instances running.");
                return;
            }

            var confirm = new Modals.StopAllConfirmWindow(running);
            if (confirm.ShowDialog() == true)
            {
                var stopped = _services.GetRequiredService<IRobloxInstanceStopper>().StopAll();
                _log?.LogInformation("Stop-all from tray: stopped {Stopped} of {Running} instance(s).", stopped, running);
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Stop-all from tray failed.");
        }
    }

    private void TryRaiseMutexBusEvent(string state)
    {
        if (_services is null) return;
        try
        {
            var bus = _services.GetService<ROROROblox.App.Plugins.IPluginEventBus>()
                as ROROROblox.App.Plugins.InProcessPluginEventBus;
            bus?.RaiseMutexStateChanged(state);
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Plugin event bus mutex-state raise failed; ignoring.");
        }
    }

    /// <summary>
    /// Bridges existing App-layer events into the plugin event bus so subscribed plugins
    /// see them via the SubscribeAccountLaunched / SubscribeAccountExited /
    /// SubscribeMutexStateChanged / SubscribeMemoryPressure gRPC streams. Wrapped in
    /// try/catch — a wiring failure must not block App startup.
    /// </summary>
    private void WirePluginEventBus()
    {
        if (_services is null) return;
        try
        {
            var tracker = _services.GetRequiredService<IRobloxProcessTracker>();
            var vm = _services.GetRequiredService<MainViewModel>();
            var bus = _services.GetRequiredService<ROROROblox.App.Plugins.IPluginEventBus>()
                as ROROROblox.App.Plugins.InProcessPluginEventBus;
            if (bus is null) return;

            // Memory pressure (Task 10): forward every tracked account on the snapshot the
            // watchdog fires with, not just the target -- WireMemoryWarningTray (separate
            // subscriber, untouched by this change) only cares about the fattest client for
            // the tray balloon, but a plugin driving an automated recycle-and-macro-back flow
            // needs the full per-account picture (over_cap / is_target / read_ok) to decide
            // what to act on.
            var watchdog = _services.GetRequiredService<IMemoryWatchdog>();
            watchdog.PressureCrossed += (_, snap) =>
            {
                try
                {
                    foreach (var account in snap.Accounts)
                    {
                        bus.RaiseMemoryPressure(account);
                    }
                }
                catch (Exception ex)
                {
                    _log?.LogDebug(ex, "Plugin bus MemoryPressure bridge threw; ignoring.");
                }
            };

            tracker.ProcessAttached += (_, e) =>
            {
                try
                {
                    // AccountsSnapshot: tracker events fire on threadpool continuations.
                    var summary = vm.AccountsSnapshot.FirstOrDefault(a => a.Id == e.AccountId);
                    if (summary is null) return;
                    var snapshot = new ROROROblox.App.Plugins.RunningAccountSnapshot(
                        AccountId: summary.Id.ToString(),
                        RobloxUserId: summary.RobloxUserId ?? 0,
                        DisplayName: summary.RenderName,
                        ProcessId: e.Pid,
                        PlaceId: summary.CurrentPlaceId ?? 0,
                        PlaceName: summary.CurrentGameName ?? string.Empty);
                    bus.RaiseAccountLaunched(snapshot);
                }
                catch (Exception ex)
                {
                    _log?.LogDebug(ex, "Plugin bus AccountLaunched bridge threw; ignoring.");
                }
            };

            tracker.ProcessExited += (_, e) =>
            {
                try
                {
                    // AccountsSnapshot: tracker events fire on threadpool continuations.
                    var summary = vm.AccountsSnapshot.FirstOrDefault(a => a.Id == e.AccountId);
                    var snapshot = new ROROROblox.App.Plugins.RunningAccountSnapshot(
                        AccountId: e.AccountId.ToString(),
                        RobloxUserId: summary?.RobloxUserId ?? 0,
                        DisplayName: summary?.RenderName ?? string.Empty,
                        ProcessId: e.Pid);
                    bus.RaiseAccountExited(snapshot, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                }
                catch (Exception ex)
                {
                    _log?.LogDebug(ex, "Plugin bus AccountExited bridge threw; ignoring.");
                }
            };
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "WirePluginEventBus failed; plugins will not see runtime events.");
        }
    }

    /// <summary>
    /// Binds the gRPC plugin host (Kestrel + named pipe). Runs BEFORE the startup gate's
    /// modals, deliberately.
    ///
    /// <para>The gate's modals are shown with <c>ShowDialog()</c>, which pumps a nested message
    /// loop and blocks the rest of OnStartup until the user answers. When leftover Roblox
    /// processes exist the leftover modal is always up, so binding the pipe after the gate meant
    /// the pipe never bound until a human clicked a button. That is exactly the state an internet
    /// outage leaves behind — dead clients still running — which is precisely when an agent needs
    /// to reach RoRoRo to clear and relaunch them. Binding first makes the host reachable while
    /// the dialog waits.</para>
    ///
    /// <para>Autostart is NOT kicked here. See <see cref="StartPluginAutostart"/> — no plugin
    /// process may launch until the user has answered the gate.</para>
    ///
    /// <para>The plugin host failing must NOT break RoRoRo's normal launch — a fully-broken
    /// plugin host still leaves RoRoRo launching, without plugin support.</para>
    /// </summary>
    private void StartPluginHostListener()
    {
        if (_services is null) return;
        try
        {
            var pluginHost = _services.GetRequiredService<ROROROblox.App.Plugins.PluginHostStartupService>();
            // Fire-and-forget: StartAsync's first await yields after Kestrel's bind, so
            // OnStartup doesn't block on it. The continuation observes a faulted task so a
            // bind failure surfaces as a debug log rather than an unobserved exception.
            _pluginHostListening = pluginHost.StartAsync(CancellationToken.None);
            _ = _pluginHostListening.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    _log?.LogDebug(t.Exception, "PluginHostStartupService.StartAsync threw; plugins disabled this session.");
                }
            }, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            _pluginHostListening = null;
            _log?.LogDebug(ex, "Resolving PluginHostStartupService threw; plugins disabled this session.");
        }
    }

    /// <summary>
    /// Launches autostart-enabled plugin processes, once the startup gate has been answered.
    ///
    /// <para>Split from <see cref="StartPluginHostListener"/> so the pipe can bind while a gate
    /// modal is still up without any plugin acting on the user's machine first. A leftover-processes
    /// modal that the user answers with "Clean up + continue" stops Roblox clients — a keep-alive
    /// or macro plugin racing that teardown is not a state worth allowing.</para>
    ///
    /// <para>Ordering still matters within the plugin path: plugin processes handshake immediately
    /// on launch, so the gRPC server has to be listening first or that first call fails with
    /// "pipe not found". Hence the continuation off the bind task rather than a bare call.</para>
    /// </summary>
    private void StartPluginAutostart()
    {
        if (_pluginHostListening is null) return; // never bound; nothing to handshake against
        _ = _pluginHostListening.ContinueWith(t =>
        {
            if (t.IsFaulted) return; // already logged by the bind continuation
            _ = Task.Run(StartPluginAutostartAsync);
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Scans the plugin registry and launches every plugin whose consent record has
    /// AutostartEnabled=true. Runs on the threadpool, well after MainWindow.Show — a
    /// broken plugin or registry must NOT break RoRoRo's normal startup.
    /// </summary>
    private async Task StartPluginAutostartAsync()
    {
        if (_services is null) return;
        try
        {
            var registry = _services.GetRequiredService<ROROROblox.App.Plugins.PluginRegistry>();
            var supervisor = _services.GetRequiredService<ROROROblox.App.Plugins.PluginProcessSupervisor>();
            var plugins = await registry.ScanAsync().ConfigureAwait(false);
            var autostartCount = plugins.Count(p => p.Consent.AutostartEnabled);
            _log?.LogDebug(
                "Plugin autostart: registry scan found {Count} plugin(s), {AutostartCount} autostart-enabled.",
                plugins.Count, autostartCount);
            supervisor.StartAutostart(plugins);
            if (autostartCount > 0)
            {
                _log?.LogInformation("Started {Count} plugin process(es) on autostart.", autostartCount);
            }
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Plugin autostart threw; plugins not launched this session.");
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

        // Stop the plugin host BEFORE disposing the service provider. Kestrel needs the
        // logger / DI graph alive to drain in-flight calls cleanly. Wrap defensively —
        // a hung StopAsync must not block the user's exit path.
        //
        // Run on the threadpool (not the WPF UI sync context) and bound by a hard timeout —
        // some Kestrel/Hosting continuations capture the calling DispatcherSynchronizationContext,
        // and a blocking GetResult() on the UI thread deadlocks (same pattern the comment on
        // ApplyAtStartup above already calls out). On timeout we abandon the stop and let the
        // process exit reclaim the pipe handle; OnExit must always finish promptly.
        if (_services is not null)
        {
            // Stop plugin PROCESSES before the host they speak to. Nothing used to: the supervisor
            // is not IDisposable and no shutdown path called StopAll(), so whether an autostarted
            // plugin outlived RoRoRo depended entirely on the plugin. A well-behaved one notices
            // the pipe drop and exits on its own (626labs.ur-task self-exits in ~0.2s). One that
            // doesn't watch the pipe simply lingers — which is why StopByInstallDirAsync has to
            // sweep for "a plugin process that outlived the RoRoRo session that launched it"
            // before it can wipe an install dir.
            //
            // Teardown shouldn't be contingent on third-party goodwill. StopAll kills the tracked
            // process trees (the starter passes entireProcessTree). There is no graceful path to
            // take first: Plugin.OnShutdown exists in the contract but nothing has ever called it —
            // no per-plugin gRPC client is wired. When that lands, it belongs here, ahead of the kill.
            //
            // Ordering: plugins first, then Kestrel. Killing them first means their open streams
            // are gone before the host drains, rather than the host yanking the pipe out from
            // under processes that are about to die anyway.
            try
            {
                _services.GetService<ROROROblox.App.Plugins.PluginProcessSupervisor>()?.StopAll();
            }
            catch (Exception ex)
            {
                _log?.LogDebug(ex, "PluginProcessSupervisor.StopAll threw on exit; plugin processes may be orphaned.");
            }

            try
            {
                var pluginHost = _services.GetService<ROROROblox.App.Plugins.PluginHostStartupService>();
                if (pluginHost is not null)
                {
                    var stopped = Task.Run(() => pluginHost.StopAsync(CancellationToken.None))
                        .Wait(TimeSpan.FromSeconds(2));
                    if (!stopped)
                    {
                        _log?.LogDebug("PluginHostStartupService.StopAsync did not complete within 2s; abandoning.");
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.LogDebug(ex, "PluginHostStartupService.StopAsync threw on exit; ignoring.");
            }

            // Stop the presence poll loop before the provider disposes (mirrors the process
            // tracker / plugin-host shutdown shape). _services.Dispose() also disposes it
            // (PresenceService : IDisposable), but stopping first halts the timer cleanly.
            try
            {
                var presence = _services.GetService<IPresenceService>();
                presence?.Stop();
            }
            catch (Exception ex)
            {
                _log?.LogDebug(ex, "PresenceService.Stop threw on exit; ignoring.");
            }

            // Stop the activity-monitor sample timer before the provider disposes (same shape
            // as the presence poller above). _services.Dispose() also disposes it
            // (ActivityMonitor : IDisposable), but stopping first halts the timer cleanly.
            try
            {
                var monitor = _services.GetService<IActivityMonitor>();
                monitor?.Stop();
            }
            catch (Exception ex)
            {
                _log?.LogDebug(ex, "ActivityMonitor.Stop threw on exit; ignoring.");
            }

            // Stop the runtime contested-mutex watcher's poll timer (Task 8) before the provider
            // disposes. _services.Dispose() also disposes it (MutexContestedWatcher : IDisposable),
            // but stopping first halts the timer cleanly, same shape as the two stops above.
            try
            {
                _services.GetService<MutexContestedWatcher>()?.Dispose();
            }
            catch (Exception ex)
            {
                _log?.LogDebug(ex, "MutexContestedWatcher.Dispose threw on exit; ignoring.");
            }

            // Discord presence (Task 9) — not DI-registered (WireDiscordPresenceAsync constructs
            // it conditionally, skipped whenever Discord:ApplicationId is empty), so
            // _services.DisposeAsync() below never reaches it; this is its only cleanup call.
            // Dispose() tears down the Lachee IPC client, which is what actually makes Discord
            // clear the presence on exit rather than leaving a stale status showing until Discord
            // notices the pipe died on its own.
            try
            {
                _services.GetService<MainViewModel>()?.DiscordPresence?.Dispose();
            }
            catch (Exception ex)
            {
                _log?.LogDebug(ex, "DiscordPresenceService.Dispose threw on exit; ignoring.");
            }
        }

        _singleInstance?.Dispose();

        // The container holds IAsyncDisposable-only singletons (PluginHostStartupService),
        // and a sync Dispose() throws InvalidOperationException for those — the repeated
        // "type only implements IAsyncDisposable" lines in the shutdown log. DisposeAsync
        // off the dispatcher with a bounded wait, same shape as the StopAsync call above.
        if (_services is not null)
        {
            try
            {
                var disposed = Task.Run(() => _services.DisposeAsync().AsTask())
                    .Wait(TimeSpan.FromSeconds(2));
                if (!disposed)
                {
                    _log?.LogDebug("ServiceProvider.DisposeAsync did not complete within 2s; abandoning.");
                }
            }
            catch (Exception ex)
            {
                _log?.LogDebug(ex, "ServiceProvider.DisposeAsync threw on exit; ignoring.");
            }
        }

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

        // Fourth net: exceptions RelayCommand.Execute swallows. These never reach the
        // dispatcher handler (Execute's catch eats them first), so without this hook a
        // command body's unguarded await fails with zero trace.
        ViewModels.RelayCommand.OnExceptionSwallowed = ex =>
            _log?.LogWarning(ex, "Exception escaped a command handler (swallowed by RelayCommand).");
    }
}
