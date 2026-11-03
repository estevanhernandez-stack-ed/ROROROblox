using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.App.Tray;

/// <summary>
/// Ends idle Roblox tray clients that outlive a launch (#222): a windowless <c>--launch-to-tray</c>
/// client RoRoRo is not tracking, seen left running about 40 minutes after an "Open Roblox home"
/// launch. The pre-launch clear (#241) only fires when another launch happens; this covers the gap.
/// <para>
/// Its own <see cref="PeriodicTimer"/>: nothing existing touches strays. <see cref="OrphanedClientSweeper"/>
/// ticks every 5 s and adopts, it never kills. The selection rule is the shared, fail-closed
/// <c>TrayClient.IsIdleTrayClient</c> with a <see cref="MinAge"/> floor, so a client still creating
/// its window, a windowed game, a tracked client, an unreadable command line and an unknown start
/// time are all left alone.
/// </para>
/// <para>
/// Not racing the pre-launch clear: both call <c>StopIdleTrayClients</c>, whose kill tolerates an
/// already-exited process (a failed kill is logged and the exit wait returns at once for a gone pid).
/// The sweep never throws out of the loop.
/// </para>
/// </summary>
internal sealed class IdleTrayClientSweeper : IDisposable
{
    internal static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan MinAge = TimeSpan.FromMinutes(2);

    private readonly IRobloxInstanceStopper _stopper;
    private readonly Func<IReadOnlySet<int>> _trackedPids;
    private readonly TimeSpan _interval;
    private readonly ILogger<IdleTrayClientSweeper> _log;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private int _sweeping;
    private bool _disposed;

    /// <param name="trackedPids">Pids RoRoRo owns right now, read off the UI thread (the process tracker hands back a copy).</param>
    /// <param name="interval">Test seam; production uses <see cref="SweepInterval"/>.</param>
    public IdleTrayClientSweeper(
        IRobloxInstanceStopper stopper,
        Func<IReadOnlySet<int>> trackedPids,
        TimeSpan? interval = null,
        ILogger<IdleTrayClientSweeper>? log = null)
    {
        _stopper = stopper ?? throw new ArgumentNullException(nameof(stopper));
        _trackedPids = trackedPids ?? throw new ArgumentNullException(nameof(trackedPids));
        _interval = interval ?? SweepInterval;
        _log = log ?? NullLogger<IdleTrayClientSweeper>.Instance;
    }

    public void Start()
    {
        if (_disposed || _loop is not null) return;
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                SweepOnce();
            }
        }
        catch (OperationCanceledException) { /* disposed */ }
    }

    /// <summary>Internal seam so one tick can be driven deterministically in tests.</summary>
    internal int SweepOnce()
    {
        if (Interlocked.Exchange(ref _sweeping, 1) == 1) return 0;
        try
        {
            var tracked = _trackedPids();
            return _stopper.StopIdleTrayClients(tracked.Contains, MinAge);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Idle tray client sweep threw; will retry next tick.");
            return 0;
        }
        finally { Interlocked.Exchange(ref _sweeping, 0); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts.Cancel(); } catch { /* already disposed */ }
        _cts.Dispose();
    }
}
