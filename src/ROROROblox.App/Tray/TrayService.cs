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
    private bool _disposed;

    public event EventHandler? RequestOpenMainWindow;
    public event EventHandler? RequestToggleMutex;
    public event EventHandler? RequestQuit;

    public TrayService()
    {
        _taskbarIcon = new TaskbarIcon();
        _taskbarIcon.TrayMouseDoubleClick += (_, _) => RequestOpenMainWindow?.Invoke(this, EventArgs.Empty);

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
        _taskbarIcon.Icon = LoadIcon(state);
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
        _taskbarIcon.Dispose();
    }
}
