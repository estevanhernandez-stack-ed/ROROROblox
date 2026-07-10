using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ROROROblox.Core;

/// <summary>
/// Production <see cref="IRobloxTrayLauncher"/>. Locates the newest RobloxPlayerBeta via
/// <see cref="RobloxInstallLocator"/> and starts it with <c>--launch-to-tray</c>.
///
/// <para>The launch mechanism is injected so tests can assert what would be launched without
/// starting a real process.</para>
/// </summary>
public sealed class RobloxTrayLauncher : IRobloxTrayLauncher
{
    private const string TrayArgument = "--launch-to-tray";

    private readonly Func<string?> _locate;
    private readonly Func<string, string, bool> _start;
    private readonly ILogger<RobloxTrayLauncher> _log;

    public RobloxTrayLauncher(
        ILogger<RobloxTrayLauncher>? log = null,
        Func<string?>? locate = null,
        Func<string, string, bool>? start = null)
    {
        _log = log ?? NullLogger<RobloxTrayLauncher>.Instance;
        _locate = locate ?? RobloxInstallLocator.FindNewestPlayerBeta;
        _start = start ?? StartDetached;
    }

    public bool RelaunchToTray()
    {
        string? exe;
        try { exe = _locate(); }
        catch (Exception ex) { _log.LogDebug(ex, "Tray relaunch: locating RobloxPlayerBeta threw."); return false; }

        if (string.IsNullOrEmpty(exe))
        {
            _log.LogDebug("Tray relaunch: no RobloxPlayerBeta install found; skipping.");
            return false;
        }

        try
        {
            var launched = _start(exe, TrayArgument);
            if (launched) _log.LogInformation("Tray relaunch: started {Exe} {Arg}.", exe, TrayArgument);
            return launched;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Tray relaunch: starting {Exe} failed.", exe);
            return false;
        }
    }

    private static bool StartDetached(string exe, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            UseShellExecute = false,
            WorkingDirectory = System.IO.Path.GetDirectoryName(exe) ?? string.Empty,
        };
        using var p = Process.Start(psi);
        return p is not null;
    }
}
