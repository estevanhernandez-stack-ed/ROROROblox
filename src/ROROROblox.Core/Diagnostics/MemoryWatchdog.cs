using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Samples private bytes per tracked account, estimates a linear growth rate, and projects time
/// to machine RAM exhaustion. Mirrors <see cref="ActivityMonitor"/>'s shape deliberately:
/// injected probes, Interlocked-guarded timer, public Sample() seam, latch/re-arm edges.
/// </summary>
public sealed class MemoryWatchdog : IMemoryWatchdog, IDisposable
{
    /// <summary>Below this, no slope is claimed. A 30s sample yields a confident, wrong projection.</summary>
    public static readonly TimeSpan MinimumObservation = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(30);

    private sealed class Record
    {
        public int Pid;
        public long BaselineBytes;
        public DateTimeOffset BaselineAt;
        public long LastBytes;
        public bool LastReadOk;
        public bool CapLatched;
        public bool ProjectionLatched;
    }

    private readonly IProcessMemoryProbe _process;
    private readonly ISystemMemoryProbe _system;
    private readonly IClock _clock;
    private readonly ConcurrentDictionary<Guid, Record> _records = new();

    private Timer? _timer;
    private int _sampling;
    private bool _disposed;
    private MemoryPressureSnapshot _last;

    public long CapBytes { get; set; }
    public long ReserveBytes { get; set; }
    public int ProjectionWarnMinutes { get; set; } = 120;

    public event EventHandler<MemoryPressureSnapshot>? PressureCrossed;

    public MemoryWatchdog(IProcessMemoryProbe process, ISystemMemoryProbe system, IClock clock)
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _system = system ?? throw new ArgumentNullException(nameof(system));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public void OnAccountLaunched(Guid accountId, int pid) => ResetBaseline(accountId, pid);

    public void OnAccountExited(Guid accountId) => _records.TryRemove(accountId, out _);

    public void ResetBaseline(Guid accountId, int pid)
        => _records[accountId] = new Record
        {
            Pid = pid,
            BaselineBytes = 0,
            BaselineAt = _clock.UtcNow,
            LastBytes = 0,
            LastReadOk = false,
            CapLatched = false,
            ProjectionLatched = false,
        };

    public void Start()
    {
        if (_disposed) return;
        _timer ??= new Timer(_ => SafeSample(), null, SampleInterval, SampleInterval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void SafeSample()
    {
        if (Interlocked.Exchange(ref _sampling, 1) == 1) return;
        try { Sample(); }
        catch { /* never let a sample tick crash the timer thread */ }
        finally { Interlocked.Exchange(ref _sampling, 0); }
    }

    public void Sample()
    {
        var now = _clock.UtcNow;
        var accounts = new List<AccountMemory>(_records.Count);
        double aggregateGrowth = 0;

        foreach (var kv in _records)
        {
            var rec = kv.Value;

            if (!_process.TryReadPrivateBytes(rec.Pid, out var bytes))
            {
                // UNKNOWN. Keep the record for the next tick; contribute NOTHING to the aggregate.
                rec.LastReadOk = false;
                accounts.Add(new AccountMemory(kv.Key, rec.LastBytes, 0, 0, false, false, ReadOk: false));
                continue;
            }

            rec.LastReadOk = true;
            rec.LastBytes = bytes;

            // First successful reading seeds the baseline.
            if (rec.BaselineBytes == 0)
            {
                rec.BaselineBytes = bytes;
                rec.BaselineAt = now;
            }
            else if (bytes < rec.BaselineBytes)
            {
                // Ratchet: a teleport freed memory. Without this, one drop poisons the slope forever.
                rec.BaselineBytes = bytes;
                rec.BaselineAt = now;
            }

            var elapsed = now - rec.BaselineAt;
            // Clock-skew guard: clamp a negative elapsed to zero. Defense-in-depth, not currently
            // load-bearing -- MinimumObservation is a fixed positive 10-minute floor, so a clamped
            // TimeSpan.Zero never clears the `elapsed >= MinimumObservation` gate below, meaning
            // negative elapsed can never reach the growth math regardless of this line. Kept per
            // spec (mirrors ActivityMonitor.GetSnapshot's clock-skew clamp) and to protect any
            // future code that reads `elapsed` before the gate, or if the gate's ordering/threshold
            // changes. Do not remove as dead code.
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

            double growth = 0;
            if (elapsed >= MinimumObservation)
            {
                growth = (bytes - rec.BaselineBytes) / elapsed.TotalHours;
                if (growth < 0) growth = 0;
                aggregateGrowth += growth;
            }

            accounts.Add(new AccountMemory(kv.Key, bytes, growth, 0, false, false, ReadOk: true));
        }

        var systemOk = _system.TryRead(out _, out var available);
        var hasProjection = systemOk && aggregateGrowth > 0;
        var minutes = 0;
        if (hasProjection)
        {
            var availableForClients = Math.Max(0, available - ReserveBytes);
            // Compute in double first: a very small positive aggregateGrowth against a large
            // availableForClients can exceed int.MaxValue, and narrowing an out-of-range double to
            // int is unspecified in C# (lands negative on the CLR). Clamp before casting so
            // MinutesToCeiling can never surface a negative countdown to the UI (Task 7).
            var minutesRaw = availableForClients / aggregateGrowth * 60;
            minutes = (int)Math.Clamp(minutesRaw, 0, int.MaxValue);
        }

        // Target = fattest client with a valid reading. The projection describes the machine;
        // the user needs to know which account to act on.
        Guid? target = accounts
            .Where(a => a.ReadOk)
            .OrderByDescending(a => a.PrivateBytes)
            .Select(a => (Guid?)a.AccountId)
            .FirstOrDefault();

        // Edge-triggered evaluation. Latch per account so one crossing = one warning.
        var crossed = false;
        for (var i = 0; i < accounts.Count; i++)
        {
            var a = accounts[i];
            if (!_records.TryGetValue(a.AccountId, out var rec)) continue;

            var overCap = CapBytes > 0 && a.ReadOk && a.PrivateBytes > CapBytes;
            if (overCap && !rec.CapLatched) { rec.CapLatched = true; crossed = true; }
            else if (!overCap) { rec.CapLatched = false; }

            var overProjection = hasProjection && minutes < ProjectionWarnMinutes;
            if (overProjection && !rec.ProjectionLatched) { rec.ProjectionLatched = true; crossed = true; }
            else if (!overProjection) { rec.ProjectionLatched = false; }

            accounts[i] = a with { OverCap = overCap, IsTarget = target == a.AccountId, MinutesToCeiling = minutes };
        }

        _last = new MemoryPressureSnapshot(
            AvailableBytes: systemOk ? available : 0,
            AggregateGrowthBytesPerHour: aggregateGrowth,
            MinutesToCeiling: minutes,
            HasProjection: hasProjection,
            TargetAccountId: target,
            Accounts: accounts);

        if (crossed)
        {
            PressureCrossed?.Invoke(this, _last);
        }
    }

    public MemoryPressureSnapshot GetSnapshot() => _last;

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }
}
