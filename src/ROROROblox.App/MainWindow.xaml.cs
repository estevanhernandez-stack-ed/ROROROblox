using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
    // operation carrying the AccountSummary as the data. The row's Border accepts the drop and
    // calls back into MainViewModel.MoveAccountAsync to swap and persist.
    private const string DragFormat = "ROROROblox.AccountSummary";
    private Point _gripPressPoint;
    private AccountSummary? _gripPressedSummary;

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

        try
        {
            var data = new DataObject(DragFormat, _gripPressedSummary);
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
        }
        finally
        {
            _gripPressedSummary = null;
        }
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

        await vm.MoveAccountAsync(source, target);
        e.Handled = true;
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
