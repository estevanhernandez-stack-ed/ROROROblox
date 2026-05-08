using Microsoft.Win32;

namespace ROROROblox.Core;

/// <summary>
/// Reads <c>HKCU\Software\Classes\roblox-player\shell\open\command</c> default value
/// and returns true when the path string contains <c>Bloxstrap</c> (case-insensitive).
/// Bloxstrap's binaries are consistently named <c>Bloxstrap.exe</c> /
/// <c>BloxstrapBootstrapper.exe</c>, so the substring match is sufficient and simple.
/// </summary>
public sealed class BloxstrapDetector : IBloxstrapDetector
{
    private const string SubKey = @"Software\Classes\roblox-player\shell\open\command";

    public bool IsBloxstrapHandler()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SubKey);
            var command = key?.GetValue(null) as string;
            return LooksLikeBloxstrap(command);
        }
        catch
        {
            // Registry inaccessible (sandboxed test runner, locked-down PC) — pretend Bloxstrap
            // isn't there. The warning is comfort, not load-bearing.
            return false;
        }
    }

    /// <summary>
    /// Pure function for testing — given a registry-stored command line string,
    /// decide if it points at a Bloxstrap binary.
    /// </summary>
    public static bool LooksLikeBloxstrap(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        return command.IndexOf("Bloxstrap", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
