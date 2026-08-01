using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    private static readonly TimeSpan SummaryInterval = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Cap re-arm deadband. Real client memory oscillates ~13-25 MB (measured live), so a bare
    /// `!overCap` re-arm turns an account parked near the cap into a fresh balloon every few
    /// minutes. Re-arming at 95% of the cap is ~5x the observed amplitude, while still re-arming
    /// promptly after a real Recycle (which drops a client by roughly a gigabyte).
    /// </summary>
    private const double CapReArmFactor = 0.95;

    /// <summary>
    /// Projection re-arm deadband. Same reasoning, opposite direction: a HIGHER minutes-to-ceiling
    /// is healthier, so recovery must clear the threshold by 15% before the latch re-arms.
    /// </summary>
    private const double ProjectionReArmFactor = 1.15;

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
    private readonly ILogger<MemoryWatchdog> _log;
    private readonly ConcurrentDictionary<Guid, Record> _records = new();

    private Timer? _timer;
    private int _sampling;
    private bool _disposed;

    // Seeded (not default) so GetSnapshot() can NEVER hand out a null Accounts before the first
    // Sample() completes. A bare `default(MemoryPressureSnapshot)` leaves Accounts null — every
    // consumer of GetSnapshot() (Task 7's MainViewModel today, System Health reporting later)
    // would otherwise inherit that trap silently, since the interface signature carries no `?`
    // and no warning. Fixing it here, once, beats every call site re-guarding with `?? []`.
    private MemoryPressureSnapshot _last = new(0, 0, 0, false, null, []);
    private DateTimeOffset _lastSummaryAt = DateTimeOffset.MinValue;

    public long CapBytes { get; set; }
    public long ReserveBytes { get; set; }
    public int ProjectionWarnMinutes { get; set; } = 120;

    public event EventHandler<MemoryPressureSnapshot>? PressureCrossed;

    public MemoryWatchdog(IProcessMemoryProbe process, ISystemMemoryProbe system, IClock clock,
        ILogger<MemoryWatchdog>? log = null)
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _system = system ?? throw new ArgumentNullException(nameof(system));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log ?? NullLogger<MemoryWatchdog>.Instance;
    }

    public void OnAccountLaunched(Guid accountId, int pid) => ResetBaseline(accountId, pid);

    public void OnAccountExited(Guid accountId, int pid)
    {
        // pid-aware removal (final-branch review IMPORTANT 5): only drop the record if it's still
        // pinned to THIS pid. A late exit callback for a pid a Recycle has already superseded must
        // not remove the fresh record the new launch just installed.
        if (_records.TryGetValue(accountId, out var rec) && rec.Pid == pid)
        {
            _records.TryRemove(accountId, out _);
        }
    }

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
                _log.LogDebug("memory: pid {Pid} for account {AccountId} unreadable this tick; excluded from aggregate", rec.Pid, kv.Key);
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
            if (overCap && !rec.CapLatched)
            {
                rec.CapLatched = true;
                crossed = true;
                _log.LogWarning("memory cap crossed: account {AccountId} at {Mb} MB (cap {CapMb} MB)",
                    a.AccountId, a.PrivateBytes / (1024 * 1024), CapBytes / (1024 * 1024));
            }
            // Re-arm ONLY on a KNOWN clear (a.ReadOk) AND a MEANINGFUL retreat (below
            // CapReArmFactor of the cap, not just the bare inverse of `overCap`) -- an
            // unreadable pid (a.ReadOk false, the routine "process mid-teardown" case) is
            // UNKNOWN, not clear, and must not re-arm the latch (final-branch review IMPORTANT
            // 3). A bare `!overCap` re-arm is a SEPARATE bug: real client memory oscillates
            // 13-25 MB, so a value parked one byte under the cap re-arms and the very next tick
            // re-crosses -- one account measured live crossed four times in eight minutes on
            // that bug. CapReArmFactor requires a real retreat before the latch resets.
            else if (a.ReadOk && a.PrivateBytes < CapBytes * CapReArmFactor) { rec.CapLatched = false; }

            var overProjection = hasProjection && minutes < ProjectionWarnMinutes;
            if (overProjection && !rec.ProjectionLatched)
            {
                rec.ProjectionLatched = true;
                crossed = true;
                _log.LogWarning(
                    "memory projection crossed: account {AccountId}, {Minutes} min to ceiling (aggregate {GrowthMbPerHr:F0} MB/hr, available {AvailableMb} MB, target {TargetAccountId})",
                    a.AccountId, minutes, aggregateGrowth / (1024 * 1024), (systemOk ? available : 0) / (1024 * 1024), target);
            }
            // Re-arm ONLY on a KNOWN clear (systemOk) AND a MEANINGFUL recovery (minutes past
            // ProjectionReArmFactor of the warn threshold, not just the bare inverse of
            // `overProjection`) -- a failed GlobalMemoryStatusEx read is UNKNOWN, not clear, and
            // must not clear every account's projection latch machine-wide. The spec's
            // failure-mode table says a failed availPhys read must SKIP projection evaluation;
            // clearing the latch is evaluating it (final-branch review IMPORTANT 3). Bare
            // `!overProjection` is a SEPARATE bug: the projection axis flapped live -- crossed at
            // 85 min, cleared, crossed again at 113 min eleven minutes later. ProjectionReArmFactor
            // requires the countdown to genuinely recover before the latch resets.
            else if (systemOk && minutes > ProjectionWarnMinutes * ProjectionReArmFactor) { rec.ProjectionLatched = false; }

            accounts[i] = a with { OverCap = overCap, IsTarget = target == a.AccountId, MinutesToCeiling = minutes };
        }

        _last = new MemoryPressureSnapshot(
            AvailableBytes: systemOk ? available : 0,
            AggregateGrowthBytesPerHour: aggregateGrowth,
            MinutesToCeiling: minutes,
            HasProjection: hasProjection,
            TargetAccountId: target,
            Accounts: accounts);

        // Per-tick logging is banned: AppLogging's own comment records HttpClientFactory at 10s
        // consuming ~90% of a 15 MB day. The 15-minute summary carries the same information at
        // 1/30th the volume, and is what puts the memory CURVE in a user's log file. The aggregate
        // fields alone can't answer "which of my 3 clients is the one ballooning" -- that requires
        // the per-account payload appended below, not just machine-wide totals.
        if (now - _lastSummaryAt >= SummaryInterval)
        {
            _lastSummaryAt = now;
            _log.LogInformation(
                "memory: {Count} client(s), aggregate {GrowthMbPerHr:F0} MB/hr, available {AvailableMb} MB, projection {Minutes} min (valid={Valid}) {Accounts}",
                accounts.Count, aggregateGrowth / (1024 * 1024), (systemOk ? available : 0) / (1024 * 1024), minutes, hasProjection,
                FormatAccountPayload(accounts));
        }

        if (crossed)
        {
            PressureCrossed?.Invoke(this, _last);
        }
    }

    /// <summary>
    /// Compact per-account breakdown appended to the 15-minute summary line. This is the piece
    /// that lets a reader reconstruct "which of my N clients grew from 2 GB to 6 GB" -- the
    /// aggregate fields on the summary describe the machine, not any one client. Format:
    /// <c>[shortId:mbMB/mbPerHrph, ...]</c> for a fresh reading, <c>[shortId:mbMB(stale), ...]</c>
    /// for a tick where the pid was unreadable -- LastBytes is a last-known-good carried forward,
    /// never a fresh sample, and must read as visibly different from one or it becomes a false
    /// data point in the exact artifact this task exists to build.
    /// </summary>
    private static string FormatAccountPayload(List<AccountMemory> accounts)
    {
        if (accounts.Count == 0) return "[]";

        var parts = new string[accounts.Count];
        for (var i = 0; i < accounts.Count; i++)
        {
            var a = accounts[i];
            var shortId = a.AccountId.ToString("N")[..8];
            var mb = a.PrivateBytes / (1024 * 1024);
            parts[i] = a.ReadOk
                ? $"{shortId}:{mb}MB/{a.GrowthBytesPerHour / (1024 * 1024):F0}ph"
                : $"{shortId}:{mb}MB(stale)";
        }
        return "[" + string.Join(", ", parts) + "]";
    }

    /// <summary>
    /// Safe to call before the first <see cref="Sample"/> completes — returns an empty snapshot
    /// (<c>Accounts</c> is a non-null, zero-length list; <c>HasProjection</c> false) rather than
    /// a default struct with a null <c>Accounts</c>. Callers never need a null-check.
    /// </summary>
    public MemoryPressureSnapshot GetSnapshot() => _last;

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }
}
