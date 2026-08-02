using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Stop one account's client and relaunch it into the SAME target. Process exit is the only
/// guaranteed reclaim of Roblox's leaked memory on Windows, so this is the actual remedy the
/// watchdog's warning points at. Extracted from the ViewModel so the ordering is testable.
/// </summary>
public sealed class AccountRecycler
{
    public delegate Task<int> LaunchDelegate(Guid accountId, LaunchTarget target, CancellationToken ct);

    private readonly IRobloxInstanceStopper _stopper;
    private readonly LaunchDelegate _launch;
    private readonly IMemoryWatchdog _watchdog;
    private readonly ILogger _log;

    public AccountRecycler(
        IRobloxInstanceStopper stopper,
        LaunchDelegate launch,
        IMemoryWatchdog watchdog,
        ILogger? log = null)
    {
        _stopper = stopper ?? throw new ArgumentNullException(nameof(stopper));
        _launch = launch ?? throw new ArgumentNullException(nameof(launch));
        _watchdog = watchdog ?? throw new ArgumentNullException(nameof(watchdog));
        _log = log ?? NullLogger.Instance;
    }

    public async Task<bool> RecycleAsync(Guid accountId, LaunchTarget target, CancellationToken ct = default)
    {
        // target.GetType().Name, never {Target} itself: LaunchTarget.PrivateServer is a positional
        // record carrying a joinable-server Code, and Serilog's default (non-@) rendering calls the
        // compiler-generated record ToString(), which would write the code verbatim into
        // rororoblox-.log — a log file DiagnosticsWindow packs into user-shared support bundles.
        _log.LogInformation("Recycling account {AccountId} into {Target} (user-initiated).", accountId, target.GetType().Name);
        _stopper.StopAccount(accountId);

        var pid = await _launch(accountId, target, ct).ConfigureAwait(false);
        if (pid <= 0)
        {
            _log.LogWarning("Recycle of account {AccountId} failed: relaunch produced no process.", accountId);
            // Deliberately do NOT reset the baseline — a baseline pointing at a dead pid would
            // silently blind the watchdog for this account.
            return false;
        }

        _watchdog.ResetBaseline(accountId, pid);
        _log.LogInformation("Recycled account {AccountId}; new pid {Pid}.", accountId, pid);
        return true;
    }
}
