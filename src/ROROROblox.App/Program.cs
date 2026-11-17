using System;
using ROROROblox.Core;
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

        // A dev build can be pointed at another data folder (RORORO_DATA_DIR). This MUST run before
        // anything resolves a RoRoRo path, and the first reader is UiCulture.ApplyFromSettings below
        // (it reads settings.json), so it sits here, straight after Velopack and before everything
        // else. Shipped builds (MSIX-packaged, or a Velopack layout) ignore the variable. It does not
        // log: logging is not up yet, so the outcome waits in AppDataRoot.Decision for the startup
        // log lines. A source fence (ProgramDataRootCallOrderTests) pins this position.
        AppDataRoot.Configure(
            isPackaged: new Distribution.Win32DistributionMode().IsPackaged,
            isVelopackInstalled: IsVelopackInstalledLayout(AppContext.BaseDirectory),
            rawValue: Environment.GetEnvironmentVariable(AppDataRoot.EnvironmentVariableName));

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

        // Apply the saved UI language BEFORE any WPF window/resource loads (localization,
        // 2026-09-07). The XAML binds its strings once via x:Static, so the culture must be set
        // here — earlier than the theme, which OnStartup applies. Best-effort, never throws.
        Localization.UiCulture.ApplyFromSettings();

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    // A Velopack-managed layout (installed "current\" or portable) has Update.exe one level above
    // the app folder; a dev build under bin\ does not. Same probe shape as IsPortableInstall.
    // Failure reads as "not installed": a path that cannot be probed has no Update.exe to find.
    internal static bool IsVelopackInstalledLayout(string baseDirectory)
    {
        try
        {
            return System.IO.File.Exists(System.IO.Path.Combine(baseDirectory, "..", "Update.exe"));
        }
        catch
        {
            return false;
        }
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
