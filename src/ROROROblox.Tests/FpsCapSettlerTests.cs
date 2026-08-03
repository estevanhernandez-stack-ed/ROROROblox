using System;
using System.Collections.Generic;
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
    /// (20s) is the real ceiling on any single settle call regardless of how many quiet-waits or
    /// retries it takes internally, so advancing a little past it is always enough headroom and
    /// never needs re-deriving per test.
    /// </summary>
    private static readonly TimeSpan SlowPathBudget = FpsCapSettler.SettleTimeout + TimeSpan.FromSeconds(2);

    /// <summary>Scripted read side. Each ReadFramerateCap() pops the next scripted value.</summary>
    private sealed class FakeProbe : IGlobalBasicSettingsProbe
    {
        private readonly Queue<int?> _caps;
        public int ReadCalls { get; private set; }
        public DateTimeOffset? Mtime { get; set; } = DateTimeOffset.UnixEpoch;

        public FakeProbe(params int?[] caps) => _caps = new Queue<int?>(caps);

        public int? ReadFramerateCap()
        {
            ReadCalls++;
            return _caps.Count > 0 ? _caps.Dequeue() : null;
        }

        public DateTimeOffset? GetLastWriteTimeUtc() => Mtime;
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
    }

    /// <summary>
    /// Advance the fake clock, yielding between steps so each awaiting continuation gets to arm
    /// its next timer before the clock moves again. Advancing in one jump can leave a later timer
    /// armed against a clock that has stopped moving — a permanent stall, not a slow test.
    /// </summary>
    private static async Task AdvanceAsync(FakeTimeProvider clock, TimeSpan total, TimeSpan step)
    {
        var elapsed = TimeSpan.Zero;
        while (elapsed < total)
        {
            clock.Advance(step);
            elapsed += step;
            for (var i = 0; i < 8; i++) { await Task.Yield(); }
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

        await AdvanceAsync(clock, SlowPathBudget, FpsCapSettler.QuietPollInterval);

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

        await AdvanceAsync(clock, SlowPathBudget, FpsCapSettler.QuietPollInterval);

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

            clock.Advance(FpsCapSettler.QuietPollInterval);
            elapsed += FpsCapSettler.QuietPollInterval;
            for (var i = 0; i < 8; i++) { await Task.Yield(); }
        }

        Assert.True(clobbered, "test setup bug: the clobber injection point was never reached");

        var outcome = await task.WaitAsync(TestBound);

        Assert.Equal(FpsCapSettleOutcome.Settled, outcome);
        // The clobber landing mid-window must force a genuine retry -- two writes, not one. A
        // settler that trusted the read right after the write (Fix 1's bug) or that never watched
        // for the mid-window clobber at all would settle on attempt 1 with a single write.
        Assert.Equal(new int?[] { 20, 20 }, writer.Writes);
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

        await AdvanceAsync(clock, SlowPathBudget, FpsCapSettler.QuietPollInterval);

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

        await AdvanceAsync(clock, SlowPathBudget, FpsCapSettler.QuietPollInterval);

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
        while (elapsed < SlowPathBudget)
        {
            probe.Mtime = probe.Mtime!.Value + TimeSpan.FromMilliseconds(50);
            clock.Advance(FpsCapSettler.QuietPollInterval);
            elapsed += FpsCapSettler.QuietPollInterval;
            for (var i = 0; i < 8; i++) { await Task.Yield(); }
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
            clock.Advance(FpsCapSettler.QuietPollInterval);
            elapsed += FpsCapSettler.QuietPollInterval;
            for (var i = 0; i < 8; i++) { await Task.Yield(); }
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
