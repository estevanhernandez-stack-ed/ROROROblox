using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using ROROROblox.App.About;
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
        Loaded += OnLoaded;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
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
            await vm.LoadAsync();
        }

        // First-run welcome — only when there's nothing in the account list. If the user
        // already has accounts (returning from an upgrade), the sentinel write happens silently.
        if (WelcomeWindow.IsFirstRun())
        {
            WelcomeWindow.MarkShown();
            if (DataContext is MainViewModel mvm && mvm.Accounts.Count == 0)
            {
                var welcome = new WelcomeWindow { Owner = this };
                welcome.ShowDialog();
            }
        }
    }

    /// <summary>
    /// Closing the X minimizes to tray (does not quit). Real exit happens via the tray menu's
    /// Quit, which sets <see cref="App.IsShuttingDown"/> before <see cref="Application.Shutdown(int)"/>.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.IsShuttingDown)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
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
}
