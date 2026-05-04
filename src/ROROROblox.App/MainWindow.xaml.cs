using System.ComponentModel;
using System.Windows;
using ROROROblox.App.ViewModels;

namespace ROROROblox.App;

public partial class MainWindow : Window
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
