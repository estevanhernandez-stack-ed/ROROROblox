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

        // The global RegisterGlobalDarkTitleBar hook fires on Loaded — AFTER the first frame,
        // which was half the bright flash Este kept seeing on a 920px window (2026-09-05).
        // Calling the helper here defers to SourceInitialized instead: the HWND exists, nothing
        // has painted, and the chrome is dark from frame one.
        Theming.WindowTheming.ApplyDarkTitleBar(this);

        // The other half (still bright after the chrome fix, same day): the HWND's surface is
        // on screen before WPF presents its first frame of a heavy page, and an unpresented
        // surface shows white regardless of Background. Round 2's Window.Opacity attempt was a
        // silent no-op — WPF ignores it without AllowsTransparency — so the guard is native
        // layered-alpha; see RevealAfterFirstRender's own comment for the full journey.
        Theming.WindowTheming.RevealAfterFirstRender(this);
        Closed += OnShellClosed;

        // The same vocabulary the main window binds (F-112), scoped to what makes sense here:
        // destination shortcuts navigate this window's pages, and Ctrl+1..6 walk the rail in
        // order. Actions that need the main window (add account, launches, the filter) are not
        // mapped — BuildBindings skips what a window does not answer for.
        foreach (var binding in Input.KeyboardVocabulary.BuildBindings(action => action switch
        {
            Input.ShortcutAction.OpenGames => NavigateCommand(ShellPage.Games),
            Input.ShortcutAction.OpenSettings => NavigateCommand(ShellPage.Settings),
            Input.ShortcutAction.OpenHistory => NavigateCommand(ShellPage.History),
            Input.ShortcutAction.OpenDiagnostics => NavigateCommand(ShellPage.Diagnostics),
            Input.ShortcutAction.OpenPlugins => NavigateCommand(ShellPage.Plugins),
            Input.ShortcutAction.OpenShortcutsList => NavigateCommand(ShellPage.About),
            _ => null,
        }))
        {
            InputBindings.Add(binding);
        }

        for (var i = 0; i < RailOrder.Length; i++)
        {
            InputBindings.Add(new System.Windows.Input.KeyBinding(
                NavigateCommand(RailOrder[i]),
                System.Windows.Input.Key.D1 + i,
                System.Windows.Input.ModifierKeys.Control));
        }
    }

    private ViewModels.RelayCommand NavigateCommand(ShellPage page)
        => new(() => NavigateTo(page));

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
