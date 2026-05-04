using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ROROROblox.App.About;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using Wpf.Ui.Controls;

namespace ROROROblox.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
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
}
