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

    public IReadOnlyList<int> GetRunningPlayerPids()
    {
        var processes = Process.GetProcessesByName(PlayerProcessName);
        try
        {
            return processes.Select(p => p.Id).ToArray();
        }
        finally
        {
            foreach (var p in processes)
            {
                p.Dispose();
            }
        }
    }
}
