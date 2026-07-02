using System.Threading;

namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Runtime watcher for the multi-instance lock being held by someone else (the tray-resident
/// Roblox). Probes only while RoRoRo does NOT hold the mutex — once we hold it, nothing can take
/// it, so there is nothing to watch. Edge-triggered: ContestedChanged fires only on a transition.
/// Mirrors ActivityMonitor's discipline (injectable, Poll() test seam, Interlocked-guarded timer).
/// </summary>
public sealed class MutexContestedWatcher : IDisposable
{
    private const int IntervalMs = 5_000;

    private readonly IMutexHolder _mutex;
    private Timer? _timer;
    private bool _lastContested;
    private int _polling; // 0 idle, 1 running — skip overlap
    private bool _disposed;

    public event EventHandler<bool>? ContestedChanged;

    public MutexContestedWatcher(IMutexHolder mutex)
        => _mutex = mutex ?? throw new ArgumentNullException(nameof(mutex));

    public void Start()
    {
        if (_disposed) return;
        _timer ??= new Timer(_ => SafePoll(), null, IntervalMs, IntervalMs);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>One probe tick. Contested = we don't hold the lock AND someone else does.</summary>
    public void Poll()
    {
        var contested = !_mutex.IsHeld && _mutex.IsHeldElsewhere();
        if (contested != _lastContested)
        {
            _lastContested = contested;
            ContestedChanged?.Invoke(this, contested);
        }
    }

    private void SafePoll()
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1) return;
        try { Poll(); }
        catch { /* never let a probe tick crash the timer thread */ }
        finally { Interlocked.Exchange(ref _polling, 0); }
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }
}
