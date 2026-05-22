using Microsoft.Win32;

namespace ROROROblox.Core;

/// <summary>
/// Reads <c>HKCU\Software\Classes\roblox-player\shell\open\command</c> default value and
/// classifies the registered <c>roblox-player:</c> protocol handler.
/// <para>
/// Two consumers:
/// <list type="bullet">
/// <item><see cref="IsBloxstrapHandler"/> — Bloxstrap-specific FFlag-override warning banner
/// (spec §5.2). Stays Bloxstrap-only on purpose; the banner copy is about Bloxstrap rewriting
/// our FFlag write.</item>
/// <item><see cref="IsStrapHandlingLaunches"/> — true when EITHER Bloxstrap or Fishstrap owns
/// the handler. A strap updates Roblox proactively before <c>RobloxPlayerBeta</c> runs, so
/// RoRoRo's pre-warm / version pre-check should be skipped to avoid a double-update
/// (install-deferral spec, Riders §7). Detect only — we never co-drive the strap's mutex.</item>
/// </list>
/// </para>
/// <para>
/// Bloxstrap and Fishstrap binaries are consistently named <c>Bloxstrap.exe</c> /
/// <c>BloxstrapBootstrapper.exe</c> and <c>Fishstrap.exe</c> / <c>FishstrapBootstrapper.exe</c>,
/// so a case-insensitive substring match on the handler command is sufficient and simple.
/// </para>
/// </summary>
public sealed class BloxstrapDetector : IBloxstrapDetector
{
    private const string SubKey = @"Software\Classes\roblox-player\shell\open\command";

    /// <summary>
    /// Seam returning the registered <c>roblox-player:</c> handler command string (the default
    /// value of the registry key), or <c>null</c> when absent. Injectable so tests never touch
    /// the real registry; a throwing reader is treated the same as "no handler" (degrade-safe).
    /// </summary>
    private readonly Func<string?> _handlerCommandReader;

    public BloxstrapDetector() : this(ReadHandlerCommandFromRegistry)
    {
    }

    /// <summary>Test seam — supply a reader that returns the handler command string.</summary>
    public BloxstrapDetector(Func<string?> handlerCommandReader)
    {
        _handlerCommandReader = handlerCommandReader ?? throw new ArgumentNullException(nameof(handlerCommandReader));
    }

    /// <summary>
    /// True when Bloxstrap is the registered <c>roblox-player:</c> handler. Drives the
    /// Bloxstrap-specific FFlag-override warning banner — intentionally NOT widened to Fishstrap.
    /// </summary>
    public bool IsBloxstrapHandler() => LooksLikeBloxstrap(ReadHandlerCommandSafe());

    /// <summary>
    /// True when EITHER Bloxstrap or Fishstrap is the registered <c>roblox-player:</c> handler,
    /// meaning a strap intercepts launches and updates Roblox proactively itself — so RoRoRo's
    /// pre-warm / version pre-check should be skipped. Degrades to <c>false</c> when the handler
    /// is absent, empty, or unreadable.
    /// </summary>
    public bool IsStrapHandlingLaunches() => LooksLikeStrap(ReadHandlerCommandSafe());

    /// <summary>
    /// Reads the handler command via the seam, swallowing any reader exception so callers never
    /// have to guard the registry read. An unreadable handler is "no strap is here."
    /// </summary>
    private string? ReadHandlerCommandSafe()
    {
        try
        {
            return _handlerCommandReader();
        }
        catch
        {
            // Registry inaccessible (sandboxed test runner, locked-down PC) — pretend nothing is
            // there. The skip/warn it drives is comfort, not load-bearing.
            return null;
        }
    }

    private static string? ReadHandlerCommandFromRegistry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SubKey);
        return key?.GetValue(null) as string;
    }

    /// <summary>
    /// Pure function for testing — given a registry-stored command line string,
    /// decide if it points at a Bloxstrap binary.
    /// </summary>
    public static bool LooksLikeBloxstrap(string? command)
        => ContainsToken(command, "Bloxstrap");

    /// <summary>
    /// Pure function for testing — given a registry-stored command line string,
    /// decide if it points at a Fishstrap binary.
    /// </summary>
    public static bool LooksLikeFishstrap(string? command)
        => ContainsToken(command, "Fishstrap");

    /// <summary>
    /// Pure function for testing — true when the command points at EITHER strap. Vanilla Roblox
    /// launchers (<c>RobloxPlayerBeta</c> / <c>RobloxPlayerLauncher</c>) read false.
    /// </summary>
    public static bool LooksLikeStrap(string? command)
        => LooksLikeBloxstrap(command) || LooksLikeFishstrap(command);

    private static bool ContainsToken(string? command, string token)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        return command.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
