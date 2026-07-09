using System;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.App.Plugins;

/// <summary>
/// Adapter over the Core <see cref="IActivityMonitor"/> that credits a plugin-supplied
/// account id as active. Mirrors <see cref="ActivitySnapshotProvider"/>'s adapter role,
/// but writes instead of reads: consumed by PluginHostService.MarkAccountActive.
/// </summary>
public sealed class AccountActivityMarker : IAccountActivityMarker
{
    private readonly IActivityMonitor _monitor;
    private readonly IClock _clock;

    public AccountActivityMarker(IActivityMonitor monitor, IClock clock)
    {
        _monitor = monitor;
        _clock = clock;
    }

    public void Mark(string accountId)
    {
        if (Guid.TryParse(accountId, out var id))
        {
            _monitor.MarkActive(id, _clock.UtcNow);
        }
        // Unparseable id → no-op (defensive; the plugin should send our stringified Guid).
    }
}
