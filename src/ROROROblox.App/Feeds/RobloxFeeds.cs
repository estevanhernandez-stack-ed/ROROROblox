using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
using ROROROblox.Core.Feeds;

namespace ROROROblox.App.Feeds;

/// <summary>
/// Decides when the app asks Roblox's client-version endpoint and Roblox's status page, and holds the two
/// answers. Singleton. It is the only caller of the status client and the only idle caller of the update
/// probe, so "nothing at startup, nothing in the tray, one request for the whole app" is one class.
/// <para>
/// Its own 30-second <see cref="TimeProvider"/> timer, not the view model's periodic tick, so a fake clock
/// can drive it. <see cref="Start"/> reads nothing; the first read is no sooner than the settle period
/// after the window is first visible. Each lane is single flight. <see cref="Changed"/> may fire on any
/// thread and never throws into the timer.
/// </para>
/// <para>
/// A failed update read is <see cref="RobloxUpdateKind.Unknown"/> (the notice goes away). A failed status
/// read changes nothing until the last good answer is older than the trust window, then the reading is
/// <see cref="RobloxStatusReading.None"/>, evaluated on every tick including while the window is hidden.
/// Debug lines carry enums and codes only; nothing Status.io sends is logged.
/// </para>
/// </summary>
internal sealed class RobloxFeeds : IDisposable
{
    private readonly Func<IRobloxUpdateProbe> _probeFactory;
    private readonly Func<IRobloxStatusApi> _statusFactory;
    private readonly IBloxstrapDetector _straps;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly FeedOptions _options;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _updateLane = new(1, 1);
    private readonly SemaphoreSlim _statusLane = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private readonly HashSet<RobloxStatusRow> _warnedMissing = [];

    private RobloxUpdateState _update = RobloxUpdateState.Unknown;
    private RobloxStatusReading _status = RobloxStatusReading.None;
    private DateTimeOffset? _firstVisibleAt;
    private bool _visible;
    private bool _statusEnabled = true;
    private DateTimeOffset? _statusEnabledAt;
    private DateTimeOffset? _updateLastAttempt;
    private DateTimeOffset? _statusLastAttempt;
    private bool _statusLastRefused;
    private bool _statusEarlyRequested;
    private DateTimeOffset? _statusLastGoodAt;
    private ITimer? _timer;
    private bool _started;
    private bool _disposed;
    private int _passesPending;

    public RobloxFeeds(
        Func<IRobloxUpdateProbe> probeFactory,
        Func<IRobloxStatusApi> statusFactory,
        IBloxstrapDetector straps,
        TimeProvider? time = null,
        ILogger<RobloxFeeds>? log = null,
        FeedOptions? options = null)
    {
        _probeFactory = probeFactory ?? throw new ArgumentNullException(nameof(probeFactory));
        _statusFactory = statusFactory ?? throw new ArgumentNullException(nameof(statusFactory));
        _straps = straps ?? throw new ArgumentNullException(nameof(straps));
        _time = time ?? TimeProvider.System;
        _log = (ILogger?)log ?? NullLogger.Instance;
        _options = options ?? FeedOptions.Default;
    }

    /// <summary>Raised after <see cref="Update"/> or <see cref="Status"/> changes, on whichever thread caused it.</summary>
    public event EventHandler? Changed;

    /// <summary>Unknown until an answer; Unknown again after a failed read or under a strap.</summary>
    public RobloxUpdateState Update
    {
        get { lock (_gate) { return _update; } }
    }

    /// <summary>None when off, never read, or older than the trust window.</summary>
    public RobloxStatusReading Status
    {
        get { lock (_gate) { return _status; } }
    }

    /// <summary>First time the window was visible plus the settle period; null until it has been visible.</summary>
    public DateTimeOffset? ReadsAllowedFrom
    {
        get { lock (_gate) { return _firstVisibleAt + _options.SettleAfterShown; } }
    }

    /// <summary>Creates the timer. Reads nothing, and a second call does nothing.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed)
            {
                return;
            }

            _started = true;
            _timer = _time.CreateTimer(_ => Kick(), state: null, dueTime: _options.Tick, period: _options.Tick);
        }
    }

    /// <summary>
    /// The main window is shown and not minimised. Pushed from the window; a pass runs at once when it
    /// becomes visible rather than waiting for the tick. Safe from any thread.
    /// </summary>
    public void SetWindowVisible(bool visible)
    {
        bool run;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (visible)
            {
                _firstVisibleAt ??= _time.GetUtcNow();
            }

            run = visible && !_visible && _started;
            _visible = visible;
        }

        if (run)
        {
            Kick();
        }
    }

    /// <summary>The off switch. Off clears the status reading at once; on restarts the status settle.</summary>
    public void SetStatusEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_disposed || _statusEnabled == enabled)
            {
                return;
            }

            _statusEnabled = enabled;
            if (enabled)
            {
                _statusEnabledAt = _time.GetUtcNow();
                _statusLastAttempt = null;
                _statusLastRefused = false;
            }
            else
            {
                _statusEarlyRequested = false;
                _statusLastGoodAt = null;
            }
        }

        if (!enabled)
        {
            SetStatus(RobloxStatusReading.None);
        }
    }

    /// <summary>
    /// A Roblox client attached. While an update is pending this re-compares two local file versions to the
    /// endpoint answer already in hand: no request, and it runs whether or not the window is visible. In any
    /// other state it does nothing. Never throws.
    /// </summary>
    public void NoteClientAttached() => RecompareIfPending();

    /// <summary>A launch failed for a connection reason: allow one status read ahead of the schedule.</summary>
    public void RequestEarlyStatusRead()
    {
        lock (_gate)
        {
            if (_disposed || !_statusEnabled)
            {
                return;
            }

            _statusEarlyRequested = true;
        }
    }

    /// <summary>What the batch gate saw, so the coordinator holds the endpoint's answer even when the gate asked first.</summary>
    public void ReportUpdateState(RobloxUpdateState state) => SetUpdate(state ?? RobloxUpdateState.Unknown);

    /// <summary>The timer body. Internal so a test awaits it. Never throws.</summary>
    internal async Task TickAsync()
    {
        try
        {
            DateTimeOffset now;
            lock (_gate)
            {
                if (!_started || _disposed)
                {
                    return;
                }

                now = _time.GetUtcNow();
            }

            var strap = ReadStrap();
            if (strap)
            {
                SetUpdate(RobloxUpdateState.Unknown);
            }
            else
            {
                RecompareIfPending();
            }

            ExpireStatus(now);

            var due = FeedSchedule.Decide(now, BuildClock(strap), _options);
            var update = due.HasFlag(FeedDue.Update) ? RunUpdateLaneAsync(strap) : Task.CompletedTask;
            var status = due.HasFlag(FeedDue.Status) ? RunStatusLaneAsync(strap) : Task.CompletedTask;
            await Task.WhenAll(update, status).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "roblox-feeds pass threw; ignoring.");
        }
    }

    /// <summary>Completes when no fire-and-forget pass is running. For tests.</summary>
    internal async Task WhenIdleAsync()
    {
        for (var spins = 0; Volatile.Read(ref _passesPending) > 0; spins++)
        {
            if (spins < 200)
            {
                await Task.Yield();
            }
            else
            {
                await Task.Delay(1).ConfigureAwait(false);
            }
        }
    }

    private void Kick()
    {
        Interlocked.Increment(ref _passesPending);
        _ = Task.Run(async () =>
        {
            try
            {
                await TickAsync().ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _passesPending);
            }
        });
    }

    private bool ReadStrap()
    {
        try
        {
            return _straps.IsStrapHandlingLaunches();
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "roblox-feeds: the strap check threw; treating it as no strap.");
            return false;
        }
    }

    private FeedClock BuildClock(bool strap)
    {
        lock (_gate)
        {
            return new FeedClock(
                _firstVisibleAt, _visible, strap, _statusEnabled,
                _updateLastAttempt, _statusLastAttempt, _statusLastRefused, _statusEarlyRequested,
                _statusEnabledAt);
        }
    }

    private void RecompareIfPending()
    {
        try
        {
            var current = Update;
            if (current.Kind != RobloxUpdateKind.Pending || current.Latest is null)
            {
                return;
            }

            SetUpdate(_probeFactory().CompareInstalledTo(current.Latest));
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "roblox-feeds: the local version re-read threw; keeping the state.");
        }
    }

    private void ExpireStatus(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_statusLastGoodAt is not { } good || now < good || now - good < _options.StatusTrust)
            {
                return;
            }

            _statusLastGoodAt = null;
        }

        SetStatus(RobloxStatusReading.None);
    }

    private async Task RunUpdateLaneAsync(bool strap)
    {
        if (!await _updateLane.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                var now = _time.GetUtcNow();
                if (_disposed || !FeedSchedule.Decide(now, BuildClockLocked(strap), _options).HasFlag(FeedDue.Update))
                {
                    return;
                }

                _updateLastAttempt = now;
            }

            RobloxUpdateState state;
            try
            {
                var probe = _probeFactory();
                state = await Task.Run(() => probe.GetUpdateStateAsync(_stopping.Token)).ConfigureAwait(false)
                    ?? RobloxUpdateState.Unknown;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "roblox-feeds: the update read threw.");
                state = RobloxUpdateState.Unknown;
            }

            _log.LogDebug("roblox-update read: {Kind} {Direction}", state.Kind, state.Direction);
            SetUpdate(state);
        }
        finally
        {
            _updateLane.Release();
        }
    }

    private async Task RunStatusLaneAsync(bool strap)
    {
        if (!await _statusLane.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                var now = _time.GetUtcNow();
                if (_disposed || !FeedSchedule.Decide(now, BuildClockLocked(strap), _options).HasFlag(FeedDue.Status))
                {
                    return;
                }

                _statusLastAttempt = now;
                _statusEarlyRequested = false;
            }

            FeedOutcome outcome;
            RobloxStatusReading? reading = null;
            try
            {
                var api = _statusFactory();
                var read = await Task.Run(() => api.ReadAsync(_stopping.Token)).ConfigureAwait(false);
                outcome = read.Outcome;
                if (outcome == FeedOutcome.Ok)
                {
                    if (read.Snapshot is null)
                    {
                        outcome = FeedOutcome.Shape;
                    }
                    else
                    {
                        reading = RobloxStatusEvaluator.Evaluate(read.Snapshot, RobloxStatusWatch.Roblox, WarnMissingOnce);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "roblox-feeds: the status read threw.");
                outcome = FeedOutcome.Network;
            }

            _log.LogDebug("roblox-status read: {Outcome}", outcome);

            lock (_gate)
            {
                _statusLastRefused = outcome == FeedOutcome.Refused;
                if (_disposed || !_statusEnabled)
                {
                    // Switched off while the read was in flight: the answer is dropped.
                    return;
                }

                if (reading is not null)
                {
                    _statusLastGoodAt = _time.GetUtcNow();
                }
            }

            if (reading is not null)
            {
                SetStatus(reading);
            }
        }
        finally
        {
            _statusLane.Release();
        }
    }

    // Caller holds the gate.
    private FeedClock BuildClockLocked(bool strap) => new(
        _firstVisibleAt, _visible, strap, _statusEnabled,
        _updateLastAttempt, _statusLastAttempt, _statusLastRefused, _statusEarlyRequested,
        _statusEnabledAt);

    private void WarnMissingOnce(RobloxStatusRow row)
    {
        bool first;
        lock (_gate)
        {
            first = _warnedMissing.Add(row);
        }

        if (first)
        {
            _log.LogWarning("roblox-status: watched row {Row} is not on the page", row);
        }
    }

    private void SetUpdate(RobloxUpdateState next)
    {
        lock (_gate)
        {
            if (_disposed || _update == next)
            {
                return;
            }

            _update = next;
        }

        RaiseChanged();
    }

    private void SetStatus(RobloxStatusReading next)
    {
        lock (_gate)
        {
            if (_disposed || _status == next)
            {
                return;
            }

            _status = next;
        }

        RaiseChanged();
    }

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "A roblox-feeds subscriber threw; ignoring.");
        }
    }

    /// <summary>Stops the timer and cancels a read in flight. Idempotent.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }

        try { _stopping.Cancel(); }
        catch (ObjectDisposedException) { }
    }
}
