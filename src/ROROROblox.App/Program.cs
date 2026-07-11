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

        // Portable runs get their own AppUserModelID. Velopack sets the same
        // "velopack.RORORO" for installed AND portable, and Windows resolves a taskbar
        // button's icon for an explicit AUMID from a Start-menu shortcut carrying that
        // AUMID — before ever falling back to the exe icon. So a portable launched on a
        // machine with a stale install (any pre-v1.9.1.0 build, whose exe had no embedded
        // icon) inherits the stale shortcut's blank icon, no matter how correct its own
        // icon is. Re-setting a suffixed AUMID (last caller wins; every window is created
        // after this line) matches no shortcut anywhere, which makes the shell fall back
        // to this exe's embedded icon (verified on Win11). Installed runs keep Velopack's
        // default AUMID so shortcut grouping and pinning behave as Velopack intends.
        // The pinned Velopack 0.0.1298 has no SetAppUserModelId() on VelopackApp, hence
        // the direct shell call rather than the builder API newer versions offer.
        if (IsPortableInstall(AppContext.BaseDirectory))
        {
            Windows.Win32.PInvoke.SetCurrentProcessExplicitAppUserModelID("velopack.RORORO.portable");
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    // Velopack's portable zip carries a ".portable" marker next to Update.exe, one level
    // above the app dir ("current\"). Installed layouts (%LocalAppData%\RORORO) and dev
    // builds (bin\...) don't have it.
    internal static bool IsPortableInstall(string baseDirectory)
    {
        try
        {
            var marker = System.IO.Path.Combine(baseDirectory, "..", ".portable");
            return System.IO.File.Exists(marker);
        }
        catch
        {
            return false;
        }
    }
}
