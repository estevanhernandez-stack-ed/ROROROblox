using System;

namespace ROROROblox.Core.Diagnostics;

public interface IMemoryWatchdog
{
    long CapBytes { get; set; }
    long ReserveBytes { get; set; }
    int ProjectionWarnMinutes { get; set; }

    /// <summary>Coalesced, edge-triggered — the accounts that newly crossed a trigger this sample.</summary>
    event EventHandler<MemoryPressureSnapshot>? PressureCrossed;

    void OnAccountLaunched(Guid accountId, int pid);
    void OnAccountExited(Guid accountId);
    void ResetBaseline(Guid accountId, int pid);
    void Start();
    void Stop();
    void Sample();
    MemoryPressureSnapshot GetSnapshot();
}
