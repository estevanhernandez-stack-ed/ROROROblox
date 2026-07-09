using System;
using System.Collections.Generic;

namespace ROROROblox.Core.Diagnostics;

public interface IActivityMonitor
{
    /// <summary>Warn line; default 15 minutes. Set from settings at composition.</summary>
    TimeSpan WarnThreshold { get; set; }

    /// <summary>Coalesced, edge-triggered: the accounts that newly crossed the warn line this sample.</summary>
    event EventHandler<IReadOnlyList<Guid>>? WarnThresholdCrossed;

    void OnAccountLaunched(Guid accountId);
    void OnAccountExited(Guid accountId);

    /// <summary>
    /// Start the 1s sample timer (2s initial delay). Idempotent -- a second call while already
    /// running is a no-op. No-op after <see cref="IDisposable.Dispose"/>.
    /// </summary>
    void Start();

    /// <summary>Stop the sample timer. Safe to call when not running, or repeatedly.</summary>
    void Stop();

    /// <summary>
    /// One sample tick: stamp the foreground account if input advanced since the last observed tick,
    /// then evaluate thresholds. The input baseline is captured at construction (not on the first call),
    /// so even the very first <see cref="Sample"/> only stamps when input has genuinely moved since the
    /// monitor was built -- an idle user never gets falsely marked active on cold start.
    /// </summary>
    void Sample();

    /// <summary>
    /// Directly credit an account as active as of <paramref name="nowUtc"/> — the path a
    /// keep-alive plugin uses after it synthesizes input into that account's window (the
    /// foreground/global-input heuristic in Sample() can't attribute plugin-directed input to
    /// the right window). Stamps LastActivityAt and re-arms the warn latch. No-op for an
    /// untracked account. The core never infers this — the plugin declares it via the
    /// consent-gated MarkAccountActive RPC.
    /// </summary>
    void MarkActive(Guid accountId, DateTimeOffset nowUtc);

    IReadOnlyList<AccountActivity> GetSnapshot();
}
