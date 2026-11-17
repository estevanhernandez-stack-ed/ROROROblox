using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using ROROROblox.App.About;
using ROROROblox.App.AppLifecycle;
using ROROROblox.App.Theming;
using ROROROblox.App.Tray;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using Wpf.Ui.Controls;

namespace ROROROblox.App;

internal partial class MainWindow : FluentWindow
{
    // Default expanded geometry — captured before first compact-toggle so we can restore it
    // when the user clicks Expand. Compact width is locked; height auto-fits to content.
    private const double CompactWidth = 380;
    private double _expandedWidth;
    private double _expandedHeight;
    private double _expandedMinWidth;
    private double _expandedMinHeight;
    private SizeToContent _expandedSizeToContent;
    private ResizeMode _expandedResizeMode;
    private bool _expandedGeometryCaptured;

    public MainWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        // A run on a test data folder labels both titles (taskbar and drawn bar). Both titles are
        // live {loc:Loc} bindings, and an assignment replaces a binding with a fixed string, so
        // this must stay inside the check: unconditional, it would freeze the title's language in
        // every shipped build.
        if (DataRootMarker.TitleSuffix(AppDataRoot.Decision) is { } testDataTag)
        {
            Title += testDataTag;
            MainTitleBar.Title += testDataTag;
        }
        // Guarantee the ContextMenu binding proxy carries the VM even if the resource-level
        // Data="{Binding}" inheritance context ever fails to capture it. Context menus live in a
        // separate popup tree and reach VM commands only through this proxy (see BindingProxy.cs).
        if (Resources["VmProxy"] is BindingProxy vmProxy)
        {
            vmProxy.Data = viewModel;
        }
        Loaded += OnLoaded;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // The keyboard vocabulary (F-112), built from the one shared table so the bindings here,
        // the menu hints, and the About page's list cannot drift apart. Editing controls keep
        // their own keys: a window-level KeyBinding is only consulted after the focused control
        // declines the keystroke, so Ctrl+F while typing in a TextBox stays the TextBox's.
        foreach (var binding in Input.KeyboardVocabulary.BuildBindings(action => action switch
        {
            Input.ShortcutAction.AddAccount => viewModel.AddAccountCommand,
            Input.ShortcutAction.LaunchAll => viewModel.LaunchAllCommand,
            Input.ShortcutAction.SquadLaunch => viewModel.OpenSquadLaunchCommand,
            Input.ShortcutAction.FocusFilter => new ViewModels.RelayCommand(() =>
            {
                FilterBox.Focus();
                FilterBox.SelectAll();
            }),
            Input.ShortcutAction.OpenGames => viewModel.OpenGamesCommand,
            Input.ShortcutAction.OpenSettings => viewModel.OpenPreferencesCommand,
            Input.ShortcutAction.OpenHistory => viewModel.OpenHistoryCommand,
            Input.ShortcutAction.OpenDiagnostics => viewModel.OpenDiagnosticsCommand,
            Input.ShortcutAction.OpenPlugins => viewModel.OpenPluginsCommand,
            // The discoverable list lives on the About page (F-112's second half).
            Input.ShortcutAction.OpenShortcutsList => viewModel.OpenAboutCommand,
            _ => null,
        }))
        {
            InputBindings.Add(binding);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsCompact) && DataContext is MainViewModel vm)
        {
            ApplyCompactState(vm.IsCompact);
        }
    }

    /// <summary>
    /// Resize + reflow the window when compact mode flips. Compact = fixed-width, auto-height,
    /// no resize handle. Expanded = restored to whatever geometry the window had before the
    /// first compact toggle, so the user's manual resizes survive a round-trip.
    /// </summary>
    private void ApplyCompactState(bool compact)
    {
        if (compact)
        {
            if (!_expandedGeometryCaptured)
            {
                _expandedWidth = Width;
                _expandedHeight = Height;
                _expandedMinWidth = MinWidth;
                _expandedMinHeight = MinHeight;
                _expandedSizeToContent = SizeToContent;
                _expandedResizeMode = ResizeMode;
                _expandedGeometryCaptured = true;
            }

            MinWidth = CompactWidth;
            MaxWidth = CompactWidth;
            Width = CompactWidth;
            MinHeight = 160;
            MaxHeight = double.PositiveInfinity;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
        }
        else
        {
            SizeToContent = SizeToContent.Manual;
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;
            MinWidth = _expandedMinWidth > 0 ? _expandedMinWidth : 640;
            MinHeight = _expandedMinHeight > 0 ? _expandedMinHeight : 400;
            Width = _expandedWidth > 0 ? _expandedWidth : 780;
            Height = _expandedHeight > 0 ? _expandedHeight : 600;
            ResizeMode = _expandedResizeMode == ResizeMode.NoResize ? ResizeMode.CanResize : _expandedResizeMode;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            // LoadAsync, then the sticky compact-mode restore (v1.18), then the saved placement (F-060),
            // in that order: the compact restore moves the geometry, so placement applied first would be
            // overwritten, and the rows the compact strip renders must exist before it. RunStartupLoadAsync
            // never throws (this handler is async void, so a throw would end the app on every start); a
            // failed step is logged there and the window loads with what it has.
            if (await vm.RunStartupLoadAsync() is { } placement)
            {
                RestoreWindowPlacement(placement);
            }
        }

        // First-run welcome — only when there is nothing in the account list.
        //
        // The sentinel is written INSIDE this branch. It used to be written before the account
        // check, so an upgrading user with accounts burned it and could never see the tour on any
        // later launch either. Tools > Welcome tour now reaches it on demand regardless (F-001);
        // this stops the app recording that it showed something it did not.
        //
        // An empty list that a failed load left behind is not a first run, so the tour stays out of the way
        // of the notice that says why the list is empty.
        //
        // Consequence, deliberate: an upgrading user keeps IsFirstRun() true, so if they later
        // remove every account the tour appears. An empty list is the state the tour is written for
        // and they have never seen it.
        if (DataContext is MainViewModel mvm
            && WelcomeWindow.ShouldShowOnStartup(WelcomeWindow.IsFirstRun(), mvm.Accounts.Count, mvm.AccountsLoadFailed))
        {
            // Let Show() finish first. On a first run the load above completes without yielding, so
            // this point is still inside the Loaded event, before the window is on screen: the tour
            // then came up over a HIDDEN main window, and a polite close from outside (taskkill, End
            // task) reached neither of them, because it goes to one unowned window and prefers a
            // visible one (measured and seen live 2026-11-17: one taskkill, no effect). With the main
            // window visible it is the window that receives the close, and the v1.39 quit path ends
            // the app, tour included.
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (!IsLoaded || App.IsShuttingDown)
            {
                return; // closed or quitting while we waited
            }

            WelcomeWindow.MarkShown();
            var welcome = new WelcomeWindow { Owner = this };
            welcome.ShowDialog();
            if (welcome.ClosedFromOutside)
            {
                // The sentinel is already written (above); an outside close does not un-show the tour.
                ExternalCloseQuit?.Invoke();
            }
        }
    }

    /// <summary>
    /// Task 8 — a tray memory-warning balloon click (<c>ITrayService.RequestFocusAccount</c>, via
    /// <c>App.xaml.cs</c>) lands here. Highlights the row through the VM (drives
    /// <see cref="AccountSummary.IsFocused"/>'s XAML trigger) and best-effort scrolls it into view.
    /// Deferred to <see cref="DispatcherPriority.Background"/> so a container that hasn't been
    /// generated yet this layout pass gets one more chance before we give up on the scroll — the
    /// highlight itself doesn't need this, since it's driven by data binding, not the visual tree.
    /// </summary>
    public void FocusAccountRow(Guid accountId)
    {
        if (DataContext is not MainViewModel vm) return;
        vm.SetFocusedAccount(accountId);

        var row = vm.Accounts.FirstOrDefault(a => a.Id == accountId);
        if (row is null) return;

        Dispatcher.InvokeAsync(() =>
        {
            if (AccountsItemsControl.ItemContainerGenerator.ContainerFromItem(row) is FrameworkElement container)
            {
                container.BringIntoView();
            }
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Sees a <c>WM_CLOSE</c> that another process posted to this window. Attached once the handle exists
    /// (a window the test harness builds but never shows never subscribes) and detaches itself when the
    /// window really closes.
    /// </summary>
    private ExternalCloseWatcher? _externalClose;

    /// <summary>
    /// How an outside close request ends the app: <c>App</c> wires this to the same quit path the tray's
    /// Quit uses. Unset in tests and before startup has wired the tray, where an outside close just hides.
    /// </summary>
    internal Action? ExternalCloseQuit { get; set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _externalClose ??= new ExternalCloseWatcher(this);
    }

    /// <summary>
    /// Closing the X minimizes to tray (does not quit). Real exit happens via the tray menu's
    /// Quit, which sets <see cref="App.IsShuttingDown"/> before <see cref="Application.Shutdown(int)"/>.
    /// A close request that arrived from outside the process (<c>taskkill</c> without <c>/F</c>, Task
    /// Manager's End task) is the one exception: it is identified positively by
    /// <see cref="ExternalCloseWatcher"/> and quits through <see cref="ExternalCloseQuit"/>. Every other
    /// close, including any the watcher cannot classify, hides.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        // F-060, and it must run BEFORE the branch below. Closing the X hides to tray rather than
        // quitting, which is the common case by a wide margin — saving only on real shutdown would
        // miss almost every time the user actually finishes with the window.
        SaveWindowPlacement();

        // Asked on every attempt, so the note is cleared by the close it belongs to.
        var external = _externalClose?.TryConsume() ?? false;

        switch (CloseRequestPolicy.Decide(App.IsShuttingDown, external))
        {
            case CloseDisposition.Proceed:
                break;

            case CloseDisposition.QuitApp:
                e.Cancel = true;
                Hide();
                // Posted, not called: this runs inside Close(), and the quit path closes windows.
                if (ExternalCloseQuit is { } quit)
                {
                    _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, quit);
                }
                break;

            default:
                e.Cancel = true;
                Hide();
                break;
        }
        base.OnClosing(e);
    }

    /// <summary>
    /// Records where the window is, so the next launch can put it back (F-060).
    /// </summary>
    /// <remarks>
    /// <see cref="Window.RestoreBounds"/> when maximized, because <c>Left</c>/<c>Width</c> then
    /// describe the maximized frame — saving those would restore a window that is maximized-shaped
    /// but not maximized, and un-maximizing it would leave it filling the screen with no way back
    /// to its old size.
    /// </remarks>
    private void SaveWindowPlacement()
    {
        if (DataContext is not MainViewModel vm) return;

        try
        {
            var maximized = WindowState == WindowState.Maximized;
            var bounds = maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
            _ = vm.SaveWindowPlacementAsync(
                new Core.WindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height, maximized));
        }
        catch
        {
            // Reading our own geometry can throw while the window is tearing down. A lost
            // placement is not worth a crash on exit.
        }
    }

    /// <summary>
    /// Puts the window back where it was, when that is still somewhere reachable (F-060).
    /// </summary>
    private void RestoreWindowPlacement(Core.WindowPlacement placement)
    {
        // The virtual screen is the union of every display, so this is the multi-monitor-aware
        // question: is any meaningful part of the saved rect still on SOME screen.
        var restorable = placement.IsRestorableOnto(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        if (!restorable) return;   // leave the XAML defaults; centred and visible beats remembered and lost

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = placement.Left;
        Top = placement.Top;
        Width = placement.Width;
        Height = placement.Height;
        if (placement.Maximized) WindowState = WindowState.Maximized;
    }

    /// <summary>
    /// Per-row Game ComboBox handler. When the user picks the
    /// <see cref="MainViewModel.JoinByLinkSentinel"/> entry, intercept the selection: revert the
    /// row's SelectedGame to the previously-picked real game, then open the paste-a-link modal.
    /// The brief flicker of the sentinel being selected is masked by the modal popping over.
    /// </summary>
    private async void OnGameComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        if (combo.DataContext is not AccountSummary summary) return;
        if (combo.SelectedItem is not FavoriteGame picked) return;
        if (!MainViewModel.IsJoinByLinkSentinel(picked)) return;

        // Revert to the most recent non-sentinel selection so the dropdown doesn't stick on
        // "(Paste a link...)" after the modal closes.
        var previous = e.RemovedItems.OfType<FavoriteGame>()
            .FirstOrDefault(g => !MainViewModel.IsJoinByLinkSentinel(g));
        summary.SelectedGame = previous;

        if (DataContext is MainViewModel vm)
        {
            await vm.OpenJoinByLinkAsync(summary);
        }
    }

    // Drag-to-reorder. The grip element on each row records the press position; once the cursor
    // moves a system-threshold distance with the left button held, we initiate a DragDrop
    // carrying the AccountSummary. Visual polish:
    //   - DragGhostPopup (declared in XAML) hosts a RenderTargetBitmap snapshot of the source
    //     row that follows the cursor at 0.78 opacity. Repositioned on every PreviewDragOver.
    //   - Cyan insertion line above the target row via AccountSummary.IsDropTarget binding in
    //     the row template.
    private const string DragFormat = "ROROROblox.AccountSummary";
    private Point _gripPressPoint;
    private AccountSummary? _gripPressedSummary;
    private bool _ghostActive;

    private void OnAccountGripMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement el) return;
        if (el.Tag is not AccountSummary summary) return;
        _gripPressPoint = e.GetPosition(this);
        _gripPressedSummary = summary;
    }

    private void OnAccountGripMouseMove(object sender, MouseEventArgs e)
    {
        if (_gripPressedSummary is null || e.LeftButton != MouseButtonState.Pressed)
        {
            _gripPressedSummary = null;
            return;
        }
        // Reorder is disabled while a tag/name filter is active (v1.6.0, item 7b) — the visible row
        // set is a subset of Accounts, so drop-position → source-index math would be wrong. Clearing
        // the filter restores reorder. Filtering + reordering simultaneously is a non-case.
        if (DataContext is MainViewModel { IsFilterActive: true })
        {
            _gripPressedSummary = null;
            return;
        }
        var current = e.GetPosition(this);
        var dx = Math.Abs(current.X - _gripPressPoint.X);
        var dy = Math.Abs(current.Y - _gripPressPoint.Y);
        if (dx < SystemParameters.MinimumHorizontalDragDistance &&
            dy < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var sourceRow = FindParentRowBorder((DependencyObject)sender);
        if (sourceRow is not null)
        {
            StartDragGhost(sourceRow, current);
            PreviewDragOver += OnDragGhostFollow;
        }

        try
        {
            var data = new DataObject(DragFormat, _gripPressedSummary);
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
        }
        finally
        {
            _gripPressedSummary = null;
            // Clear ghost + drop-target highlights regardless of how the drag ended (drop /
            // cancel / outside-window release / Esc).
            PreviewDragOver -= OnDragGhostFollow;
            EndDragGhost();
            ClearAllDropTargets();
        }
    }

    private void StartDragGhost(FrameworkElement sourceRow, Point cursorWin)
    {
        try
        {
            var width = (int)Math.Ceiling(sourceRow.ActualWidth);
            var height = (int)Math.Ceiling(sourceRow.ActualHeight);
            if (width <= 0 || height <= 0) return;

            // Render at the source's actual DPI so the bitmap doesn't blur on high-DPI displays.
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(sourceRow);
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                pixelWidth: (int)Math.Ceiling(width * dpi.DpiScaleX),
                pixelHeight: (int)Math.Ceiling(height * dpi.DpiScaleY),
                dpiX: dpi.PixelsPerInchX,
                dpiY: dpi.PixelsPerInchY,
                pixelFormat: System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(sourceRow);
            rtb.Freeze();
            DragGhostImage.Source = rtb;
            DragGhostImage.Width = width;
            DragGhostImage.Height = height;

            PositionDragGhost(cursorWin);
            DragGhostPopup.IsOpen = true;
            _ghostActive = true;
        }
        catch
        {
            // Ghost is decoration only — never block the drag if rendering fails.
            _ghostActive = false;
        }
    }

    private void PositionDragGhost(Point cursorWin)
    {
        if (!_ghostActive) return;
        var screenPoint = PointToScreen(cursorWin);
        // Popup with Placement=Absolute uses screen coordinates. Offset slightly so the ghost
        // sits below-right of the cursor instead of dead-center.
        DragGhostPopup.HorizontalOffset = screenPoint.X + 8;
        DragGhostPopup.VerticalOffset = screenPoint.Y + 8;
    }

    private void EndDragGhost()
    {
        DragGhostPopup.IsOpen = false;
        DragGhostImage.Source = null;
        _ghostActive = false;
    }

    private void OnDragGhostFollow(object sender, DragEventArgs e)
    {
        if (!_ghostActive) return;
        PositionDragGhost(e.GetPosition(this));
    }

    private static FrameworkElement? FindParentRowBorder(DependencyObject start)
    {
        var current = start;
        while (current is not null)
        {
            if (current is FrameworkElement fe && fe.Name == "AccountRowBorder")
            {
                return fe;
            }
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void OnAccountRowDragEnter(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DragFormat)) return;
        if (sender is not FrameworkElement el || el.Tag is not AccountSummary target) return;
        // Only one row can be highlighted at a time — clear any others first to defend against
        // racey enter-before-leave events.
        ClearAllDropTargets(except: target);
        target.IsDropTarget = true;
    }

    private void OnAccountRowDragLeave(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not AccountSummary target) return;
        target.IsDropTarget = false;
    }

    private void OnAccountRowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnAccountRowDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DragFormat)) return;
        if (e.Data.GetData(DragFormat) is not AccountSummary source) return;
        if (sender is not FrameworkElement el || el.Tag is not AccountSummary target) return;
        if (DataContext is not MainViewModel vm) return;
        // Belt-and-suspenders with the drag-start guard: never mutate order while filtered (the
        // visible rows are a subset of Accounts, so the reorder math would be wrong). v1.6.0 (7b).
        if (vm.IsFilterActive)
        {
            ClearAllDropTargets();
            return;
        }

        ClearAllDropTargets();
        await vm.MoveAccountAsync(source, target);
        e.Handled = true;
    }

    private void ClearAllDropTargets(AccountSummary? except = null)
    {
        if (DataContext is not MainViewModel vm) return;
        foreach (var summary in vm.Accounts)
        {
            if (!ReferenceEquals(summary, except))
            {
                summary.IsDropTarget = false;
            }
        }
    }

    /// <summary>
    /// A follow-alt chip in the row's follow strip got clicked. The chip's Tag is the TARGET
    /// account (the one to follow); the row that owns the chip is the SOURCE (the one being
    /// launched). Walk up the visual tree to find the source's AccountSummary, then route to
    /// MainViewModel.FollowAltAsync.
    /// </summary>
    private async void OnFollowChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not AccountSummary target) return;
        // The row's outer Border was named AccountRowBorder in the template — it carries the
        // row's AccountSummary as DataContext. Walk up until we find it.
        var source = FindRowAccountSummary(el);
        if (source is null) return;
        if (DataContext is not MainViewModel vm) return;
        await vm.FollowAltAsync(source, target);
        e.Handled = true;
    }

    // Add-affordance toggle (v1.6.0, item 7a). The collapsed "+" pill engages an inline TextBox:
    //   - OnAddTagPlusClick      — "+" clicked → IsAddingTag=true (XAML swaps pill → box).
    //   - OnAddTagBoxVisibleChanged — box just became visible → auto-focus + select-all.
    //   - OnAddTagKeyDown        — Enter commits the tag (AddTagCommand) + collapses; Esc cancels.
    //   - OnAddTagBoxLostFocus   — focus lost with no commit → clear + collapse unchanged.
    // Compact-mode rows never carry these handlers (chips stay read-only), so the toggle is
    // full-mode only by construction.

    // Tools has no click handler here by design: it is a ToolsDropDownButton (a Button subclass
    // that owns its ContextMenu), so opening, keyboard highlight of the first item, second-click
    // dismiss, and the ExpandCollapse automation pattern all live in Controls/ToolsDropDownButton.cs.
    // The first version hand-wrote the opening here and announced to a screen reader as a plain
    // button; a real Menu was tried after that and rejected (the control's file records why). This
    // comment said "it is a Menu" until 2026-08-30.

    /// <summary>The "+" pill was clicked — engage the inline tag input for this row.</summary>
    private void OnAddTagPlusClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not AccountSummary summary) return;
        summary.IsAddingTag = true;
        e.Handled = true;
        // Focus happens in OnAddTagBoxVisibleChanged once the box is realized + visible.
    }

    /// <summary>
    /// The inline tag box just became visible — focus it so the user can type immediately.
    /// Dispatched at Input priority because the element isn't focusable until layout settles.
    /// </summary>
    private void OnAddTagBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true) return;
        if (sender is not Wpf.Ui.Controls.TextBox box) return;
        box.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(() =>
            {
                box.Focus();
                box.SelectAll();
            }));
    }

    /// <summary>
    /// Inline "add tag" box. Enter routes the trimmed text to the row's
    /// <see cref="AccountSummary.AddTagCommand"/> (normalization — trim/dedupe/length+count caps —
    /// lives in the command), clears the box, and collapses back to the "+" pill. Escape cancels:
    /// clears + collapses unchanged. The box's Tag carries the row's AccountSummary. v1.6.0 (7a).
    /// </summary>
    private void OnAddTagKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.TextBox box) return;
        if (box.Tag is not AccountSummary summary) return;

        if (e.Key == Key.Escape)
        {
            // Cancel — discard text and collapse back to the "+" pill.
            box.Text = string.Empty;
            summary.IsAddingTag = false;
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter && e.Key != Key.Return) return;

        var text = box.Text;
        if (summary.AddTagCommand.CanExecute(text))
        {
            summary.AddTagCommand.Execute(text);
        }
        box.Text = string.Empty;
        summary.IsAddingTag = false;
        e.Handled = true;
    }

    /// <summary>
    /// Focus left the inline tag box with no Enter commit (click elsewhere / Tab away) — collapse
    /// back to the "+" pill, discarding any half-typed text. v1.6.0 (7a).
    /// </summary>
    private void OnAddTagBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.TextBox box) return;
        if (box.Tag is not AccountSummary summary) return;
        box.Text = string.Empty;
        summary.IsAddingTag = false;
    }

    /// <summary>
    /// The panel's note box commits on LostFocus (the binding) and on Enter, the AddTagBox pattern: Enter pushes
    /// the text to the row now and moves focus on, so the next Tab lands somewhere useful and the LostFocus that
    /// follows has nothing left to write. Escape throws away what was typed and re-reads the saved note.
    /// </summary>
    private void OnNoteBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.TextBox box) return;
        var binding = box.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty);

        if (e.Key == Key.Escape)
        {
            binding?.UpdateTarget();
            box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter && e.Key != Key.Return) return;
        binding?.UpdateSource();
        box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        e.Handled = true;
    }

    private static AccountSummary? FindRowAccountSummary(DependencyObject start)
    {
        var current = start;
        while (current is not null)
        {
            if (current is FrameworkElement fe
                && fe.DataContext is AccountSummary summary
                && fe.Name == "AccountRowBorder")
            {
                return summary;
            }
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    /// <summary>
    /// Per-row caption color swatch click. Opens the picker; on apply, the AccountSummary's
    /// CaptionColorHex setter triggers MainViewModel persistence + window decorator refresh
    /// for any running Roblox process for that account.
    /// </summary>
    private void OnCaptionColorSwatchClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not AccountSummary summary) return;
        var window = new CaptionColorPickerWindow(summary, () =>
        {
            // Fire decorator refresh so an already-running Roblox window updates instantly
            // instead of waiting for the 1.5s tick.
            if (DataContext is MainViewModel vm)
            {
                vm.RefreshDecoratorForAccount(summary.Id);
            }
        })
        {
            Owner = this,
        };
        window.ShowDialog();
        e.Handled = true;
    }

    /// <summary>
    /// Per-row FPS cap ComboBox — selection changed. Reads the selected item's Tag, converts to
    /// int? (null = no cap, int = target fps), guards against no-op re-fires, then routes to
    /// <see cref="MainViewModel.OnFpsCapChangedAsync"/>.
    /// </summary>
    private void FpsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        if (combo.DataContext is not AccountSummary row) return;
        if (combo.SelectedItem is not ComboBoxItem item) return;
        int? newValue = item.Tag switch
        {
            null => null,
            int i => i,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => null
        };
        if (row.FpsCap == newValue) return;
        if (DataContext is MainViewModel vm)
        {
            _ = vm.OnFpsCapChangedAsync(row, newValue);
        }
    }

    /// <summary>
    /// Per-row FPS cap ComboBox — loaded. Selects the ComboBoxItem whose Tag matches the row's
    /// current <see cref="AccountSummary.FpsCap"/> so the dropdown shows the right value on first
    /// render (and after virtualization recycles the row).
    /// </summary>
    private void FpsCombo_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        if (combo.DataContext is not AccountSummary row) return;
        var current = row.FpsCap;
        foreach (var obj in combo.Items)
        {
            if (obj is not ComboBoxItem item) continue;
            int? itemValue = item.Tag switch
            {
                null => null,
                int i => i,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => null
            };
            if (itemValue == current)
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }
}
