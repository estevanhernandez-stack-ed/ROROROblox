using System.Windows;
using ROROROblox.Core;

namespace ROROROblox.App.Modals;

/// <summary>
/// Shared rename popup for FavoriteGame, SavedPrivateServer, and Account. v1.3.x. Spec §5.5.
/// One window class, three entity kinds. <see cref="MainViewModel"/>'s switch on
/// <see cref="RenameTarget.Kind"/> handles the dispatch after this returns.
/// </summary>
internal partial class RenameWindow : Window
{
    private RenameResult _result = new(RenameResultKind.Cancel, NewName: null);

    private RenameWindow(RenameTarget target)
    {
        InitializeComponent();

        // Mono-micro reference — uppercase per brand. Em-dash separator.
        OriginalNameLine.Text = $"ROBLOX NAME — {target.OriginalName}".ToUpperInvariant();

        // Pre-fill: existing local name, falling back to the Roblox-side original. All-selected
        // so the first keystroke replaces — fast retype is the dominant interaction.
        NameTextBox.Text = target.CurrentLocalName ?? target.OriginalName;
        NameTextBox.SelectAll();

        // "Reset to original" only renders when there's something to reset to. The store's
        // UpdateLocalNameAsync(id, null) IS what reset does — same code path as Save with empty
        // input — but the explicit hyperlink makes the affordance discoverable.
        ResetRow.Visibility = target.CurrentLocalName is not null ? Visibility.Visible : Visibility.Collapsed;

        Loaded += (_, _) => NameTextBox.Focus();
    }

    /// <summary>
    /// Show the popup modally + return what the user did. Owner-window required so the popup
    /// inherits the theme + sits above the right window. v1.3.x. Spec §5.5.
    /// </summary>
    public static Task<RenameResult> ShowAsync(Window owner, RenameTarget target)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(target);

        var window = new RenameWindow(target) { Owner = owner };
        window.ShowDialog();
        return Task.FromResult(window._result);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var raw = NameTextBox.Text;
        // Trim + normalize empty/whitespace to null. Effective reset on empty Save.
        // Defense in depth — the store also normalizes — but doing it here means MainViewModel
        // sees the same null shape regardless of which path the user took.
        var normalized = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        _result = new RenameResult(RenameResultKind.Save, normalized);
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        // _result already defaults to Cancel; just close.
        DialogResult = false;
        Close();
    }

    private void OnResetClick(object sender, System.Windows.RoutedEventArgs e)
    {
        _result = new RenameResult(RenameResultKind.Reset, NewName: null);
        DialogResult = true;
        Close();
    }
}
