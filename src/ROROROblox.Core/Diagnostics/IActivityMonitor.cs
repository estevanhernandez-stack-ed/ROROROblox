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

    /// <summary>One sample tick: stamp the foreground account if input advanced, then evaluate thresholds.</summary>
    void Sample();

    IReadOnlyList<AccountActivity> GetSnapshot();
}
