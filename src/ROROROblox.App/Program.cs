using System;
using Velopack;

namespace ROROROblox.App;

// Velopack requires VelopackApp.Build().Run() as the FIRST line of Main so install /
// uninstall / restart-after-update hooks fire before WPF spins up. The auto-generated
// Main from App.xaml is replaced via <StartupObject> in the csproj.
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        VelopackApp.Build().SetArgs(args).Run();

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
