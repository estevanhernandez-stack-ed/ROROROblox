using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ROROROblox.Core;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// <b>2026-09-11 correction -- the paragraph below names a real condition, but not THE cause.</b>
/// The quiet pool stays: it is cheap and it does remove contention. It did not close the flake.
/// The same "Pump stalled" failure kept landing on arm64 CI afterwards -- five runs on main
/// between 2026-09-09 and 2026-09-10, every one of them including
/// <see cref="PostWriteQuietWait_CompetingWriteLandsInsideTheWindow_ForcesARetry"/>. The actual
/// cause was a lost-wakeup deadlock in the pump's own signal, not starvation; see
/// <see cref="ArmSignallingClock"/> for the mechanism. It reproduces on a fully idle 16-core box
/// with no parallelism and no load at all -- sleeping 5ms inside the probe's read took 9 of this
/// file's 11 tests to a 30s stall -- which is why "25/25 clean in isolation" was never evidence
/// about the mechanism: isolation changes how often the race is lost, not whether it exists.
/// Starvation was the wrong suspect for the reason the failure message itself gives -- elapsed
/// time cannot tell a starved continuation from a timer that was never armed.
/// <para>
/// F-116's last family, closed by removing the condition instead of surviving it. The pump below
/// advances a fake clock and then waits, in real time, for the settler's continuation to resume —
/// and that continuation is a plain thread-pool work item. Run in parallel with the rest of the
/// suite, the pool is contended in exactly the way this file's own diagnosis measured: a sibling
/// test's work drains LIFO from local queues ahead of the global queue where FakeTimeProvider's
/// timer callback sits, and the pool's injection ramp (watched climbing 18 → 36 while the awaited
/// continuation never ran) adds roughly one thread per 500ms — far slower than the pump's
/// patience. Two mitigations were tried and measured: raising SetMinThreads 4x did nothing (3
/// failures in ~8 runs before, 2 in 7 after — reverted), and the pump's own 30s observation
/// ceiling only converts the stall into a loud failure. What was ALSO measured, and is the whole
/// basis of this fix: the same tests run 25/25 clean in isolation with no generous ceiling needed.
/// So this collection opts out of parallel execution — the settler's continuations get a quiet
/// pool, which is the "scheduler of their own" the register row said was the untested hypothesis.
/// The cost is these tests' own wall time (~1s of real time) running serially, and
/// <see cref="FpsCapSettlerTests.ThePumpKeepsItsQuietPool"/> fails the build if the attribute is
/// ever tidied away.
/// </para>
/// </summary>
[CollectionDefinition(QuietPoolCollectionName, DisableParallelization = true)]
public sealed class FpsCapSettlerQuietPoolCollection
{
    public const string QuietPoolCollectionName = "FpsCapSettler quiet pool";
}

[Collection(FpsCapSettlerQuietPoolCollection.QuietPoolCollectionName)]
public sealed class FpsCapSettlerTests
{
    /// <summary>
    /// The fence on the fix above: this class must stay in a parallel-disabled collection. If this
    /// fails, someone removed or renamed the attribute pair, and the pump is back to sharing a
    /// contended pool — re-read the collection definition's summary before deciding that is fine.
    /// </summary>
    [Fact]
    public void ThePumpKeepsItsQuietPool()
    {
        var collection = (CollectionAttribute?)Attribute.GetCustomAttribute(
            typeof(FpsCapSettlerTests), typeof(CollectionAttribute));
        Assert.NotNull(collection);

        var definition = (CollectionDefinitionAttribute?)Attribute.GetCustomAttribute(
            typeof(FpsCapSettlerQuietPoolCollection), typeof(CollectionDefinitionAttribute));
        Assert.NotNull(definition);
        Assert.True(definition.DisableParallelization,
            "The FpsCapSettler collection no longer disables parallelization — the pump is back on "
            + "a contended pool, which is the F-116 flake condition.");
    }

    private static readonly TimeSpan TestBound = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Generous pump budget for tests that let the slow path run to completion. SettleTimeout
    /// (45s) is the real ceiling on any single settle call regardless of how many quiet-waits or
    /// retries it takes internally, so advancing a little past it is always enough headroom and
    /// never needs re-deriving per test.
    /// </summary>
    private static readonly TimeSpan SlowPathBudget = FpsCapSettler.SettleTimeout + TimeSpan.FromSeconds(2);

    /// <summary>
    /// The fake clock, plus a signal that fires when the code under test has ARMED its next timer.
    /// <see cref="ArmCount"/> is the authoritative value <see cref="PumpStepAsync"/> checks;
    /// <see cref="WaitForArmAsync"/> is purely a wakeup so the pump need not busy-spin -- see the
    /// "why not Task.Yield()" remarks on <see cref="PumpStepAsync"/> for why that matters.
    /// <para>
    /// <b>Why the arm and not the mtime read (2026-09-11).</b> The pump used to wait on the probe's
    /// <c>GetLastWriteTimeUtc()</c> call, reasoning that a read proves the settler's delay
    /// continuation resumed. It does prove that -- and it is not enough.
    /// <c>FpsCapSettler.WaitForQuietAsync</c> reads the mtime and only THEN loops back to arm its
    /// next <c>Task.Delay</c>, so a pump released by the read can advance the clock again while the
    /// settler is still in the handful of instructions between the two. The settler then arms
    /// against the already-advanced clock, making its timer due a full step in the FUTURE, and the
    /// pump -- which will not advance again until it sees another read -- waits for a wakeup that
    /// can never arrive. A permanent deadlock, reported by the 30s ceiling as if it were a slow
    /// runner. Confirmed by construction, not inferred: a 5ms sleep injected into the probe's read
    /// on an idle, unparallelised box stalled 9 of this file's 11 tests at exactly 30s, reproducing
    /// the arm64 CI failure byte for byte.
    /// </para>
    /// <para>
    /// The arm has no such gap. <c>base.CreateTimer</c> registers the waiter before it returns, so
    /// "a timer was armed since my advance" means the settler has already consumed that advance AND
    /// is parked for the next one. That makes it structurally impossible for the clock to get ahead
    /// of the code under test -- which is what the read-based signal claimed and did not deliver.
    /// </para>
    /// <para>
    /// A stale release, left by an arm that happened while nobody was waiting, causes one extra
    /// harmless wakeup, which the caller discards by re-checking <see cref="ArmCount"/> against its
    /// own baseline. <see cref="PumpStepAsync"/> therefore ignores the wait's return value
    /// entirely; trusting it is how the old pump consumed a stale permit and returned without the
    /// settler having observed anything at all.
    /// </para>
    /// </summary>
    private sealed class ArmSignallingClock : FakeTimeProvider
    {
        private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
        private int _arms;

        public ArmSignallingClock(DateTimeOffset start) : base(start) { }

        public int ArmCount => Volatile.Read(ref _arms);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);

            // An infinite dueTime arms nothing, so announcing it would release the pump on a timer
            // that can never fire. Task.Delay against this clock always passes a finite one; the
            // guard is here so a future caller creating a parked timer cannot quietly reintroduce
            // the lost-wakeup this class exists to close.
            if (dueTime != Timeout.InfiniteTimeSpan)
            {
                Interlocked.Increment(ref _arms);
                _signal.Release();
            }

            return timer;
        }

        /// <summary>Returns <c>Task</c>, not <c>Task&lt;bool&gt;</c>, so the wakeup-only nature of
        /// this wait is structural rather than a comment a caller can overlook.</summary>
        public Task WaitForArmAsync(TimeSpan timeout) => _signal.WaitAsync(timeout);
    }

    /// <summary>Scripted read side. Each ReadFramerateCap() pops the next scripted value.</summary>
    private sealed class FakeProbe : IGlobalBasicSettingsProbe
    {
        private readonly Queue<int?> _caps;
        public int ReadCalls { get; private set; }
        public DateTimeOffset? Mtime { get; set; } = DateTimeOffset.UnixEpoch;

        /// <summary>
        /// Real-time pause taken AFTER the mtime is read, used only by
        /// <see cref="ThePumpSurvivesAPreemptionBetweenTheReadAndTheRearm"/> to hold the settler
        /// inside the read-then-arm window the old pump signal released on. Zero everywhere else,
        /// so no other test in this file pays a millisecond for it.
        /// </summary>
        public TimeSpan PostReadPause { get; set; }

        public FakeProbe(params int?[] caps) => _caps = new Queue<int?>(caps);

        public int? ReadFramerateCap()
        {
            ReadCalls++;
            return _caps.Count > 0 ? _caps.Dequeue() : null;
        }

        public DateTimeOffset? GetLastWriteTimeUtc()
        {
            if (PostReadPause > TimeSpan.Zero) { Thread.Sleep(PostReadPause); }
            return Mtime;
        }
    }

    /// <summary>
    /// A real write touches the settings file's last-write time. Wiring that through here matters
    /// now that FpsCapSettler re-confirms with a second quiet-wait after writing (see class
    /// remarks on FpsCapSettler): without this, the fake's mtime would never move on our own
    /// write, and the post-write wait would trivially credit a stale mtime instead of genuinely
    /// re-arming its debounce the way it does against the real file.
    /// </summary>
    private sealed class RecordingWriter : IGlobalBasicSettingsWriter
    {
        private readonly FakeProbe _probe;
        private readonly TimeProvider _clock;

        public List<int?> Writes { get; } = new();
        public Exception? Throw { get; set; }

        public RecordingWriter(FakeProbe probe, TimeProvider clock)
        {
            _probe = probe;
            _clock = clock;
        }

        public Task WriteFramerateCapAsync(int? fps, CancellationToken ct = default)
        {
            if (Throw is not null) { throw Throw; }
            Writes.Add(fps);
            _probe.Mtime = _clock.GetUtcNow();
            return Task.CompletedTask;
        }

        /// <summary>F-109. Records rather than discards — the fallback write is worth asserting.</summary>
        public (bool Fullscreen, int X, int Y, int W, int H)? WindowState;
        public Task WriteWindowStateAsync(bool fullscreen, int x, int y, int width, int height, CancellationToken ct = default)
        { WindowState = (fullscreen, x, y, width, height); return Task.CompletedTask; }
    }

    /// <summary>
    /// Fix 4: <see cref="FakeProbe"/> scripts reads by CALL COUNT, never by time, and
    /// <see cref="RecordingWriter"/> only moves the mtime on OUR OWN write. Neither can express "a
    /// competing client's write lands at a specific fake-clock instant partway through the
    /// post-write quiet wait" -- which is exactly why deleting the post-write
    /// <c>WaitForQuietAsync</c> call left every one of the seven pre-existing tests green. This
    /// probe holds live, mutable <see cref="Cap"/> / <see cref="Mtime"/> state instead of a script,
    /// so a test can change either mid-wait and the settle call -- polling the SAME
    /// <see cref="FakeTimeProvider"/> the test drives -- has to actually observe it.
    /// </summary>
    private sealed class TimeAwareProbe : IGlobalBasicSettingsProbe
    {
        public int? Cap { get; set; }
        public DateTimeOffset? Mtime { get; set; }
        public int ReadCalls { get; private set; }

        public int? ReadFramerateCap() { ReadCalls++; return Cap; }

        public DateTimeOffset? GetLastWriteTimeUtc() => Mtime;
    }

    /// <summary>Writes through to a <see cref="TimeAwareProbe"/>, stamping its own write's mtime.</summary>
    private sealed class TimeAwareWriter : IGlobalBasicSettingsWriter
    {
        private readonly TimeAwareProbe _probe;
        private readonly TimeProvider _clock;
        public List<int?> Writes { get; } = new();

        public TimeAwareWriter(TimeAwareProbe probe, TimeProvider clock)
        {
            _probe = probe;
            _clock = clock;
        }

        public Task WriteFramerateCapAsync(int? fps, CancellationToken ct = default)
        {
            Writes.Add(fps);
            _probe.Cap = fps;
            _probe.Mtime = _clock.GetUtcNow();
            return Task.CompletedTask;
        }

        /// <summary>F-109. Records rather than discards — the fallback write is worth asserting.</summary>
        public (bool Fullscreen, int X, int Y, int W, int H)? WindowState;
        public Task WriteWindowStateAsync(bool fullscreen, int x, int y, int width, int height, CancellationToken ct = default)
        { WindowState = (fullscreen, x, y, width, height); return Task.CompletedTask; }
    }

    /// <summary>
    /// Real-time ceiling on how long <see cref="PumpStepAsync"/> waits for the settler to observe
    /// a single clock advance. This never bounds the success path -- a healthy settler resumes its
    /// continuation and calls <c>GetLastWriteTimeUtc()</c> within microseconds of the clock moving.
    /// It only fires when that never happens at all, turning a would-be hang into a loud, immediate
    /// assertion failure instead of the suite blocking for real time.
    /// <para>
    /// 30s, not 5s: measured directly (`dotnet test ROROROblox.slnx` looped dozens of times) that
    /// running the FULL SOLUTION -- 1182 tests, including ones that spawn real OS processes
    /// (<c>RobloxProcessTrackerTests</c>) and do real disk I/O -- occasionally delays a single
    /// continuation's real-world resume by tens of seconds for reasons that have nothing to do with
    /// this file: confirmed by running JUST <c>FpsCapSettlerTests</c> in isolation 25/25 clean with
    /// no ceiling anywhere near this generous. 30s gives that headroom without meaningfully slowing
    /// down the failure-path report on an actual hang (which would otherwise block forever).
    /// </para>
    /// <para>
    /// <b>2026-09-11:</b> value unchanged -- it was measured, and it still costs nothing on the
    /// success path -- but what it bounds is narrower now. Until the pump waited on the timer arm
    /// rather than the mtime read, the commonest way to reach this ceiling was the pump's own
    /// lost-wakeup deadlock (see <see cref="ArmSignallingClock"/>), which no amount of headroom
    /// could have survived. With that closed, reaching 30s again really does mean a hang in the
    /// code under test or a runner that scheduled nothing for half a minute.
    /// </para>
    /// </summary>
    private static readonly TimeSpan PumpObservationCeiling = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Advance the fake clock by exactly one step, then block until the settler has PROVABLY
    /// consumed it -- either it has armed its next timer against the advanced clock
    /// (<see cref="ArmSignallingClock"/>) or the settle task itself has completed.
    /// <para>
    /// <b>2026-09-11:</b> this used to wait on the probe's mtime READ instead. That signal fires one
    /// step too early -- the settler reads, and only then arms -- and a pump released inside that
    /// gap advances the clock past the timer the settler is about to register, deadlocking both
    /// sides until the ceiling below fires. <see cref="ArmSignallingClock"/> carries the full
    /// mechanism and the experiment that proved it.
    /// </para>
    /// <para>
    /// This replaces the old fixed-count <c>for (var i = 0; i &lt; 8; i++) { await Task.Yield(); }</c>
    /// pump, which advanced the clock unconditionally on a scheduler-turn BUDGET rather than on an
    /// observed SIGNAL. Under load (proven on CI arm64 and in a local full-suite run) 8 yields is
    /// not always enough for the settler's continuation to actually resume before the next advance
    /// lands, and because each test's pump budget (<see cref="SlowPathBudget"/> = SettleTimeout +
    /// 2s) deliberately exceeds the settler's own 45s deadline, a pump that keeps advancing while
    /// the settler is still asleep runs fake time past the settle budget for reasons that have
    /// nothing to do with the settler's own logic -- it returns <c>Exhausted</c> correctly by its
    /// own contract, while the test (which assumed its yields were enough) asserts <c>Settled</c>.
    /// Blocking on an observed signal instead of a yield count makes that race structurally
    /// impossible: the clock cannot get ahead of the code under test.
    /// </para>
    /// <para>
    /// <b>Waits on <see cref="ArmSignallingClock.WaitForArmAsync"/> (a <c>SemaphoreSlim</c>), not a
    /// <c>while (...) { await Task.Yield(); }</c> spin.</b> A first pass used a raw Yield spin and
    /// it reproduced the ORIGINAL bug's mechanism on itself: under full-suite parallel load, a tight
    /// spin loop re-posts its own continuation to its worker thread's LOCAL queue millions of times
    /// per second, which .NET's thread pool drains in LIFO order ahead of the GLOBAL queue --
    /// exactly where <c>FakeTimeProvider</c> posts the real timer callback the loop is waiting on.
    /// Measured directly: <c>ThreadPool.PendingWorkItemCount</c> stayed at 0 for 30 straight seconds
    /// while the pool kept injecting more worker threads (18 -&gt; 36) trying to compensate, and the
    /// spin still never observed the read it was waiting for. A semaphore wait has no such
    /// LIFO-vs-global asymmetry and costs zero CPU while blocked, so it does not create the load it
    /// is trying to survive. Neither this nor <c>Task.Yield()</c> is <c>Thread.Sleep</c> or a
    /// real-time delay tied to the SUCCESS path's pacing -- both only bound the FAILURE path -- but
    /// only the semaphore wait is actually safe to run inside a parallel test suite.
    /// </para>
    /// <para>
    /// Polls in short (200ms) waits rather than one long <see cref="PumpObservationCeiling"/> wait
    /// so the settle task's completion is noticed promptly. It no longer re-issues
    /// <c>clock.Advance(TimeSpan.Zero)</c> between them (removed 2026-09-11): that was a defensive
    /// nudge against "an edge this reading missed", and the edge turned out to be real but immune
    /// to it -- a waiter armed a full step in the future cannot be woken by advancing zero. Naming
    /// the edge removed the reason for the nudge.
    /// </para>
    /// </summary>
    private static async Task PumpStepAsync(ArmSignallingClock clock, TimeSpan step, Task settleTask)
    {
        var before = clock.ArmCount;
        clock.Advance(step);

        var budget = Stopwatch.StartNew();
        while (clock.ArmCount == before && !settleTask.IsCompleted)
        {
            // The wait's return value is deliberately discarded: a stale release left over from an
            // arm nobody was waiting on would otherwise break this loop with the settler still
            // un-parked, which is the same "clock gets ahead of the code" bug in a new costume.
            // ArmCount against this caller's own baseline is the only thing that decides.
            await clock.WaitForArmAsync(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
            if (clock.ArmCount != before || settleTask.IsCompleted)
            {
                break;
            }

            if (budget.Elapsed > PumpObservationCeiling)
            {
                // This message used to end "a genuine hang, not a slow test." It cannot know that,
                // PumpObservationCeiling's own summary says why: a loaded runner
                // delays a single continuation's real-world resume by tens of seconds for reasons
                // that have nothing to do with this file. Elapsed time cannot separate a deadlock
                // from a starved thread, so the message asserted a conclusion its evidence did not
                // reach -- and it sends whoever reads it hunting a deadlock that may not exist.
                // Observed on x64 CI at 14bbd03, green on arm64 at the same commit and green on
                // re-run. (F-098: an instrument claiming more than it measures, in the direction
                // that wastes an afternoon rather than the one that ships a bug.)
                Assert.Fail(
                    "Pump stalled: the settler never armed its next timer within " +
                    $"{PumpObservationCeiling} of real time after the fake clock advanced by {step}. " +
                    "That is EITHER a genuine hang in the code under test OR a continuation this " +
                    "runner never scheduled; elapsed time alone cannot tell them apart. Re-run before " +
                    "investigating: if it passes, it was starvation, and only a repeatable failure " +
                    "is evidence of a hang.");
            }
        }
    }

    /// <summary>
    /// Advance the fake clock in <paramref name="step"/> increments up to <paramref name="total"/>,
    /// pumping each step through <see cref="PumpStepAsync"/> so the clock never outruns
    /// <paramref name="settleTask"/>. Stops early once the settle task completes -- further
    /// advances past that point serve nothing.
    /// </summary>
    private static async Task AdvanceAsync(
        ArmSignallingClock clock, TimeSpan total, TimeSpan step, Task settleTask)
    {
        var elapsed = TimeSpan.Zero;
        while (elapsed < total && !settleTask.IsCompleted)
        {
            await PumpStepAsync(clock, step, settleTask);
            elapsed += step;
        }
    }

    [Fact]
    public async Task FileAlreadyHoldsTheCap_WritesNothingAndReturnsImmediately()
    {
        var probe = new FakeProbe(20);
        // FakeTimeProvider()'s parameterless ctor starts at 2000-01-01, not DateTimeOffset.UnixEpoch.
        // Pin it to UnixEpoch explicitly so it agrees with FakeProbe.Mtime's default below and the
        // "no time passed" assertion is checking something real, not an unrelated ctor default.
        var clock = new ArmSignallingClock(DateTimeOffset.UnixEpoch);
        var writer = new RecordingWriter(probe, clock);

        var outcome = await FpsCapSettler
            .SettleAsync(probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None)
            .WaitAsync(TestBound);

        Assert.Equal(FpsCapSettleOutcome.AlreadySet, outcome);
        Assert.Empty(writer.Writes);
        Assert.Equal(1, probe.ReadCalls);
        // No time passed: the fast path must not wait for quiet.
        Assert.Equal(DateTimeOffset.UnixEpoch.UtcDateTime, clock.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task QuietFileThenSurvivingWrite_Settles()
    {
        // read 1: current cap is 9999 (not ours) -> take the slow path
        // read 2: after the post-write quiet window, our 20 is still there -> settled
        var probe = new FakeProbe(9999, 20);
        // Pinned to UnixEpoch to match FakeProbe.Mtime's default -- otherwise FakeTimeProvider's
        // real 2000-01-01 start would be ~30 years past any epoch mtime, and the pre-write wait
        // would credit "already quiet" on its very first check instead of genuinely polling
        // through the debounce, silently skipping the behavior this test exists to exercise.
        var clock = new ArmSignallingClock(DateTimeOffset.UnixEpoch);
        var writer = new RecordingWriter(probe, clock);

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None);

        await AdvanceAsync(clock, SlowPathBudget, FpsCapSettler.QuietPollInterval, task);

        var outcome = await task.WaitAsync(TestBound);

        Assert.Equal(FpsCapSettleOutcome.Settled, outcome);
        Assert.Equal(new int?[] { 20 }, writer.Writes);
    }

    [Fact]
    public async Task WriteClobbered_RetriesAndSettlesOnTheSecondAttempt()
    {
        // read 1: 9999 (not ours)
        // read 2: 9999 again -> our write was clobbered, retry
        // read 3: 20 -> survived
        var probe = new FakeProbe(9999, 9999, 20);
        var clock = new ArmSignallingClock(DateTimeOffset.UnixEpoch);
        var writer = new RecordingWriter(probe, clock);

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None);

        await AdvanceAsync(clock, SlowPathBudget, FpsCapSettler.QuietPollInterval, task);

        var outcome = await task.WaitAsync(TestBound);

        Assert.Equal(FpsCapSettleOutcome.Settled, outcome);
        Assert.Equal(2, writer.Writes.Count);
    }

    /// <summary>
    /// Fix 4: the branch none of the other six tests can exercise -- a competing write landing
    /// INSIDE the post-write quiet wait, at a scripted fake-clock instant rather than a scripted
    /// call count. Also exercises the pre-write "instant-credit" branch (mtime already older than
    /// <see cref="FpsCapSettler.QuietDebounce"/> when the wait starts) twice: once on attempt 1
    /// (mtime seeded stale on purpose) and once on attempt 2 (mtime is naturally stale by then,
    /// since the clobber landed several seconds before the retry begins) -- the common production
    /// path (Roblox writes this file on session exit; the first launch of a session usually finds
    /// no Roblox process running at all), which the epoch-pinned <see cref="FakeProbe"/> fixture
    /// used everywhere else can't express either, since its mtime always starts exactly at the
    /// clock's own start.
    /// <para>
    /// Proven by mutation (see the fix report): deleting the post-write <c>WaitForQuietAsync</c>
    /// call in <c>FpsCapSettler.SettleAsync</c> turns this test red; restoring it turns it green.
    /// None of the other six tests in this file move on that mutation at all.
    /// </para>
    /// </summary>
    [Fact]
    public async Task PostWriteQuietWait_CompetingWriteLandsInsideTheWindow_ForcesARetry()
    {
        var clock = new ArmSignallingClock(DateTimeOffset.UnixEpoch);
        var probe = new TimeAwareProbe
        {
            Cap = 9999,
            // Already stale relative to the clock's start -- the pre-write instant-credit branch.
            Mtime = DateTimeOffset.UnixEpoch - TimeSpan.FromSeconds(30),
        };
        var writer = new TimeAwareWriter(probe, clock);

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None);

        // Fast-path check (mismatch) + pre-write instant-credit wait + attempt 1's write all
        // resolve without the clock needing to move.
        for (var i = 0; i < 20 && writer.Writes.Count == 0; i++) { await Task.Yield(); }
        Assert.Equal(1, writer.Writes.Count);

        var clobbered = false;
        var elapsed = TimeSpan.Zero;
        while (elapsed < SlowPathBudget && !task.IsCompleted)
        {
            // Land the clobber partway through attempt 1's post-write debounce window -- a
            // competing client's write at a specific FAKE-CLOCK INSTANT, which only a time-aware
            // probe (not a call-count script) can model. Landing at debounce-minus-2s means the
            // wait must have already been watching for at least 3s and has 2s of "quiet" credit
            // that this clobber invalidates -- if the wait were not genuinely time-driven, this
            // would land after it already returned and be invisible.
            if (!clobbered && elapsed >= FpsCapSettler.QuietDebounce - TimeSpan.FromSeconds(2))
            {
                probe.Cap = 9999;
                probe.Mtime = clock.GetUtcNow();
                clobbered = true;
            }

            await PumpStepAsync(clock, FpsCapSettler.QuietPollInterval, task);
            elapsed += FpsCapSettler.QuietPollInterval;
        }

        Assert.True(clobbered, "test setup bug: the clobber injection point was never reached");

        var outcome = await task.WaitAsync(TestBound);

        Assert.Equal(FpsCapSettleOutcome.Settled, outcome);
        // The clobber landing mid-window must force a genuine retry -- two writes, not one. A
        // settler that trusted the read right after the write (Fix 1's bug) or that never watched
        // for the mid-window clobber at all would settle on attempt 1 with a single write.
        Assert.Equal(new int?[] { 20, 20 }, writer.Writes);
    }

    /// <summary>
    /// THE headline test for the 2026-08-02 proof-of-read fix. A fake probe whose mtime changes at
    /// a SCRIPTED FAKE-CLOCK INSTANT simulates the previously launched client writing back late --
    /// the settler must not write the next account's cap before that write is observed, no matter
    /// how long the file has otherwise looked "quiet" against the launch baseline.
    /// <para>
    /// Proven by mutation: deleting the <c>writeObserved &amp;&amp;</c> conjunct in
    /// <c>FpsCapSettler</c>'s pre-write wait (i.e. deleting the proof-of-read gate) turns this test
    /// red -- the write then lands inside the window this test asserts must stay empty. Restoring
    /// the conjunct turns it green. A call-count fake (like <see cref="FakeProbe"/>) cannot express
    /// this at all, which is exactly why the original gap shipped four reviews deep without being
    /// caught.
    /// </para>
    /// </summary>
    [Fact]
    public async Task LaunchBaseline_GatesTheWriteUntilTheLaunchedClientsFirstWriteIsObserved()
    {
        var clock = new ArmSignallingClock(DateTimeOffset.UnixEpoch);
        // The mtime RobloxLauncher would have captured at the PREVIOUS launch's Process.Start.
        var baseline = clock.GetUtcNow();
        var probe = new TimeAwareProbe { Cap = 9999, Mtime = baseline };
        var writer = new TimeAwareWriter(probe, clock);

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None,
            launchBaselineUtc: baseline);

        // Drive the clock well past QuietDebounce with the file's mtime UNCHANGED from baseline --
        // the exact shape of the pre-fix bug: "the file hasn't moved" gets credited as quiet, but
        // for the wrong reason (the launched client hasn't started writing yet, not because it
        // already read the cap and calmed down). Must NOT write while this holds.
        var noWriteWindow = FpsCapSettler.QuietDebounce + TimeSpan.FromSeconds(3);
        await AdvanceAsync(clock, noWriteWindow, FpsCapSettler.QuietPollInterval, task);

        Assert.Empty(writer.Writes);

        // The launched client finally produces its first write-back -- proof it read the file --
        // at this scripted fake-clock instant. Mutated directly on the probe (not through `writer`,
        // which only records OUR writes): this models a DIFFERENT process.
        probe.Cap = 9999;
        probe.Mtime = clock.GetUtcNow();

        await AdvanceAsync(clock, SlowPathBudget, FpsCapSettler.QuietPollInterval, task);

        var outcome = await task.WaitAsync(TestBound);

        Assert.Equal(FpsCapSettleOutcome.Settled, outcome);
        Assert.Equal(new int?[] { 20 }, writer.Writes);
    }

    /// <summary>
    /// The launched client crashed, or Roblox folded the launch into an already-running instance --
    /// either way it never produces the write the proof-of-read gate is watching for. That gate must
    /// be BOUNDED, not blocking: once its own wait budget is spent, the settle proceeds to write the
    /// cap anyway rather than hang the launch indefinitely.
    /// </summary>
    [Fact]
    public async Task LaunchBaseline_ClientNeverWrites_ProceedsAnywayOnceBounded()
    {
        var clock = new ArmSignallingClock(DateTimeOffset.UnixEpoch);
        var baseline = clock.GetUtcNow();
        var probe = new TimeAwareProbe { Cap = 9999, Mtime = baseline };
        var writer = new TimeAwareWriter(probe, clock);

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None,
            launchBaselineUtc: baseline);

        // The "launched client" never writes -- probe.Mtime only ever moves from SettleAsync's own
        // eventual write (via TimeAwareWriter).
        await AdvanceAsync(clock, SlowPathBudget, FpsCapSettler.QuietPollInterval, task);

        var outcome = await task.WaitAsync(TestBound);

        Assert.Equal(FpsCapSettleOutcome.Settled, outcome);
        Assert.Equal(new int?[] { 20 }, writer.Writes);
    }

    [Fact]
    public async Task NeverSurvives_ExhaustsAttemptsAndStillReturns()
    {
        // Always reads back someone else's value: every attempt is clobbered.
        // 1 entry read + 1 re-read per attempt (MaxWriteAttempts = 3) = 4 consumed.
        var probe = new FakeProbe(9999, 9999, 9999, 9999);
        var clock = new ArmSignallingClock(DateTimeOffset.UnixEpoch);
        var writer = new RecordingWriter(probe, clock);

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None);

        await AdvanceAsync(clock, SlowPathBudget, FpsCapSettler.QuietPollInterval, task);

        var outcome = await task.WaitAsync(TestBound);

        // Exhausting attempts must NOT abort the launch — the caller proceeds with whatever we wrote.
        Assert.Equal(FpsCapSettleOutcome.Exhausted, outcome);
        Assert.Equal(FpsCapSettler.MaxWriteAttempts, writer.Writes.Count);
    }

    [Fact]
    public async Task WriterThrows_DegradesToWriteFailedRatherThanEscaping()
    {
        var probe = new FakeProbe(9999);
        var clock = new ArmSignallingClock(DateTimeOffset.UnixEpoch);
        var writer = new RecordingWriter(probe, clock) { Throw = new GlobalBasicSettingsWriteException("disk on fire") };

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None);

        await AdvanceAsync(clock, SlowPathBudget, FpsCapSettler.QuietPollInterval, task);

        var outcome = await task.WaitAsync(TestBound);

        Assert.Equal(FpsCapSettleOutcome.WriteFailed, outcome);
    }

    /// <summary>
    /// The fence on the 2026-09-11 pump fix. A pause inside the probe's read holds the settler in
    /// exactly the window the old signal released on -- after the mtime read, before the next
    /// <c>Task.Delay</c> is armed -- so the pump advances the clock while the settler is not yet
    /// parked. Against the old read-based signal that is a permanent deadlock: measured directly, a
    /// 5ms pause took 9 of this file's 11 tests to a 30s "Pump stalled" on an idle 16-core box with
    /// no parallelism and no load. Against the arm signal it cannot stall at all, because the pump
    /// does not move until the settler is parked for the next advance.
    /// <para>
    /// The 2ms is a deliberate REAL-time pause, and the only one in this file. It buys a race window
    /// wide enough to lose every time rather than occasionally; the settler needs ~50 pump steps to
    /// reach <c>QuietDebounce</c> on this fixture, so the whole test costs ~100ms of wall clock.
    /// This is the WriterThrows shape on purpose -- it is the fastest-completing slow-path fixture
    /// here, and the outcome it asserts is unrelated to the timing, so a regression shows up as the
    /// pump's own ceiling rather than as a confusing wrong-outcome failure.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ThePumpSurvivesAPreemptionBetweenTheReadAndTheRearm()
    {
        var probe = new FakeProbe(9999) { PostReadPause = TimeSpan.FromMilliseconds(2) };
        var clock = new ArmSignallingClock(DateTimeOffset.UnixEpoch);
        var writer = new RecordingWriter(probe, clock) { Throw = new GlobalBasicSettingsWriteException("disk on fire") };

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None);

        await AdvanceAsync(clock, SlowPathBudget, FpsCapSettler.QuietPollInterval, task);

        Assert.Equal(FpsCapSettleOutcome.WriteFailed, await task.WaitAsync(TestBound));
    }

    [Fact]
    public async Task FileKeepsChanging_QuietWaitTimesOutButStillWritesAndReturns()
    {
        var probe = new FakeProbe(9999, 20);
        var clock = new ArmSignallingClock(DateTimeOffset.UnixEpoch);
        var writer = new RecordingWriter(probe, clock);

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None);

        // Keep bumping the mtime so the file never goes quiet, past the overall settle budget.
        var elapsed = TimeSpan.Zero;
        while (elapsed < SlowPathBudget && !task.IsCompleted)
        {
            probe.Mtime = probe.Mtime!.Value + TimeSpan.FromMilliseconds(50);
            await PumpStepAsync(clock, FpsCapSettler.QuietPollInterval, task);
            elapsed += FpsCapSettler.QuietPollInterval;
        }

        var outcome = await task.WaitAsync(TestBound);

        // The pre-write wait burns almost the entire overall budget without ever going quiet, so
        // the post-write wait starts with the deadline already passed and returns instantly --
        // without ever having watched the file for a clobber. The scripted re-read (20) happens
        // to match, but a read microseconds after our own write is not a confirmation: this is
        // the exact shape of the original wrong-cap bug (fix 1), so it must NOT report Settled.
        // With the budget exhausted, the top-of-loop deadline check refuses a second attempt and
        // this falls straight to Exhausted -- correctly loud (LogError) instead of a false-clean
        // Settled that would have hidden a real clobber from a support bundle.
        Assert.Equal(FpsCapSettleOutcome.Exhausted, outcome);
        Assert.Single(writer.Writes);
    }

    [Fact]
    public async Task PermanentlyBusyFile_ExhaustsWithinTheOverallBudget_NotThreeFullTimeouts()
    {
        // Every quiet-wait times out (mtime never stops moving) AND every re-read comes back
        // wrong (never our value): the worst case for both dimensions at once. Before
        // SettleTimeout existed, this could run MaxWriteAttempts x (two QuietWaitTimeout-bounded
        // waits) = 3 x 60s = 180s. With the overall deadline, one attempt consumes the entire
        // budget and the second attempt's own top-of-loop check refuses to start.
        var probe = new FakeProbe(9999, 9999, 9999, 9999, 9999);
        var clock = new ArmSignallingClock(DateTimeOffset.UnixEpoch);
        var writer = new RecordingWriter(probe, clock);

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None);

        var pumpBudget = FpsCapSettler.SettleTimeout + TimeSpan.FromSeconds(3);
        var elapsed = TimeSpan.Zero;
        while (elapsed < pumpBudget && !task.IsCompleted)
        {
            probe.Mtime = probe.Mtime!.Value + TimeSpan.FromMilliseconds(50);
            await PumpStepAsync(clock, FpsCapSettler.QuietPollInterval, task);
            elapsed += FpsCapSettler.QuietPollInterval;
        }

        var outcome = await task.WaitAsync(TestBound);

        Assert.Equal(FpsCapSettleOutcome.Exhausted, outcome);
        // Only the first attempt ever got to start -- the second attempt's top-of-loop deadline
        // check refuses before doing any work.
        Assert.Single(writer.Writes);
        // The regression this guards: the old unbounded design could run ~93-180s here. The fake
        // clock is the source of truth for how much simulated time SettleAsync actually consumed.
        var settleElapsed = clock.GetUtcNow() - DateTimeOffset.UnixEpoch;
        Assert.True(
            settleElapsed <= FpsCapSettler.SettleTimeout + TimeSpan.FromSeconds(1),
            $"Settle consumed {settleElapsed}, expected at most {FpsCapSettler.SettleTimeout} + 1s slack.");
    }
}
