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
            return processes.Select(p =>
            {
                bool hasWindow;
                try { hasWindow = p.MainWindowHandle != IntPtr.Zero; }
                catch { hasWindow = false; } // exited mid-scan / access denied → treat as windowless
                return new RobloxProcessInfo(p.Id, hasWindow);
            }).ToArray();
        }
        finally
        {
            foreach (var p in processes)
            {
                p.Dispose();
            }
        }
    }

    public IReadOnlyList<int> GetRunningPlayerPids()
        => GetRunningPlayers().Select(p => p.Pid).ToArray();
}
