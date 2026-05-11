using System.Windows;

namespace ROROROblox.App.Plugins;

/// <summary>
/// Tray-menu auxiliary surface (matches Preferences / Diagnostics / History pattern).
/// Owned by <see cref="App.OpenPluginsFromTray"/>; modal-dialog lifetime. The VM is
/// constructed by the App and passed in — keeps DI and theming concerns out of XAML.
/// </summary>
internal partial class PluginsWindow : Window
{
    private readonly PluginsViewModel _vm;

    public PluginsWindow(PluginsViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        DataContext = _vm;
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _vm.LoadAsync();
        }
        catch
        {
            // Defensive: a corrupt plugins root must not stop the window from rendering.
            // The (now-empty) list + empty state is the right fallback.
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        // Detach the supervisor handler so the VM doesn't outlive the window.
        _vm.Dispose();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
