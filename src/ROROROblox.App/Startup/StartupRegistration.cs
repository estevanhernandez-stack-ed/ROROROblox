using Microsoft.Win32;

namespace ROROROblox.App.Startup;

/// <summary>
/// Run-on-login for unpackaged (direct-download) installs, via the per-user Run key. Packaged
/// installs use <see cref="PackagedStartupRegistration"/> instead — a packaged build's registry
/// writes land in the virtual hive, so this implementation would silently do nothing there and
/// then read its own virtual value back (App.xaml.cs:714-721).
///
/// <b>Two value names, one setting.</b> eb579ed (2026-05-06) renamed the value from
/// <c>ROROROblox</c> to <c>RORORO</c> and shipped no migration, which stranded everyone who had
/// enabled run-on-login before that date: <c>IsEnabled</c> read only the new name, so Settings
/// reported OFF while the old value kept launching the app at login, and <c>Disable</c> deleted
/// only the new name, so the toggle could not clear it either. The state was unreachable from
/// inside the app — found live 2026-09-09 on a maintainer's machine, launching a 1.22 build
/// underneath a 1.27 install.
///
/// The fix treats the legacy name as the same setting rather than sweeping it at startup.
/// Deleting a Run value the user never asked us to touch is a surprise, and a startup heuristic
/// ("does it point at the running exe?") gets it wrong for anyone deliberately keeping an older
/// build. Making the existing toggle honest is enough, and <c>ROROROblox</c> is our own former
/// name, so no other product's entry is at risk.
/// </summary>
internal sealed class StartupRegistration : IStartupRegistration
{
    private const string DefaultRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    internal const string ValueName = "RORORO";

    /// <summary>Pre-eb579ed (2026-05-06). Read and cleared, never written.</summary>
    internal const string LegacyValueName = "ROROROblox";

    private readonly string _runKeyPath;

    public StartupRegistration() : this(DefaultRunKeyPath)
    {
    }

    /// <summary>
    /// Seam for tests. The key path is injectable so the suite can exercise real registry
    /// behaviour against a scratch key — a test that wrote to the live Run key would install the
    /// very autostart entry this class exists to manage.
    /// </summary>
    internal StartupRegistration(string runKeyPath) => _runKeyPath = runKeyPath;

    /// <summary>True if EITHER name is present — a pre-rename value still launches the app.</summary>
    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: false);
        if (key is null) return false;
        return key.GetValue(ValueName) is not null || key.GetValue(LegacyValueName) is not null;
    }

    /// <summary>
    /// Writes the current name and clears the legacy one, so enabling collapses a stranded
    /// pre-rename user to a single entry instead of leaving the app launching twice.
    /// </summary>
    public void Enable()
    {
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine executable path.");
        using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(_runKeyPath);
        key.SetValue(ValueName, $"\"{exePath}\"");
        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
    }

    /// <summary>Clears both names. Off has to mean off, whichever era wrote the value.</summary>
    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true);
        if (key is null) return;
        key.DeleteValue(ValueName, throwOnMissingValue: false);
        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
    }
}
