using System.Collections.Generic;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.App.Plugins;

/// <summary>
/// Adapter over the Core <see cref="IActivityMonitor"/> that projects its snapshot into the
/// plugin-host-friendly <see cref="AccountActivitySnapshot"/> shape. Consumed by
/// PluginHostService.GetAccountActivity, mirroring how IRunningAccountsProvider feeds
/// GetRunningAccounts.
/// </summary>
public sealed class ActivitySnapshotProvider : IActivitySnapshotProvider
{
    private readonly IActivityMonitor _monitor;

    public ActivitySnapshotProvider(IActivityMonitor monitor) => _monitor = monitor;

    public IReadOnlyList<AccountActivitySnapshot> Snapshot()
    {
        var snap = _monitor.GetSnapshot();
        var list = new List<AccountActivitySnapshot>(snap.Count);
        foreach (var a in snap)
        {
            var seconds = (long)a.SinceActivity.TotalSeconds;
            if (seconds < 0) seconds = 0;
            list.Add(new AccountActivitySnapshot(
                a.AccountId.ToString(),
                a.LastActivityAt.ToUnixTimeMilliseconds(),
                seconds));
        }
        return list;
    }
}
