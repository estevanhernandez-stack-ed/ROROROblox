using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ROROROblox.Core;

/// <summary>
/// Answers one question before a launch: can Windows open a <c>roblox-player:</c> link at all?
/// </summary>
/// <remarks>
/// <para>
/// A launch shell-executes that link. When nothing handles the scheme, <c>Process.Start</c> does
/// not throw: it returns no process and Windows opens its own "You'll need a new app to open this
/// link" dialog (measured on Windows 11, 2026-11-10). The launcher then reported the launch as
/// started. The Microsoft Store edition of Roblox is the common cause, because its package
/// declares the <c>roblox</c> protocol and never <c>roblox-player</c>.
/// </para>
/// <para>
/// <b>The answer is biased on purpose.</b> Saying "missing" when a handler exists would stop a
/// working install from launching, which is far worse than the dead end this prevents. So the
/// probe says "missing" only when every lookup ran and every lookup said no. A lookup that throws
/// counts as "present", and so does an empty lookup list.
/// </para>
/// </remarks>
public interface IRobloxLaunchHandlerProbe
{
    /// <summary>False only when no handler for the launch scheme could be found by any lookup.</summary>
    bool IsRegistered();
}

/// <inheritdoc cref="IRobloxLaunchHandlerProbe"/>
public sealed class RobloxLaunchHandlerProbe : IRobloxLaunchHandlerProbe
{
    /// <summary>The scheme every launch link starts with. See <see cref="RobloxLauncher.BuildLaunchUri"/>.</summary>
    public const string Scheme = "roblox-player";

    private static readonly Func<string, bool>[] RealLookups = [ClassesRootHasACommand, ShellKnowsAHandler];

    private readonly IReadOnlyList<Func<string, bool>> _lookups;

    public RobloxLaunchHandlerProbe() : this(RealLookups)
    {
    }

    /// <summary>Test seam: each lookup takes a scheme and says whether it found a handler.</summary>
    public RobloxLaunchHandlerProbe(IReadOnlyList<Func<string, bool>> lookups)
    {
        _lookups = lookups ?? throw new ArgumentNullException(nameof(lookups));
    }

    public bool IsRegistered() => AnyLookupFinds(Scheme, _lookups);

    /// <summary>The real lookups against this machine, for any scheme. Used by tests and diagnostics.</summary>
    public static bool IsSchemeRegistered(string scheme) => AnyLookupFinds(scheme, RealLookups);

    private static bool AnyLookupFinds(string scheme, IReadOnlyList<Func<string, bool>> lookups)
    {
        if (lookups.Count == 0)
        {
            return true; // nothing checked is nothing known
        }

        foreach (var lookup in lookups)
        {
            try
            {
                if (lookup(scheme))
                {
                    return true;
                }
            }
            catch
            {
                return true; // an unreadable answer is never "missing"
            }
        }

        return false;
    }

    /// <summary>
    /// The classic registration: <c>HKEY_CLASSES_ROOT\&lt;scheme&gt;\shell\open\command</c> with a
    /// command in it. HKCR is the merged per-user and per-machine view, so a Roblox installed for
    /// one user and one installed for all users both show here.
    /// </summary>
    private static bool ClassesRootHasACommand(string scheme)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(scheme + @"\shell\open\command");
        return key?.GetValue(null) is string command && !string.IsNullOrWhiteSpace(command);
    }

    /// <summary>
    /// What the shell itself resolves, which also covers a handler registered by a packaged app
    /// or chosen through Windows' default-apps settings. Those never appear under HKCR.
    /// </summary>
    private static bool ShellKnowsAHandler(string scheme)
    {
        foreach (var what in new[] { AssocStrCommand, AssocStrExecutable, AssocStrFriendlyAppName, AssocStrAppId })
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            var hr = AssocQueryStringW(AssocFInitIgnoreUnknown, what, scheme, null, buffer, ref size);
            if ((hr == SOk && buffer.Length > 0) || hr == SFalse || hr == EPointer)
            {
                // S_FALSE and E_POINTER both mean an answer exists that did not fit the buffer.
                return true;
            }
        }

        return false;
    }

    // ASSOCF_INIT_IGNOREUNKNOWN: without it an unregistered name can resolve to the "Unknown"
    // class and report Windows' own picker as the handler.
    private const uint AssocFInitIgnoreUnknown = 0x00000400;
    private const int AssocStrCommand = 1;
    private const int AssocStrExecutable = 2;
    private const int AssocStrFriendlyAppName = 4;
    private const int AssocStrAppId = 21;
    private const int SOk = 0;
    private const int SFalse = 1;
    private const int EPointer = unchecked((int)0x80004003);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int AssocQueryStringW(
        uint flags, int str, string pszAssoc, string? pszExtra, StringBuilder? pszOut, ref uint pcchOut);
}
