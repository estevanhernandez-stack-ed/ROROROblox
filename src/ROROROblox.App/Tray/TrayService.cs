using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using ROROROblox.Core;

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

    private readonly TaskbarIcon _taskbarIcon;
    private readonly MenuItem _toggleItem;

    // When the avatar painter sets these, UpdateStatus uses them in place of the resource ICOs.
    // Per-state so the cyan/grey/magenta ring still reflects mutex status.
    private Icon? _customOn;
    private Icon? _customOff;
    private Icon? _customError;
    private MultiInstanceState _currentState = MultiInstanceState.Off;
    private bool _disposed;

    public event EventHandler? RequestOpenMainWindow;
    public event EventHandler? RequestToggleMutex;
    public event EventHandler? RequestQuit;
    public event EventHandler? RequestOpenDiagnostics;
    public event EventHandler? RequestOpenLogs;
    public event EventHandler? RequestOpenPreferences;
    public event EventHandler? RequestActivateMain;
    public event EventHandler? RequestOpenHistory;

    public TrayService()
    {
        _taskbarIcon = new TaskbarIcon();
        // Double-click is the user's "do the thing" gesture — App.xaml.cs decides whether
        // that means "launch main" or "surface the window" based on whether a main is set.
        _taskbarIcon.TrayMouseDoubleClick += (_, _) => RequestActivateMain?.Invoke(this, EventArgs.Empty);

        var (toggle, menu) = BuildContextMenu();
        _toggleItem = toggle;
        _taskbarIcon.ContextMenu = menu;

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
        _taskbarIcon.ToolTipText = state switch
        {
            MultiInstanceState.On => "ROROROblox — Multi-Instance ON",
            MultiInstanceState.Off => "ROROROblox — Multi-Instance OFF",
            MultiInstanceState.Error => "ROROROblox — Multi-Instance ERROR (mutex lost)",
            _ => "ROROROblox",
        };
        _toggleItem.Header = state switch
        {
            MultiInstanceState.On => "Multi-Instance: ON ✓",
            MultiInstanceState.Error => "Multi-Instance: ERROR (mutex lost)",
            _ => "Multi-Instance: OFF",
        };
        _toggleItem.IsEnabled = state != MultiInstanceState.Error;
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

    private Icon ResolveIconForState(MultiInstanceState state)
    {
        var custom = state switch
        {
            MultiInstanceState.On => _customOn,
            MultiInstanceState.Error => _customError,
            _ => _customOff,
        };
        return custom ?? LoadIcon(state);
    }

    private (MenuItem toggle, ContextMenu menu) BuildContextMenu()
    {
        var menu = new ContextMenu();

        var toggle = new MenuItem { Header = "Multi-Instance: OFF" };
        toggle.Click += (_, _) => RequestToggleMutex?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(toggle);

        menu.Items.Add(new Separator());

        var open = new MenuItem { Header = "Open ROROROblox" };
        open.Click += (_, _) => RequestOpenMainWindow?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(open);

        menu.Items.Add(new Separator());

        var preferences = new MenuItem { Header = "Preferences..." };
        preferences.Click += (_, _) => RequestOpenPreferences?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(preferences);

        var history = new MenuItem { Header = "History..." };
        history.Click += (_, _) => RequestOpenHistory?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(history);

        var diagnostics = new MenuItem { Header = "Diagnostics..." };
        diagnostics.Click += (_, _) => RequestOpenDiagnostics?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(diagnostics);

        var logs = new MenuItem { Header = "Open log folder" };
        logs.Click += (_, _) => RequestOpenLogs?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(logs);

        menu.Items.Add(new Separator());

        var quit = new MenuItem { Header = "Quit" };
        quit.Click += (_, _) => RequestQuit?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(quit);

        return (toggle, menu);
    }

    private static Icon LoadIcon(MultiInstanceState state)
    {
        var filename = state switch
        {
            MultiInstanceState.On => "tray-on.ico",
            MultiInstanceState.Error => "tray-error.ico",
            _ => "tray-off.ico",
        };

        var resource = Application.GetResourceStream(new Uri(IconResourceBase + filename, UriKind.Relative))
            ?? throw new InvalidOperationException($"Tray icon resource not found: {filename}");
        using var stream = resource.Stream;
        return new Icon(stream);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _customOn?.Dispose();
        _customOff?.Dispose();
        _customError?.Dispose();
        _taskbarIcon.Dispose();
    }
}
