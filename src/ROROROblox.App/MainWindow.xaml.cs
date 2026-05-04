using System.Windows;
using ROROROblox.App.Startup;

namespace ROROROblox.App;

public partial class MainWindow : Window
{
    private readonly IStartupRegistration _startupRegistration;

    public MainWindow(IStartupRegistration startupRegistration)
    {
        _startupRegistration = startupRegistration;
        InitializeComponent();
        StatusText.Text = $"DI wired. run-on-login: {(_startupRegistration.IsEnabled() ? "ON" : "OFF")}";
    }
}
