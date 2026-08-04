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

public sealed class FpsCapSettlerTests
{
    private static readonly TimeSpan TestBound = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Generous pump budget for tests that let the slow path run to completion. SettleTimeout
    /// (45s) is the real ceiling on any single settle call regardless of how many quiet-waits or
    /// retries it takes internally, so advancing a little past it is always enough headroom and
    /// never needs re-deriving per test.
    /// </summary>
    private static readonly TimeSpan SlowPathBudget = FpsCapSettler.SettleTimeout + TimeSpan.FromSeconds(2);

    /// <summary>
    /// Tracks how many times a probe's <c>GetLastWriteTimeUtc()</c> has been called, and lets a
    /// waiter block on the NEXT call without polling. <see cref="ReadCount"/> is the authoritative
    /// signal <see cref="PumpStepAsync"/> checks (a plain <c>Interlocked</c>-guarded counter, read
    /// back via <see cref="Volatile.Read(ref int)"/>); <see cref="WaitAsync"/> is purely a wakeup
    /// so the pump doesn't have to busy-spin to notice a change -- see the "why not Task.Yield()"
    /// remarks on <see cref="PumpStepAsync"/> for why that distinction matters.
    /// <para>
    /// A stale/unconsumed release from a read that happened while nobody was waiting on it just
    /// causes one extra harmless wakeup on the NEXT wait call, which immediately re-checks
    /// <see cref="ReadCount"/> against the caller's own baseline and loops if it hasn't actually
    /// moved -- no explicit draining needed.
    /// </para>
    /// </summary>
    private sealed class MtimeReadTracker
    {
        private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
        private int _count;

        public int ReadCount => Volatile.Read(ref _count);

        public void RecordRead()
        {
            Interlocked.Increment(ref _count);
            _signal.Release();
        }

        public Task<bool> WaitAsync(TimeSpan timeout) => _signal.WaitAsync(timeout);
    }

    /// <summary>Scripted read side. Each ReadFramerateCap() pops the next scripted value.</summary>
    private sealed class FakeProbe : IGlobalBasicSettingsProbe
    {
        private readonly Queue<int?> _caps;
        public int ReadCalls { get; private set; }
        public DateTimeOffset? Mtime { get; set; } = DateTimeOffset.UnixEpoch;

        /// <summary>See <see cref="MtimeReadTracker"/> -- the pump's proof that the settler's
        /// delay continuation actually resumed and re-polled after a clock advance.</summary>
        public MtimeReadTracker MtimeReads { get; } = new();

        public FakeProbe(params int?[] caps) => _caps = new Queue<int?>(caps);

        public int? ReadFramerateCap()
        {
            ReadCalls++;
            return _caps.Count > 0 ? _caps.Dequeue() : null;
        }

        public DateTimeOffset? GetLastWriteTimeUtc()
        {
            MtimeReads.RecordRead();
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

        /// <summary>See <see cref="MtimeReadTracker"/> -- same role, same reasoning as on <see cref="FakeProbe"/>.</summary>
        public MtimeReadTracker MtimeReads { get; } = new();

        public int? ReadFramerateCap() { ReadCalls++; return Cap; }

        public DateTimeOffset? GetLastWriteTimeUtc()
        {
            MtimeReads.RecordRead();
            return Mtime;
        }
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
    /// </summary>
    private static readonly TimeSpan PumpObservationCeiling = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Advance the fake clock by exactly one step, then block until the settler has PROVABLY seen
    /// it -- either its <c>GetLastWriteTimeUtc()</c> probe read count has moved (proof the delay
    /// continuation actually resumed and re-polled) or the settle task itself has completed.
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
    /// <b>Waits on <see cref="MtimeReadTracker.WaitAsync"/> (a <c>SemaphoreSlim</c>), not a
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
    /// and re-issues <c>clock.Advance(TimeSpan.Zero)</c> between them. <c>FakeTimeProvider</c>'s own
    /// source (read directly, not assumed) shows <c>Advance</c>/<c>AddWaiter</c> are correctly
    /// lock-protected, so this is NOT compensating for a confirmed library bug -- it is a cheap,
    /// side-effect-free defensive nudge (advancing by zero cannot skew any test's elapsed-fake-time
    /// assertions) against the possibility that this specific pairing hits an edge this reading
    /// missed. The 25/25-clean isolated run below is the actual evidence the core signal is sound;
    /// this nudge is belt-and-suspenders, not the fix.
    /// </para>
    /// </summary>
    private static async Task PumpStepAsync(
        FakeTimeProvider clock, TimeSpan step, Task settleTask, MtimeReadTracker mtimeReads)
    {
        var before = mtimeReads.ReadCount;
        clock.Advance(step);

        var budget = Stopwatch.StartNew();
        while (mtimeReads.ReadCount == before && !settleTask.IsCompleted)
        {
            var observed = await mtimeReads.WaitAsync(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
            if (observed || mtimeReads.ReadCount != before || settleTask.IsCompleted)
            {
                break;
            }

            if (budget.Elapsed > PumpObservationCeiling)
            {
                Assert.Fail(
                    $"Pump stalled: the settler never re-read GetLastWriteTimeUtc() within " +
                    $"{PumpObservationCeiling} of real time after the fake clock advanced by {step}. " +
                    "The code under test's continuation never resumed -- a genuine hang, not a slow test.");
            }

            clock.Advance(TimeSpan.Zero);
        }
    }

    /// <summary>
    /// Advance the fake clock in <paramref name="step"/> increments up to <paramref name="total"/>,
    /// pumping each step through <see cref="PumpStepAsync"/> so the clock never outruns
    /// <paramref name="settleTask"/>. Stops early once the settle task completes -- further
    /// advances past that point serve nothing.
    /// </summary>
    private static async Task AdvanceAsync(
        FakeTimeProvider clock, TimeSpan total, TimeSpan step, Task settleTask, MtimeReadTracker mtimeReads)
    {
        var elapsed = TimeSpan.Zero;
        while (elapsed < total && !settleTask.IsCompleted)
        {
            await PumpStepAsync(clock, step, settleTask, mtimeReads);
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
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
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
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var writer = new RecordingWriter(probe, clock);

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None);

        await AdvanceAsync(clock, SlowPathBudget, FpsCapSettler.QuietPollInterval, task, probe.MtimeReads);

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
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var writer = new RecordingWriter(probe, clock);

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None);

        await AdvanceAsync(clock, SlowPathBudget, FpsCapSettler.QuietPollInterval, task, probe.MtimeReads);

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
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
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

            await PumpStepAsync(clock, FpsCapSettler.QuietPollInterval, task, probe.MtimeReads);
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
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
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
        await AdvanceAsync(clock, noWriteWindow, FpsCapSettler.QuietPollInterval, task, probe.MtimeReads);

        Assert.Empty(writer.Writes);

        // The launched client finally produces its first write-back -- proof it read the file --
        // at this scripted fake-clock instant. Mutated directly on the probe (not through `writer`,
        // which only records OUR writes): this models a DIFFERENT process.
        probe.Cap = 9999;
        probe.Mtime = clock.GetUtcNow();

        await AdvanceAsync(clock, SlowPathBudget, FpsCapSettler.QuietPollInterval, task, probe.MtimeReads);

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
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var baseline = clock.GetUtcNow();
        var probe = new TimeAwareProbe { Cap = 9999, Mtime = baseline };
        var writer = new TimeAwareWriter(probe, clock);

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None,
            launchBaselineUtc: baseline);

        // The "launched client" never writes -- probe.Mtime only ever moves from SettleAsync's own
        // eventual write (via TimeAwareWriter).
        await AdvanceAsync(clock, SlowPathBudget, FpsCapSettler.QuietPollInterval, task, probe.MtimeReads);

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
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var writer = new RecordingWriter(probe, clock);

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None);

        await AdvanceAsync(clock, SlowPathBudget, FpsCapSettler.QuietPollInterval, task, probe.MtimeReads);

        var outcome = await task.WaitAsync(TestBound);

        // Exhausting attempts must NOT abort the launch — the caller proceeds with whatever we wrote.
        Assert.Equal(FpsCapSettleOutcome.Exhausted, outcome);
        Assert.Equal(FpsCapSettler.MaxWriteAttempts, writer.Writes.Count);
    }

    [Fact]
    public async Task WriterThrows_DegradesToWriteFailedRatherThanEscaping()
    {
        var probe = new FakeProbe(9999);
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var writer = new RecordingWriter(probe, clock) { Throw = new GlobalBasicSettingsWriteException("disk on fire") };

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None);

        await AdvanceAsync(clock, SlowPathBudget, FpsCapSettler.QuietPollInterval, task, probe.MtimeReads);

        var outcome = await task.WaitAsync(TestBound);

        Assert.Equal(FpsCapSettleOutcome.WriteFailed, outcome);
    }

    [Fact]
    public async Task FileKeepsChanging_QuietWaitTimesOutButStillWritesAndReturns()
    {
        var probe = new FakeProbe(9999, 20);
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var writer = new RecordingWriter(probe, clock);

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None);

        // Keep bumping the mtime so the file never goes quiet, past the overall settle budget.
        var elapsed = TimeSpan.Zero;
        while (elapsed < SlowPathBudget && !task.IsCompleted)
        {
            probe.Mtime = probe.Mtime!.Value + TimeSpan.FromMilliseconds(50);
            await PumpStepAsync(clock, FpsCapSettler.QuietPollInterval, task, probe.MtimeReads);
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
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var writer = new RecordingWriter(probe, clock);

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None);

        var pumpBudget = FpsCapSettler.SettleTimeout + TimeSpan.FromSeconds(3);
        var elapsed = TimeSpan.Zero;
        while (elapsed < pumpBudget && !task.IsCompleted)
        {
            probe.Mtime = probe.Mtime!.Value + TimeSpan.FromMilliseconds(50);
            await PumpStepAsync(clock, FpsCapSettler.QuietPollInterval, task, probe.MtimeReads);
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
