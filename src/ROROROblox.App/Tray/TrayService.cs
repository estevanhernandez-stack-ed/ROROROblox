using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.Core;
using ROROROblox.Core.StreamerMode;

namespace ROROROblox.App.Tray;

/// <summary>
/// System-tray surface backed by Hardcodet's <see cref="TaskbarIcon"/>. Spec §5.2.
/// Doesn't own the mutex — fires <see cref="RequestToggleMutex"/> and lets the composition
/// root wire that to <see cref="IMutexHolder"/>. Icon swaps between cyan (ON) / grey (OFF) /
/// magenta (Error). Placeholder icons today; design-skill replaces before ship.
/// </summary>
internal sealed class TrayService : ITrayService
{
    private const string IconResourceBase = "/ROROROblox.App;component/Tray/Resources/";

    private readonly IStreamerIdentityProvider _streamerIdentity;
    private readonly ILogger<TrayService> _log;
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
    // _lastMemoryWarningAccountId is remembered so a balloon click can replay the account id on
    // RequestFocusAccount; Windows' TrayBalloonTipClicked carries no payload of its own. Cleared
    // by ShowToast so a click on an unrelated (idle-alert) balloon never fires a stale account.
    private bool _memoryWarningActive;
    private Guid? _lastMemoryWarningAccountId;

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

    public TrayService(IStreamerIdentityProvider streamerIdentity, ILogger<TrayService>? log = null)
    {
        _streamerIdentity = streamerIdentity;
        _log = log ?? NullLogger<TrayService>.Instance;
        _taskbarIcon = new TaskbarIcon();
        // Double-click is the user's "do the thing" gesture — App.xaml.cs decides whether
        // that means "launch main" or "surface the window" based on whether a main is set.
        _taskbarIcon.TrayMouseDoubleClick += (_, _) => RequestActivateMain?.Invoke(this, EventArgs.Empty);

        // Balloon click -> RequestFocusAccount, but only when the balloon on screen was a memory
        // warning (ShowToast clears _lastMemoryWarningAccountId, so a click on an idle-alert toast
        // is correctly a no-op here).
        _taskbarIcon.TrayBalloonTipClicked += (_, _) =>
        {
            if (_lastMemoryWarningAccountId is { } accountId)
            {
                RequestFocusAccount?.Invoke(this, accountId);
            }
        };

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

    /// <summary>See <see cref="SetMemoryWarning"/>'s thread-safety note — same marshaling reason.</summary>
    public void ShowMemoryWarning(string title, string message, Guid accountId)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_disposed) return;
            _lastMemoryWarningAccountId = accountId;
            _taskbarIcon.ShowBalloonTip(title, message, BalloonIcon.Warning);
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
                // tray-warn.ico isn't in the tree yet (626labs-design asset still pending) --
                // degrade to whatever's currently showing rather than crash the one code path
                // that only runs when a user is already in trouble.
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

        var toggle = new MenuItem { Header = "Multi-Instance: OFF" };
        toggle.Click += (_, _) => RequestToggleMutex?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(toggle);

        // Streamer mode (v1.10) — fake-name/avatar substitution for on-stream safety. Checkable
        // so the tray reflects state at a glance; Click reads the CURRENT provider state (not the
        // checkbox's own auto-toggled IsChecked) to decide the new value, then OnStreamerModeChanged
        // resyncs IsChecked once the provider's Changed event confirms the flip landed.
        var streamerMode = new MenuItem { Header = "Streamer mode", IsCheckable = true };

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

        var stopAll = new MenuItem { Header = "Stop all Roblox instances" };
        stopAll.Click += (_, _) => RequestStopAllInstances?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(stopAll);

        menu.Items.Add(new Separator());

        // F-034. The repo name is ROROROblox; the product is RoRoRo. This is the entry a
        // tray-resident app shows more often than any other surface it has.
        var open = new MenuItem { Header = $"Open {Branding.ProductName}" };
        open.Click += (_, _) => RequestOpenMainWindow?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(open);

        menu.Items.Add(new Separator());

        var preferences = new MenuItem { Header = "Settings..." };
        preferences.Click += (_, _) => RequestOpenPreferences?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(preferences);

        var history = new MenuItem { Header = "History..." };
        history.Click += (_, _) => RequestOpenHistory?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(history);

        var diagnostics = new MenuItem { Header = "Diagnostics..." };
        diagnostics.Click += (_, _) => RequestOpenDiagnostics?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(diagnostics);

        var plugins = new MenuItem { Header = "Plugins..." };
        plugins.Click += (_, _) => RequestOpenPlugins?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(plugins);

        var logs = new MenuItem { Header = "Open log folder" };
        logs.Click += (_, _) => RequestOpenLogs?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(logs);

        menu.Items.Add(new Separator());

        var quit = new MenuItem { Header = "Quit" };
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

    public void ShowToast(string title, string message)
    {
        if (_disposed) return;
        // This balloon isn't about any one account — clear so a click doesn't replay a stale
        // memory-warning account id via RequestFocusAccount.
        _lastMemoryWarningAccountId = null;
        _taskbarIcon.ShowBalloonTip(title, message, BalloonIcon.Info);
    }

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
