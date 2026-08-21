using System;

namespace ROROROblox.Core.Diagnostics;

public interface IMemoryWatchdog
{
    /// <summary>
    /// What one client costs on THIS machine, learned from settled samples (F-083). Falls back to
    /// the measured-elsewhere seed until there is enough evidence.
    /// <para>
    /// Exposed here because the watchdog is the only thing already sampling every client every 30
    /// seconds — it had this machine's real answer in hand and nothing was reading it. **For the
    /// launch advisor only.** Feeding it to the anomaly cap would let a heavy machine teach itself
    /// that heavy is normal, which is F-082 by a new route, and the headroom trigger reads free
    /// memory directly, so replacing a measurement with an estimate there is a downgrade.
    /// </para>
    /// </summary>
    int ExpectedClientMb { get; }

    long CapBytes { get; set; }
    long ReserveBytes { get; set; }
    int ProjectionWarnMinutes { get; set; }

    /// <summary>Coalesced, edge-triggered — the accounts that newly crossed a trigger this sample.</summary>
    event EventHandler<MemoryPressureSnapshot>? PressureCrossed;

    void OnAccountLaunched(Guid accountId, int pid);

    /// <summary>
    /// Drops the tracked record for <paramref name="accountId"/> ONLY when its current record is
    /// still pinned to <paramref name="pid"/> (final-branch review IMPORTANT 5). Exit callbacks
    /// arrive from <c>Process.Exited</c> on unordered threadpool threads; a Recycle replaces the
    /// record twice in quick succession (stop old pid, launch new pid), so a delayed exit callback
    /// for the SUPERSEDED pid must not remove the fresh record — that would silently blind the
    /// watchdog for the account for the rest of the session. Symmetric with
    /// <c>RobloxWindowDecorator.Untrack(pid)</c>, called alongside this at the same call site.
    /// </summary>
    void OnAccountExited(Guid accountId, int pid);
    void ResetBaseline(Guid accountId, int pid);
    void Start();
    void Stop();
    void Sample();
    MemoryPressureSnapshot GetSnapshot();
}
