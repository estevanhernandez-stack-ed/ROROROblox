using System.Windows;
using System.Windows.Controls;

namespace ROROROblox.App.Shell;

/// <summary>
/// The non-modal host for the six former modal islands (F-013). One instance at a time — every
/// door (toolbar buttons, Tools menu, tray items) resolves to the same window, surfaced and
/// navigated, so the double-window class the modal design allowed (two Preferences with
/// independent snapshots) cannot be built.
/// <para>
/// Pages are created lazily on first navigation and kept for the window's lifetime, so switching
/// pages does not lose in-progress state; a page that subscribes to app-lifetime services
/// implements <see cref="IDisposable"/> and is disposed when the window closes — the shell's
/// replacement for the <c>OnClosed</c> unsubscribe discipline the windows it absorbed carried.
/// </para>
/// </summary>
internal sealed partial class ShellWindow : Window
{
    /// <summary>Rail order — must match the ListBoxItems in XAML.</summary>
    private static readonly ShellPage[] RailOrder =
        [ShellPage.Games, ShellPage.Settings, ShellPage.History, ShellPage.Diagnostics, ShellPage.Plugins, ShellPage.About];

    private static readonly IReadOnlyDictionary<ShellPage, string> Titles = new Dictionary<ShellPage, string>
    {
        [ShellPage.Games] = "Games",
        [ShellPage.Settings] = "Settings",
        [ShellPage.History] = "History",
        [ShellPage.Diagnostics] = "Diagnostics",
        [ShellPage.Plugins] = "Plugins",
        [ShellPage.About] = "About",
    };

    private readonly Func<ShellPage, UserControl> _createPage;
    private readonly Dictionary<ShellPage, UserControl> _pages = [];

    public ShellWindow(Func<ShellPage, UserControl> createPage)
    {
        _createPage = createPage ?? throw new ArgumentNullException(nameof(createPage));
        InitializeComponent();
        Closed += OnShellClosed;
    }

    /// <summary>Select a page, creating it on first visit. Also the initial-navigation entry.</summary>
    public void NavigateTo(ShellPage page)
        => ShellNav.SelectedIndex = Array.IndexOf(RailOrder, page);

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ShellNav.SelectedIndex is var index && index < 0 || index >= RailOrder.Length) return;

        var page = RailOrder[index];
        if (!_pages.TryGetValue(page, out var control))
        {
            control = _createPage(page);
            _pages[page] = control;
        }

        PageHost.Content = control;
        // The header-matches-title-bar rule (conventions C2), held dynamically: one Alt-Tab entry,
        // named for wherever the user is right now.
        Title = Titles[page];
    }

    private void OnShellClosed(object? sender, EventArgs e)
    {
        foreach (var page in _pages.Values)
        {
            (page as IDisposable)?.Dispose();
        }

        _pages.Clear();
    }
}
