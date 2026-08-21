using System.Windows;
using System.Windows.Controls;

namespace ROROROblox.App.Plugins;

/// <summary>
/// The Plugins destination, hosted by the shell (F-013 — formerly <c>PluginsWindow</c>, the
/// "tray-menu auxiliary surface" whose header named the pattern the shell replaced). The VM is
/// constructed by the App and passed in — keeps DI and theming concerns out of XAML. <c>Loaded</c>
/// refires on every navigation back to this page, so each visit re-scans the plugin root — the
/// same freshness the per-open window had. The VM now lives as long as the shell; the page's
/// <see cref="IDisposable"/> passes the shell's close to it.
/// </summary>
internal partial class PluginsPage : UserControl, IDisposable
{
    private readonly PluginsViewModel _vm;

    public PluginsPage(PluginsViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        DataContext = _vm;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _vm.LoadAsync();
        }
        catch
        {
            // Defensive: a corrupt plugins root must not stop the page from rendering.
            // The (now-empty) list + empty state is the right fallback.
        }
    }

    // Detach the supervisor handler so the VM doesn't outlive the shell.
    public void Dispose() => _vm.Dispose();
}
