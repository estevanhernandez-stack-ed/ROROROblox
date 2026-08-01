using System.Diagnostics;

namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Default <see cref="IRobloxRunningProbe"/> implementation — thin wrapper over
/// <see cref="Process.GetProcessesByName(string)"/>. Disposes every returned <see cref="Process"/>
/// handle so we don't leak system handles on hot startup paths (the same anti-pattern that bit
/// <c>RobloxProcessTracker</c> historically). Cycle 4 (2026-05-08).
/// </summary>
public sealed class RobloxRunningProbe : IRobloxRunningProbe
{
    private const string PlayerProcessName = "RobloxPlayerBeta";

    public IReadOnlyList<RobloxProcessInfo> GetRunningPlayers()
    {
        var processes = Process.GetProcessesByName(PlayerProcessName);
        try
        {
            return processes
                .Select(p => new RobloxProcessInfo(p.Id, ReadHasWindow(() => p.MainWindowHandle)))
                .ToArray();
        }
        finally
        {
            foreach (var p in processes)
            {
                p.Dispose();
            }
        }
    }

    /// <summary>
    /// Classify one process as windowed or windowless from its main-window handle.
    ///
    /// <para><b>Fails CLOSED.</b> A read we cannot complete — exited mid-scan, access denied across
    /// an integrity boundary, any transient failure — reports <c>true</c> (windowed). This flag is
    /// not cosmetic: <see cref="SeamlessTakeover.WindowlessOnly"/> closes every client it is handed
    /// <b>with no confirming modal</b>, so "windowless" is the destructive answer and must never be
    /// the one we guess. An unknown process may be mid-game with unsaved progress; treating it as
    /// windowed costs at worst an unnecessary confirmation prompt, while the reverse costs the user
    /// their session.</para>
    ///
    /// <para>Public so the failure branch can be unit tested — <see cref="Process.MainWindowHandle"/>
    /// cannot be provoked into throwing from a test against a real process, which is why this
    /// enumeration was previously covered only by manual smoke and the inverted default survived.</para>
    /// </summary>
    public static bool ReadHasWindow(Func<IntPtr> readMainWindowHandle)
    {
        try { return readMainWindowHandle() != IntPtr.Zero; }
        catch { return true; } // unknown → assume windowed; never silently close what we couldn't read
    }

    public IReadOnlyList<int> GetRunningPlayerPids()
        => GetRunningPlayers().Select(p => p.Pid).ToArray();
}
