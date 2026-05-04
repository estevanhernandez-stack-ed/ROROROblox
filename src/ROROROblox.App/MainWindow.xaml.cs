using System.ComponentModel;
using System.Windows;
using ROROROblox.App.About;
using ROROROblox.App.ViewModels;
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
}
