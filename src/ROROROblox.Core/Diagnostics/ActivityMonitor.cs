using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ROROROblox.Core.Diagnostics;

public sealed class ActivityMonitor : IActivityMonitor
{
    private sealed class Record
    {
        public DateTimeOffset LastActivityAt;
        public bool WarnLatched;
    }

    private readonly IForegroundWindowProbe _foreground;
    private readonly ISystemInputClock _input;
    private readonly IForegroundAccountResolver _resolver;
    private readonly IClock _clock;
    private readonly ConcurrentDictionary<Guid, Record> _records = new();

    private uint? _lastSeenInputTick;

    public TimeSpan WarnThreshold { get; set; } = TimeSpan.FromMinutes(15);

    public event EventHandler<IReadOnlyList<Guid>>? WarnThresholdCrossed;

    public ActivityMonitor(
        IForegroundWindowProbe foreground,
        ISystemInputClock input,
        IForegroundAccountResolver resolver,
        IClock clock)
    {
        _foreground = foreground ?? throw new ArgumentNullException(nameof(foreground));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public void OnAccountLaunched(Guid accountId)
        => _records[accountId] = new Record { LastActivityAt = _clock.UtcNow, WarnLatched = false };

    public void OnAccountExited(Guid accountId)
        => _records.TryRemove(accountId, out _);

    public void Sample()
    {
        var now = _clock.UtcNow;

        // 1. foreground + input stamping. No prior tick recorded yet ("no baseline") counts as
        // advanced: any observed tick is new information the first time we see it. Only a
        // *repeated* tick value against a known prior sample means "no input happened."
        var currentTick = _input.LastInputTick;
        var advanced = _lastSeenInputTick is not { } previousTick || currentTick != previousTick;
        _lastSeenInputTick = currentTick;

        if (advanced
            && _foreground.TryGetForegroundPid(out var pid)
            && _resolver.TryResolveAccountByPid(pid, out var accountId)
            && _records.TryGetValue(accountId, out var rec))
        {
            rec.LastActivityAt = now;
        }

        // 2. threshold edge evaluation (coalesced)
        List<Guid>? crossed = null;
        foreach (var kv in _records)
        {
            var since = now - kv.Value.LastActivityAt;
            if (since >= WarnThreshold)
            {
                if (!kv.Value.WarnLatched)
                {
                    kv.Value.WarnLatched = true;
                    (crossed ??= new List<Guid>()).Add(kv.Key);
                }
            }
            else if (kv.Value.WarnLatched)
            {
                kv.Value.WarnLatched = false; // re-arm
            }
        }

        if (crossed is { Count: > 0 })
        {
            WarnThresholdCrossed?.Invoke(this, crossed);
        }
    }

    public IReadOnlyList<AccountActivity> GetSnapshot()
    {
        var now = _clock.UtcNow;
        var list = new List<AccountActivity>(_records.Count);
        foreach (var kv in _records)
        {
            var since = now - kv.Value.LastActivityAt;
            if (since < TimeSpan.Zero) since = TimeSpan.Zero; // clock-skew guard
            list.Add(new AccountActivity(kv.Key, kv.Value.LastActivityAt, since));
        }
        return list;
    }
}
