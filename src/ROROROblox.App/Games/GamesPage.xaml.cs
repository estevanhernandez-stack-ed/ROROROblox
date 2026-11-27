using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ROROROblox.App.Localization;
using ROROROblox.App.Modals;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.App.Games;

/// <summary>
/// The Library destination, hosted by the shell (F-013 — formerly <c>GamesWindow</c>). <c>Loaded</c>
/// refires on every navigation back here, so each visit reloads the stores — the same freshness the
/// per-open window had.
/// <para>
/// v1.36 item 9: this file is wiring. Every decision (chips, the one box, paste routing, who Play
/// launches as, the remove confirm) is in <see cref="LibraryViewModel"/> and tested there. What stays
/// here is what needs a window or a focus: the rename dialog, the Play as picker's placement, and the
/// box taking focus when the page opens.
/// </para>
/// </summary>
internal partial class GamesPage : UserControl
{
    private readonly LibraryViewModel _vm;
    private readonly AccountPickerFlyout _picker = new();
    private LibraryItem? _pickerFor;

    /// <param name="viewModel">The page's brain, built by the composition root beside <c>MainViewModel</c>.</param>
    /// <param name="pickerAccounts">What the Play as picker lists: <c>MainViewModel.Accounts</c> (UI thread; the page runs there).</param>
    public GamesPage(LibraryViewModel viewModel, IEnumerable pickerAccounts)
    {
        _vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ArgumentNullException.ThrowIfNull(pickerAccounts);
        DataContext = _vm;
        InitializeComponent();
        _picker.ItemsSource = pickerAccounts;
        _picker.AccountPicked += OnAccountPicked;
        _vm.PropertyChanged += OnViewModelChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    internal LibraryViewModel ViewModel => _vm;

    private Window? _window;

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_window is not null)
        {
            _window.StateChanged -= OnWindowStateChanged;
            _window = null;
        }

        _vm.Detach();
    }

    /// <summary>Minimised declares no interest in anyone's pulse; restored declares the tracked set again.</summary>
    private void OnWindowStateChanged(object? sender, EventArgs e)
        => _vm.SetWindowMinimised(_window?.WindowState == WindowState.Minimized);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_window is null && Window.GetWindow(this) is { } window)
        {
            _window = window;
            window.StateChanged += OnWindowStateChanged;
        }

        _vm.SetWindowMinimised(_window?.WindowState == WindowState.Minimized);
        LibraryBox.Focus();
        try
        {
            await _vm.LoadAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _vm.Report(Loc.Format("Shell_Games_CouldntAdd", ex.Message));
        }
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LibraryViewModel.IsRemoveConfirmOpen)) return;

        // The confirm opens on Cancel, so a stray Enter or Space does the safe thing; closing it hands the box its focus back.
        Dispatcher.BeginInvoke(() =>
        {
            if (_vm.IsRemoveConfirmOpen) CancelRemoveButton.Focus();
            else LibraryBox.Focus();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private static LibraryItem? ItemOf(object sender)
        => (sender as FrameworkElement)?.DataContext as LibraryItem
           ?? ((sender as FrameworkElement)?.Tag as LibraryItem);

    // ---------- the one box ----------

    private async void OnBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await _vm.SubmitAsync();
    }

    private async void OnAddSearchResultClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: GameSearchResult result })
        {
            await _vm.AddSearchResultAsync(result);
        }
    }

    // ---------- tiles ----------

    // The pulse asks only for what is on screen, and the page says what that is with four events on each template
    // root: Loaded and Unloaded (a container entering and leaving the tree), DataContextChanged (a recycled
    // container handed another entry), and IsVisibleChanged (the other layout collapsing, the window hiding). The
    // set is by root, so an entry shown by two roots for an instant stays on screen until both have gone.

    private void NoteSlot(object sender)
    {
        if (sender is FrameworkElement { IsVisible: true } root && root.DataContext is LibraryItem item)
        {
            _vm.NoteOnScreen(item, root);
        }
        else if (sender is FrameworkElement gone)
        {
            _vm.NoteOffScreen(gone);
        }
    }

    /// <summary>A tile or row was realised, which only happens for entries on screen: ask for its icon if it has none, and count it in.</summary>
    private void OnTileLoaded(object sender, RoutedEventArgs e) => NoteSlot(sender);

    private void OnTileUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement root) _vm.NoteOffScreen(root);
    }

    private void OnTileDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => NoteSlot(sender);

    private void OnTileVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => NoteSlot(sender);

    private async void OnPlayClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item) return;
        try
        {
            await _vm.PlayAsync(item.Entry, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _vm.Report(Loc.Format("Shell_Games_CouldntAdd", ex.Message));
        }
    }

    private void OnPlayAsClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item || sender is not Button button) return;
        _pickerFor = item;
        _picker.PlacementTarget = button;
        _picker.IsOpen = true;
    }

    private async void OnAccountPicked(object? sender, AccountSummary account)
    {
        if (_pickerFor is not { } item) return;
        try
        {
            await _vm.PlayAsync(item.Entry, account.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _vm.Report(Loc.Format("Shell_Games_CouldntAdd", ex.Message));
        }
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    // ---------- management (the entry menu) ----------

    private async void OnSetDefaultClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item) return;
        try { await _vm.SetDefaultAsync(item.Entry); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _vm.Report(Loc.Format(item.IsPrivate ? "Shell_Games_CouldntSetDefaultServer" : "Shell_Games_CouldntSetDefault", ex.Message));
        }
    }

    private async void OnClearDefaultClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item) return;
        try { await _vm.ClearDefaultAsync(item.Entry); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _vm.Report(Loc.Format(item.IsPrivate ? "Shell_Games_CouldntClearDefaultServer" : "Shell_Games_CouldntClearDefault", ex.Message));
        }
    }

    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item) return;

        var entry = item.Entry;
        var target = new RenameTarget(
            entry.Kind == LibraryEntryKind.PrivateServer ? RenameTargetKind.PrivateServer : RenameTargetKind.Game,
            entry.Kind == LibraryEntryKind.PrivateServer && entry.PrivateServerId is { } serverId ? serverId : entry.PlaceId,
            entry.Name,
            entry.LocalName);
        var result = await RenameWindow.ShowAsync(Window.GetWindow(this)!, target);
        if (result.Kind == RenameResultKind.Cancel) return;

        try { await _vm.RenameAsync(entry, result.NewName); }
        catch (KeyNotFoundException)
        {
            _vm.Report(Loc.Get(item.IsPrivate ? "Shell_Games_ServerNotSaved" : "Shell_Msg_GameNotSaved"));
            await _vm.ReloadAsync();
        }
        catch (System.IO.IOException ex)
        {
            _vm.Report(Loc.Format("Shell_Games_CouldntSaveNameChangeDisk", ex.Message));
        }
    }

    private async void OnResetNameClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item) return;
        try { await _vm.ResetNameAsync(item.Entry); }
        catch (KeyNotFoundException)
        {
            _vm.Report(Loc.Get(item.IsPrivate ? "Shell_Games_ServerNotSaved" : "Shell_Msg_GameNotSaved"));
            await _vm.ReloadAsync();
        }
    }

    // ---------- remove: the in-page confirm ----------

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            _vm.RequestRemove(item);
        }
    }

    private async void OnConfirmRemoveClick(object sender, RoutedEventArgs e) => await _vm.ConfirmRemoveAsync();

    private void OnCancelRemoveClick(object sender, RoutedEventArgs e) => _vm.CancelRemove();

    private void OnRemoveBarKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        _vm.CancelRemove();
    }
}
