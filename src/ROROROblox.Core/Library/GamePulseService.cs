using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.Core.Feeds;

namespace ROROROblox.Core.Library;

/// <summary>
/// In-memory game readings for what is on screen. Interest is a union across owners; a change of interest
/// is acted on after a 250 ms quiet period; a game is not asked about again within five minutes of its
/// last attempt, good or failed; a count is trusted for fifteen minutes and then reads null while the date
/// stays. Nothing is written to disk. Nothing is asked before <see cref="AllowReadsFrom"/>'s instant.
/// </summary>
public sealed class GamePulseService : IGamePulseService, IDisposable
{
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan MinReadAge = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan CountTrust = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan RefreshTick = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan RefusedHold = TimeSpan.FromMinutes(5);

    private sealed class Reading
    {
        public int Playing;
        public DateTimeOffset? UpdatedUtc;
        public DateTimeOffset At;
        public bool CountExpiredRaised;
    }

    private readonly Func<IGamePulseApi> _apiFactory;
    private readonly TimeProvider _time;
    private readonly ILogger<GamePulseService> _log;

    private readonly object _gate = new();
    private readonly Dictionary<object, HashSet<long>> _interest = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<long, Reading> _readings = [];
    private readonly Dictionary<long, DateTimeOffset> _lastAttempt = [];
    private DateTimeOffset? _allowFrom;
    private DateTimeOffset? _holdUntil;
    private bool _enabled = true;
    private bool _inFlight;
    private bool _dirty;
    private bool _disposed;
    private ITimer? _debounceTimer;
    private ITimer? _refreshTimer;
    private ITimer? _expiryTimer;

    public GamePulseService(Func<IGamePulseApi> apiFactory, TimeProvider? time = null, ILogger<GamePulseService>? log = null)
    {
        _apiFactory = apiFactory ?? throw new ArgumentNullException(nameof(apiFactory));
        _time = time ?? TimeProvider.System;
        _log = log ?? NullLogger<GamePulseService>.Instance;
    }

    public event EventHandler<GamePulseChangedEventArgs>? Changed;

    public GamePulseView? TryGet(long universeId)
    {
        lock (_gate)
        {
            if (!_enabled || !_readings.TryGetValue(universeId, out var r))
            {
                return null;
            }

            int? playing = _time.GetUtcNow() - r.At >= CountTrust ? null : r.Playing;
            return playing is null && r.UpdatedUtc is null ? null : new GamePulseView(playing, r.UpdatedUtc);
        }
    }

    public void SetInterest(object owner, IReadOnlyCollection<long> universeIds)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(universeIds);

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var next = new HashSet<long>(universeIds);
            if (next.Count == 0)
            {
                if (!_interest.Remove(owner))
                {
                    return;
                }
            }
            else
            {
                if (_interest.TryGetValue(owner, out var current) && current.SetEquals(next))
                {
                    return;
                }

                _interest[owner] = next;
            }

            if (_interest.Count == 0)
            {
                StopTimers();
                return;
            }

            _refreshTimer ??= _time.CreateTimer(_ => OnTick(), null, RefreshTick, RefreshTick);
            ArmExpiry();
            ArmDebounce();
        }
    }

    public void SetEnabled(bool enabled)
    {
        List<long> cleared;
        lock (_gate)
        {
            if (_disposed || _enabled == enabled)
            {
                return;
            }

            _enabled = enabled;
            if (enabled)
            {
                return;
            }

            cleared = [.. _readings.Keys];
            _readings.Clear();
            _lastAttempt.Clear();
            _debounceTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _expiryTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        Raise(cleared, always: true);
    }

    public void AllowReadsFrom(DateTimeOffset instant)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _allowFrom = instant;
            if (_interest.Count > 0)
            {
                ArmDebounce();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            StopTimers();
        }
    }

    // All of these run under _gate.
    private void StopTimers()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = null;
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        _expiryTimer?.Dispose();
        _expiryTimer = null;
    }

    private void ArmDebounce()
    {
        var delay = Debounce;
        if (_allowFrom is { } from)
        {
            var wait = from - _time.GetUtcNow();
            if (wait > delay)
            {
                delay = wait;
            }
        }

        if (_debounceTimer is null)
        {
            _debounceTimer = _time.CreateTimer(_ => OnTick(), null, delay, Timeout.InfiniteTimeSpan);
        }
        else
        {
            _debounceTimer.Change(delay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>One one-shot timer aimed at the earliest count that has not yet expired.</summary>
    private void ArmExpiry()
    {
        if (_interest.Count == 0)
        {
            return;
        }

        DateTimeOffset? earliest = null;
        foreach (var r in _readings.Values)
        {
            if (!r.CountExpiredRaised && (earliest is null || r.At < earliest))
            {
                earliest = r.At;
            }
        }

        if (earliest is not { } at)
        {
            _expiryTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        var due = at + CountTrust - _time.GetUtcNow();
        if (due < TimeSpan.Zero)
        {
            due = TimeSpan.Zero;
        }

        if (_expiryTimer is null)
        {
            _expiryTimer = _time.CreateTimer(_ => OnExpiry(), null, due, Timeout.InfiniteTimeSpan);
        }
        else
        {
            _expiryTimer.Change(due, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnExpiry()
    {
        List<long> expired = [];
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var now = _time.GetUtcNow();
            foreach (var (id, r) in _readings)
            {
                if (!r.CountExpiredRaised && now - r.At >= CountTrust)
                {
                    r.CountExpiredRaised = true;
                    expired.Add(id);
                }
            }

            ArmExpiry();
        }

        Raise(expired);
    }

    private void OnTick()
    {
        // The timer callback is the only caller; the pass never throws.
        _ = PassAsync();
    }

    internal async Task PassAsync()
    {
        try
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                if (_inFlight)
                {
                    _dirty = true;
                    return;
                }

                _inFlight = true;
            }

            while (true)
            {
                List<long> due;
                lock (_gate)
                {
                    due = CollectDue();
                    if (due.Count == 0)
                    {
                        _inFlight = false;
                        return;
                    }
                }

                var stop = await AskAsync(due).ConfigureAwait(false);

                lock (_gate)
                {
                    if (stop || _disposed || !_dirty)
                    {
                        _inFlight = false;
                        _dirty = false;
                        return;
                    }

                    _dirty = false;
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "game-pulse pass failed");
            lock (_gate)
            {
                _inFlight = false;
            }
        }
    }

    private List<long> CollectDue()
    {
        var now = _time.GetUtcNow();
        if (!_enabled || _allowFrom is not { } from || now < from || (_holdUntil is { } hold && now < hold))
        {
            return [];
        }

        var due = new SortedSet<long>();
        foreach (var set in _interest.Values)
        {
            foreach (var id in set)
            {
                if (!_lastAttempt.TryGetValue(id, out var last) || now - last >= MinReadAge)
                {
                    due.Add(id);
                }
            }
        }

        return [.. due];
    }

    /// <summary>Consecutive requests of at most 50, never in parallel. True when a refusal stops the rest.</summary>
    private async Task<bool> AskAsync(List<long> ids)
    {
        for (var offset = 0; offset < ids.Count; offset += IGamePulseApi.MaxIdsPerRequest)
        {
            var chunk = ids.GetRange(offset, Math.Min(IGamePulseApi.MaxIdsPerRequest, ids.Count - offset));
            lock (_gate)
            {
                if (_disposed || !_enabled)
                {
                    return true;
                }

                var now = _time.GetUtcNow();
                foreach (var id in chunk)
                {
                    _lastAttempt[id] = now;
                }
            }

            GamePulseRead read;
            try
            {
                read = await _apiFactory().ReadAsync(chunk).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "game-pulse read threw");
                read = new GamePulseRead(FeedOutcome.Network, null);
            }

            List<long> changed = [];
            lock (_gate)
            {
                if (_disposed || !_enabled)
                {
                    return true;
                }

                var now = _time.GetUtcNow();
                if (read.Outcome == FeedOutcome.Ok && read.Answer is { } answer)
                {
                    foreach (var id in chunk)
                    {
                        if (answer.Found.TryGetValue(id, out var found))
                        {
                            var had = _readings.TryGetValue(id, out var old)
                                && !old.CountExpiredRaised && old.Playing == found.Playing && old.UpdatedUtc == found.UpdatedUtc;
                            _readings[id] = new Reading { Playing = found.Playing, UpdatedUtc = found.UpdatedUtc, At = now };
                            if (!had)
                            {
                                changed.Add(id);
                            }
                        }
                        else if (_readings.Remove(id))
                        {
                            changed.Add(id);
                        }
                    }

                    ArmExpiry();
                }
                else if (read.Outcome == FeedOutcome.Refused)
                {
                    _holdUntil = now + RefusedHold;
                }
            }

            Raise(changed);
            if (read.Outcome == FeedOutcome.Refused)
            {
                return true;
            }
        }

        return false;
    }

    private void Raise(List<long> ids, bool always = false)
    {
        if (ids.Count == 0 && !always)
        {
            return;
        }

        try
        {
            Changed?.Invoke(this, new GamePulseChangedEventArgs(ids));
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "game-pulse Changed handler threw");
        }
    }
}
