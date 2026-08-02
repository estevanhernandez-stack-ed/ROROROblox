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

    private sealed class RecordingWriter : IGlobalBasicSettingsWriter
    {
        public List<int?> Writes { get; } = new();
        public Exception? Throw { get; set; }

        public Task WriteFramerateCapAsync(int? fps, CancellationToken ct = default)
        {
            if (Throw is not null) { throw Throw; }
            Writes.Add(fps);
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
        var writer = new RecordingWriter();
        // FakeTimeProvider()'s parameterless ctor starts at 2000-01-01, not DateTimeOffset.UnixEpoch.
        // Pin it to UnixEpoch explicitly so it agrees with FakeProbe.Mtime's default below and the
        // "no time passed" assertion is checking something real, not an unrelated ctor default.
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);

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
        // read 2: after the confirm window, our 20 is still there -> settled
        var probe = new FakeProbe(9999, 20);
        var writer = new RecordingWriter();
        var clock = new FakeTimeProvider();

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None);

        await AdvanceAsync(clock,
            FpsCapSettler.QuietDebounce + FpsCapSettler.WriteConfirmWindow + TimeSpan.FromSeconds(1),
            FpsCapSettler.QuietPollInterval);

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
        var writer = new RecordingWriter();
        var clock = new FakeTimeProvider();

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None);

        await AdvanceAsync(clock,
            (FpsCapSettler.QuietDebounce + FpsCapSettler.WriteConfirmWindow) * 3,
            FpsCapSettler.QuietPollInterval);

        var outcome = await task.WaitAsync(TestBound);

        Assert.Equal(FpsCapSettleOutcome.Settled, outcome);
        Assert.Equal(2, writer.Writes.Count);
    }

    [Fact]
    public async Task NeverSurvives_ExhaustsAttemptsAndStillReturns()
    {
        // Always reads back someone else's value: every attempt is clobbered.
        var probe = new FakeProbe(9999, 9999, 9999, 9999, 9999, 9999, 9999, 9999);
        var writer = new RecordingWriter();
        var clock = new FakeTimeProvider();

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None);

        await AdvanceAsync(clock,
            (FpsCapSettler.QuietDebounce + FpsCapSettler.WriteConfirmWindow) * (FpsCapSettler.MaxWriteAttempts + 2),
            FpsCapSettler.QuietPollInterval);

        var outcome = await task.WaitAsync(TestBound);

        // Exhausting attempts must NOT abort the launch — the caller proceeds with whatever we wrote.
        Assert.Equal(FpsCapSettleOutcome.Exhausted, outcome);
        Assert.Equal(FpsCapSettler.MaxWriteAttempts, writer.Writes.Count);
    }

    [Fact]
    public async Task WriterThrows_DegradesToWriteFailedRatherThanEscaping()
    {
        var probe = new FakeProbe(9999);
        var writer = new RecordingWriter { Throw = new GlobalBasicSettingsWriteException("disk on fire") };
        var clock = new FakeTimeProvider();

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None);

        await AdvanceAsync(clock,
            FpsCapSettler.QuietDebounce + TimeSpan.FromSeconds(1),
            FpsCapSettler.QuietPollInterval);

        var outcome = await task.WaitAsync(TestBound);

        Assert.Equal(FpsCapSettleOutcome.WriteFailed, outcome);
    }

    [Fact]
    public async Task FileKeepsChanging_QuietWaitTimesOutButStillWritesAndReturns()
    {
        var probe = new FakeProbe(9999, 20);
        var writer = new RecordingWriter();
        var clock = new FakeTimeProvider();

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None);

        // Keep bumping the mtime so the file never goes quiet, past the timeout.
        var elapsed = TimeSpan.Zero;
        var budget = FpsCapSettler.QuietWaitTimeout + FpsCapSettler.WriteConfirmWindow + TimeSpan.FromSeconds(2);
        while (elapsed < budget)
        {
            probe.Mtime = probe.Mtime!.Value + TimeSpan.FromMilliseconds(50);
            clock.Advance(FpsCapSettler.QuietPollInterval);
            elapsed += FpsCapSettler.QuietPollInterval;
            for (var i = 0; i < 8; i++) { await Task.Yield(); }
        }

        var outcome = await task.WaitAsync(TestBound);

        // A contended file must not block the launch forever.
        Assert.Equal(FpsCapSettleOutcome.Settled, outcome);
        Assert.Single(writer.Writes);
    }
}
