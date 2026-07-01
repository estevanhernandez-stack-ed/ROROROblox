using System;
using System.Collections.Generic;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.App.ViewModels;

/// <summary>Projects an ActivityMonitor snapshot onto the visible account rows. Pure.</summary>
public static class ActivitySnapshotApplier
{
    public static void Apply(
        IEnumerable<AccountSummary> rows,
        IReadOnlyList<AccountActivity> snapshot,
        TimeSpan warnThreshold)
    {
        var byId = new Dictionary<Guid, AccountActivity>(snapshot.Count);
        foreach (var a in snapshot) byId[a.AccountId] = a;

        foreach (var row in rows)
        {
            if (byId.TryGetValue(row.Id, out var a))
            {
                row.SinceActivity = a.SinceActivity;
                row.IdleWarn = a.SinceActivity >= warnThreshold;
            }
            else
            {
                row.SinceActivity = null;
                row.IdleWarn = false;
            }
        }
    }
}
