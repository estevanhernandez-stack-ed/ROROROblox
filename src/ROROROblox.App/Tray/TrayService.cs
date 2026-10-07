using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Localization;
using ROROROblox.Core;
using ROROROblox.Core.Discord;
using ROROROblox.Core.StreamerMode;

namespace ROROROblox.App.Tray;

/// <summary>
/// System-tray surface backed by Hardcodet's <see cref="TaskbarIcon"/>. Spec §5.2.
/// Doesn't own the mutex — fires <see cref="RequestToggleMutex"/> and lets the composition
/// root wire that to <see cref="IMutexHolder"/>. Icon swaps between cyan (ON) / grey (OFF) /
/// magenta (Error), with a warn overlay for memory pressure. The icon set is final art: the
/// on/off/error ICOs and title-bar PNGs are rendered by scripts/generate-tray-icons.ps1 (last
/// regenerated 2026-07-08, commit c2a1454) and the tray-warn pair was added by hand through the
/// design pass on 2026-08-01 (commit 74271fa). This line said "placeholder icons" until 2026-08-30.
/// </summary>
internal sealed class TrayService : ITrayService
{
    private const string IconResourceBase = "/ROROROblox.App;component/Tray/Resources/";

    private readonly IStreamerIdentityProvider _streamerIdentity;
    private readonly ILogger<TrayService> _log;

    // The noise one notification makes (v1.33 item 5). HERE rather than in AlertDispatcher because
    // the dispatcher's fan-out loop runs once per destination and this runs once per balloon: a
    // sound played up there would chime three times for one crossing on a roster routed to the
    // desktop, the clan channel and a phone. One event is one sound.
    private readonly IAlertSoundPlayer _sound;
    private readonly TaskbarIcon _taskbarIcon;
    private readonly MenuItem _toggleItem;
    private readonly MenuItem _streamerModeItem;

    // When the avatar painter sets these, UpdateStatus uses them in place of the resource ICOs.
    // Per-state so the cyan/grey/magenta ring still reflects mutex status.
    private Icon? _customOn;
    private Icon? _customOff;
    private Icon? _customError;
    private MultiInstanceState _currentState = MultiInstanceState.Off;
    private bool _disposed;

    // Memory-warning overlay (Task 8) — deliberately independent of _currentState/UpdateStatus.
    //
    // _lastMemoryWarningAccountId used to sit beside this: the account a memory balloon was about,
    // remembered here because Windows' TrayBalloonTipClicked carries no payload of its own. It went
    // with ShowMemoryWarning and the shell balloon on 2026-10-06 (v1.33 item 4). The account now
    // arrives WITH the notification (ShowToast's trailing id) and rides on the drawn balloon itself,
    // so there is no app-lifetime "last balloon" state to go stale and no reason a click on one
    // notification could ever replay another's account.
    private bool _memoryWarningActive;

    public event EventHandler? RequestOpenMainWindow;
    public event EventHandler? RequestToggleMutex;
    public event EventHandler? RequestStopAllInstances;
    public event EventHandler? RequestQuit;
    public event EventHandler? RequestOpenDiagnostics;
    public event EventHandler? RequestOpenLogs;
    public event EventHandler? RequestOpenPreferences;
    public event EventHandler? RequestActivateMain;
    public event EventHandler? RequestOpenHistory;
    public event EventHandler? RequestOpenPlugins;
    public event EventHandler<Guid>? RequestFocusAccount;
    public event EventHandler<MultiInstanceState>? StatusChanged;

    public TrayService(
        IStreamerIdentityProvider streamerIdentity,
        ILogger<TrayService>? log = null,
        IAlertSoundPlayer? sound = null)
    {
        _streamerIdentity = streamerIdentity;
        _log = log ?? NullLogger<TrayService>.Instance;
        // Defaults to the shipped chime rather than to a no-op. A composition root that forgot to
        // pass one would otherwise produce a build where alerts are silent and nothing says so —
        // the failure this whole item exists to stop being possible.
        _sound = sound ?? new AlertSoundPlayer(() => AlertSoundSetting.Default);
        _taskbarIcon = new TaskbarIcon();
        // Double-click is the user's "do the thing" gesture — App.xaml.cs decides whether
        // that means "launch main" or "surface the window" based on whether a main is set.
        _taskbarIcon.TrayMouseDoubleClick += (_, _) => RequestActivateMain?.Invoke(this, EventArgs.Empty);

        // NO TrayBalloonTipClicked SUBSCRIPTION, and its absence is deliberate (v1.33 item 4).
        // That event belongs to the SHELL balloon, which this class no longer shows — ShowToast
        // draws its own (AlertBalloon, via ShowCustomBalloon), so the shell never has a balloon of
        // ours to be clicked and the event can never fire. Click-to-focus is wired per balloon in
        // ShowToast instead, from the control that was actually clicked, which is also why it no
        // longer needs a remembered "last account" field.
        var (toggle, streamerMode, menu) = BuildContextMenu();
        _toggleItem = toggle;
        _streamerModeItem = streamerMode;
        _taskbarIcon.ContextMenu = menu;

        // Streamer mode (v1.10) can also be flipped from the Settings checkbox or the plugin
        // host — keep the tray checkmark in lockstep regardless of which surface toggled it.
        _streamerIdentity.Changed += OnStreamerModeChanged;

        UpdateStatus(MultiInstanceState.Off);
    }

    public void Show()
    {
        _taskbarIcon.Visibility = Visibility.Visible;
    }

    public void UpdateStatus(MultiInstanceState state)
    {
        _currentState = state;
        _taskbarIcon.Icon = ResolveIconForState(state);
        // Wording lives in Core (F-034). The tooltip shipped the REPO name in all three states
        // while the menu item two pixels away did not, because they were two hand-written switches
        // that nobody read side by side. There is one now, and MainWindow's footer reads from it too.
        _taskbarIcon.ToolTipText = MultiInstanceStatusLine.Tooltip(state);
        _toggleItem.Header = MultiInstanceStatusLine.MenuHeader(state);
        // Error is a one-click reload (re-acquire), not a dead end: on MutexLost the handle is
        // released (IsHeld == false), so the toggle's Acquire path re-acquires in place — no app
        // restart needed. Keep it enabled so the user can recover from the tray.
        _toggleItem.IsEnabled = true;

        // LAST, deliberately. A subscriber that reads back off the tray sees the state already
        // applied rather than the one being replaced. Raised unconditionally, including when the
        // state did not change, because a caller re-asserting ON after a recovery attempt is
        // information — it means the re-acquire worked.
        StatusChanged?.Invoke(this, state);
    }

    /// <summary>
    /// Replace the default per-state ICOs with main-account-avatar-driven ones. Pass <c>null</c>
    /// for any (or all) to revert to the bundled defaults for that state. Old icons are disposed
    /// here so callers don't have to.
    /// </summary>
    public void SetCustomStateIcons(Icon? on, Icon? off, Icon? error)
    {
        // Dispose the previous customs we owned. Don't dispose the inputs — caller transfers
        // ownership when calling.
        _customOn?.Dispose();
        _customOff?.Dispose();
        _customError?.Dispose();

        _customOn = on;
        _customOff = off;
        _customError = error;

        // Refresh the live icon to reflect the new set.
        _taskbarIcon.Icon = ResolveIconForState(_currentState);
    }

    /// <summary>
    /// Toggle the memory-pressure warning badge (Task 8). Independent of <see cref="UpdateStatus"/> —
    /// see the doc on <see cref="ITrayService.SetMemoryWarning"/> for why the two must never merge.
    /// <para>
    /// <b>Thread-safety:</b> <c>IMemoryWatchdog.PressureCrossed</c> — the event this method
    /// is wired to (App.xaml.cs's <c>WireMemoryWarningTray</c>) — fires from
    /// <c>MemoryWatchdog.Sample()</c>, which runs on the watchdog's own <see cref="System.Threading.Timer"/>
    /// callback, NOT the UI thread. <c>_taskbarIcon.Icon</c> is a WPF-hosted property, so this
    /// marshals via <c>Application.Current.Dispatcher.Invoke</c> the same way
    /// <see cref="OnStreamerModeChanged"/> already does for its own cross-thread caller.
    /// </para>
    /// </summary>
    public void SetMemoryWarning(bool active)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_disposed) return;
            if (_memoryWarningActive == active) return;
            _memoryWarningActive = active;
            _taskbarIcon.Icon = ResolveIconForState(_currentState);
        });
    }

    private Icon ResolveIconForState(MultiInstanceState state)
    {
        // Mutex ERROR is the more urgent failure and always wins the tray slot — a memory
        // warning must never erase the ON/ERROR state the user needs during a real mutex problem.
        if (_memoryWarningActive && state != MultiInstanceState.Error)
        {
            try
            {
                return LoadIcon(WarnIconFilename);
            }
            catch (Exception ex)
            {
                // tray-warn.ico IS in the tree (a csproj Resource since 2026-08-01, commit 74271fa;
                // register row F-124 retired the comment that said otherwise). This catch is for a
                // corrupt or unloadable resource: degrade to whatever's currently showing rather
                // than crash the one code path that only runs when a user is already in trouble.
                _log.LogWarning(ex, "Memory-warning tray icon unavailable; keeping the current icon.");
                return _taskbarIcon.Icon ?? LoadIcon(StateIconFilename(state));
            }
        }

        var custom = state switch
        {
            MultiInstanceState.On => _customOn,
            MultiInstanceState.Error => _customError,
            _ => _customOff,
        };
        return custom ?? LoadIcon(StateIconFilename(state));
    }

    private (MenuItem toggle, MenuItem streamerMode, ContextMenu menu) BuildContextMenu()
    {
        var menu = new ContextMenu();

        var toggle = new MenuItem { Header = MultiInstanceStatusLine.MenuHeader(MultiInstanceState.Off) };
        toggle.Click += (_, _) => RequestToggleMutex?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(toggle);

        // Streamer mode (v1.10) — fake-name/avatar substitution for on-stream safety. Checkable
        // so the tray reflects state at a glance; Click reads the CURRENT provider state (not the
        // checkbox's own auto-toggled IsChecked) to decide the new value, then OnStreamerModeChanged
        // resyncs IsChecked once the provider's Changed event confirms the flip landed.
        var streamerMode = new MenuItem { Header = Loc.Get("Tray_StreamerMode"), IsCheckable = true };

        // F-102. BOUND, not clicked. MenuItemAutomationPeer.Toggle() raises no Click at all —
        // measured, not assumed, in TogglePatternReachesTheHandlerTests — so the previous handler
        // never ran for an assistive technology or automation caller, and streamer mode silently
        // stayed off while the tick appeared. Same defect the Settings checkbox had, found by
        // asking the question F-102 said to ask.
        streamerMode.SetBinding(
            MenuItem.IsCheckedProperty,
            new System.Windows.Data.Binding(nameof(StreamerModeFlag.On))
            {
                Source = new StreamerModeFlag(_streamerIdentity),
                Mode = System.Windows.Data.BindingMode.TwoWay,
            });
        menu.Items.Add(streamerMode);

        var stopAll = new MenuItem { Header = Loc.Get("Tray_StopAll") };
        stopAll.Click += (_, _) => RequestStopAllInstances?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(stopAll);

        menu.Items.Add(new Separator());

        // F-034. The repo name is ROROROblox; the product is RoRoRo. This is the entry a
        // tray-resident app shows more often than any other surface it has.
        var open = new MenuItem { Header = Loc.Format("Tray_Open", Branding.ProductName) };
        open.Click += (_, _) => RequestOpenMainWindow?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(open);

        menu.Items.Add(new Separator());

        var preferences = new MenuItem { Header = Loc.Get("Tray_Settings") };
        preferences.Click += (_, _) => RequestOpenPreferences?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(preferences);

        var history = new MenuItem { Header = Loc.Get("Tray_History") };
        history.Click += (_, _) => RequestOpenHistory?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(history);

        var diagnostics = new MenuItem { Header = Loc.Get("Tray_Diagnostics") };
        diagnostics.Click += (_, _) => RequestOpenDiagnostics?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(diagnostics);

        var plugins = new MenuItem { Header = Loc.Get("Tray_Plugins") };
        plugins.Click += (_, _) => RequestOpenPlugins?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(plugins);

        var logs = new MenuItem { Header = Loc.Get("Tray_OpenLogs") };
        logs.Click += (_, _) => RequestOpenLogs?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(logs);

        menu.Items.Add(new Separator());

        var quit = new MenuItem { Header = Loc.Get("Tray_Quit") };
        quit.Click += (_, _) => RequestQuit?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(quit);

        return (toggle, streamerMode, menu);
    }

    /// <summary>
    /// Keeps the tray checkmark in sync when streamer mode flips from a surface other than this
    /// menu item (the Settings checkbox, a plugin, or this same click landing asynchronously).
    /// The provider's <c>Changed</c> event can fire off the UI thread (its <c>SetActiveAsync</c>
    /// awaits a settings write with <c>ConfigureAwait(false)</c>), and <see cref="MenuItem"/> is a
    /// WPF DependencyObject — direct property writes from a non-UI thread throw, so this marshals
    /// via the dispatcher (same pattern as <c>tray.UpdateStatus</c> callers elsewhere in App.xaml.cs).
    /// </summary>
    private void OnStreamerModeChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        Application.Current?.Dispatcher.Invoke(() => _streamerModeItem.IsChecked = _streamerIdentity.IsActive);
    }

    // The memory-warning ring. Shipped: the asset exists at Tray/Resources/tray-warn.ico and the
    // csproj <Resource> line includes it. (F-124: this comment previously described the asset as
    // not-yet-added and warned of a runtime throw — a landmine that had already been defused. The
    // logs show 29 cap-crossings with no exception; a comment describing a hazard that is not
    // there trains readers to skip the comments that describe ones that are.)
    private const string WarnIconFilename = "tray-warn.ico";

    private static string StateIconFilename(MultiInstanceState state) => state switch
    {
        MultiInstanceState.On => "tray-on.ico",
        MultiInstanceState.Error => "tray-error.ico",
        _ => "tray-off.ico",
    };

    private static Icon LoadIcon(string filename)
    {
        var resource = Application.GetResourceStream(new Uri(IconResourceBase + filename, UriKind.Relative))
            ?? throw new InvalidOperationException($"Tray icon resource not found: {filename}");
        using var stream = resource.Stream;
        return new Icon(stream);
    }

    /// <summary>
    /// The notification — every alert kind routed to
    /// <see cref="ROROROblox.Core.Discord.AlertDestination.Local"/> arrives here.
    /// <para>
    /// <b>DRAWN, NOT THE SHELL'S, since 2026-10-06 (v1.33 item 4).</b>
    /// <c>ShowBalloonTip</c> is the Windows balloon and it ALWAYS plays the system notification
    /// sound: <c>BalloonFlags.NoSound</c> exists in the shipped Hardcodet.NotifyIcon.Wpf 2.0.1 but
    /// only behind a non-public overload (verified by reflection 2026-10-05), so there is no
    /// supported way to ask for a quiet one. <see cref="AlertBalloon"/> through
    /// <c>ShowCustomBalloon</c> draws no OS chrome and therefore triggers no OS sound, which is what
    /// makes the sound a setting instead of Windows' decision. What that costs: no Action Center
    /// entry — the shell balloon had none either — and Focus Assist is no longer consulted by
    /// anyone. Both are PRD non-goals.
    /// </para>
    /// <para>
    /// <b>And this is where the sound is played</b> (v1.33 item 5), through
    /// <see cref="IAlertSoundPlayer"/> and after the balloon is up. One event is one sound: this
    /// method runs once per notification, while <c>AlertDispatcher</c>'s fan-out loop that calls it
    /// runs once per destination. The user's choice between the bundled chime, Windows' asterisk
    /// and nothing is read on every play, so changing it takes effect on the next alert.
    /// </para>
    /// <para>
    /// <b>Click-to-focus rides on the balloon.</b> <paramref name="accountId"/> is the single
    /// account the group resolved to, or null when it covered several;
    /// <see cref="AlertBalloon.FocusRequested"/> replays it on <see cref="RequestFocusAccount"/>.
    /// Per balloon, deliberately: the old path kept the account in a field on this class and the
    /// shell's payload-free <c>TrayBalloonTipClicked</c> replayed whatever it last held, which had
    /// to be cleared by every unrelated notification — and that clearing is what made item 3's
    /// collapse break the feature with no test going red.
    /// </para>
    /// <para>
    /// <b>Length (2026-09-15, re-stated 2026-10-06):</b> 63 title and 255 text. The dispatcher
    /// already builds this destination's payload with
    /// <see cref="ROROROblox.Core.Discord.PayloadLimits.Toast"/> — a group names the accounts that
    /// fit and ends "and N more". What changed is the backstop: the SHELL used to cut a longer
    /// string, silently and mid-line. Nothing cuts now except <see cref="AlertBalloon"/>'s own
    /// coercion, which cuts with a visible <c>…</c>.
    /// </para>
    /// <para>
    /// <b>Thread-safety (2026-09-11):</b> marshals for the same reason
    /// <see cref="SetMemoryWarning"/> does, and this one had been missing it. Its callers are not on
    /// the UI thread: <c>AlertDispatcher.DispatchAsync</c> is invoked fire-and-forget from
    /// <c>MetricReportSinkAdapter.AlertsRaised</c>, which is raised on a thread-pool timer thread when a
    /// metric grouping window closes (a gRPC handler thread until 2026-09-15). <c>_taskbarIcon</c> is a
    /// WPF <c>FrameworkElement</c>, so touching it from there throws — and the dispatcher's own catch
    /// swallows that into one Warning line, so the alert vanished with no toast and no visible error.
    /// </para>
    /// <para>
    /// Worse than it sounds, because the dispatcher's fan-out loop is sequential and stamps the
    /// cooldown (per account and kind; per account and metric id for a metric breach, corrected
    /// 2026-09-15) inside it, after each destination's send: a throw here ended the loop, so every
    /// destination ordered after Local lost its send too. The metric-alert default
    /// routes to Local and nowhere else, which is exactly the configuration in which the failure is
    /// invisible.
    /// </para>
    /// </summary>
    public void ShowToast(string title, string message, Guid? accountId = null)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_disposed) return;

            var balloon = new AlertBalloon
            {
                Title = title,
                Body = message,
                AccountId = accountId,
            };

            // Subscribed per balloon, so the handler closes over THIS notification's account and
            // nothing else can reach it. Close first, then focus: a balloon still on screen over a
            // window that just came forward reads as an unfinished click.
            balloon.FocusRequested += (_, id) =>
            {
                _taskbarIcon.CloseBalloon();
                RequestFocusAccount?.Invoke(this, id);
            };

            _taskbarIcon.ShowCustomBalloon(balloon, PopupAnimation.Fade, BalloonMilliseconds);

            // AFTER the balloon is up, and inside the marshal. After, because the sound announces
            // something that is already on screen — a chime that lands first points at nothing. And
            // inside, because this is the one place that runs exactly once per notification, which
            // is what "one event is one sound" means operationally. It cannot throw (see
            // IAlertSoundPlayer.Play), so it cannot cost the balloon or the rest of the fan-out.
            _sound.Play();
        });
    }

    /// <summary>
    /// How long a drawn balloon stays. The shell balloon's own dwell is a user/system setting we
    /// never saw; this one is ours, so it is written down. Eight seconds is long enough to read two
    /// wrapped lines and decide whether to click, and short enough not to sit over a game.
    /// <para>
    /// <c>ShowCustomBalloon</c> takes a nullable timeout where null means "stay until closed", and
    /// that is explicitly NOT what this is: a notification nobody dismisses is a notification that
    /// covers the screen corner all evening.
    /// </para>
    /// </summary>
    private const int BalloonMilliseconds = 8000;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _streamerIdentity.Changed -= OnStreamerModeChanged;
        _customOn?.Dispose();
        _customOff?.Dispose();
        _customError?.Dispose();
        _taskbarIcon.Dispose();
    }
}
