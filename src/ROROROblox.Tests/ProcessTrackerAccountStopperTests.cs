using ROROROblox.App.Plugins.Adapters;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// Locks the escalation the plugin-invoked stop was missing (F-111, plugin path).
///
/// <para>The adapter shipped as <c>RequestClose(id) || Kill(id)</c> — the exact guard F-111
/// measured as inert. <c>RequestClose</c> answers "was there a window to ask", never "did it
/// close", so for an in-game client it returns true, the short-circuit skips <c>Kill</c>, and
/// the client is left running behind Roblox's own confirm dialog. Measured live 2026-08-22:
/// alive and growing 90 seconds after a stop the tool reported as issued.</para>
///
/// <para>These tests are written against a tracker that behaves the way a real in-game client
/// behaves. The register's warning on F-111 applies here too: a test written against a client
/// that exits on the first ask passes while the bug is still there.</para>
/// </summary>
public class ProcessTrackerAccountStopperTests
{
    [Fact]
    public async Task AClientThatIgnoresBothAsksIsForced()
    {
        // Never exits, however many times we ask — an in-game client waiting on a dialog
        // no one will ever click.
        var tracker = new FakeTracker(exitsAfterAsks: int.MaxValue);
        var stopper = NewStopper(tracker);

        await stopper.RunStopSequenceAsync(FakeTracker.Id, ClientStopSequence.DefaultGrace);

        Assert.True(tracker.AskCount >= 1);
        Assert.True(tracker.KillCalled,
            "The grace expired with the client still alive and Kill never ran — this is the F-111 " +
            "guard, which trusts RequestClose's return value instead of asking whether it exited.");
    }

    [Fact]
    public async Task AClientThatClosesOnTheSecondAskIsNeverKilled()
    {
        // The ordinary path F-111 bought: SC_CLOSE raises Roblox's confirm, a second ask
        // dismisses it into a clean exit, and Roblox persists its own settings.
        var tracker = new FakeTracker(exitsAfterAsks: 2);
        var stopper = NewStopper(tracker);

        var outcome = await stopper.RunStopSequenceAsync(FakeTracker.Id, ClientStopSequence.DefaultGrace);

        Assert.Equal(ClientStopOutcome.ClosedItself, outcome);
        Assert.False(tracker.KillCalled,
            "A clean exit was forced anyway — that discards the Roblox settings the clean exit exists to save.");
    }

    [Fact]
    public void AnUnparseableIdIsFalseNotAThrow()
        => Assert.False(NewStopper(new FakeTracker(0)).StopAccount("not-a-guid"));

    [Fact]
    public void AnUntrackedAccountIsFalse()
        => Assert.False(NewStopper(new FakeTracker(0) { Tracking = false }).StopAccount(FakeTracker.Id.ToString()));

    [Fact]
    public void StopAccountReportsIssuedAndStartsTheSequence()
    {
        var tracker = new FakeTracker(exitsAfterAsks: 1);
        var stopper = NewStopper(tracker);

        Assert.True(stopper.StopAccount(FakeTracker.Id.ToString()));
        Assert.NotNull(stopper.InFlightFor(FakeTracker.Id));
    }

    /// <summary>Injects an instant delay so the ten-second grace costs nothing in test time.</summary>
    private static ProcessTrackerAccountStopper NewStopper(FakeTracker tracker)
        => new(tracker, settings: null, log: null, delay: (_, _) => Task.CompletedTask);

    private sealed class FakeTracker : IRobloxProcessTracker
    {
        public static readonly Guid Id = Guid.Parse("11111111-2222-3333-4444-555555555555");

        private readonly int _exitsAfterAsks;
        public FakeTracker(int exitsAfterAsks) => _exitsAfterAsks = exitsAfterAsks;

        public bool Tracking { get; set; } = true;
        public int AskCount { get; private set; }
        public bool KillCalled { get; private set; }

        public bool IsTracking(Guid accountId) => Tracking;

        // Returns TRUE the way the real one does: we had a window to ask. It says nothing
        // about whether the client closed, which is the whole of F-111.
        public bool RequestClose(Guid accountId)
        {
            AskCount++;
            if (AskCount >= _exitsAfterAsks) Tracking = false;
            return true;
        }

        public bool Kill(Guid accountId)
        {
            KillCalled = true;
            Tracking = false;
            return true;
        }

        public IReadOnlyDictionary<Guid, TrackedProcess> Attached =>
            Tracking
                ? new Dictionary<Guid, TrackedProcess> { [Id] = new(1234, DateTimeOffset.UnixEpoch) }
                : new Dictionary<Guid, TrackedProcess>();

        public Task TrackLaunchAsync(Guid accountId, DateTimeOffset launchedAtUtc, CancellationToken ct = default)
            => Task.CompletedTask;
        public bool AttachExisting(Guid accountId, int pid) => throw new NotImplementedException();

        public event EventHandler<RobloxProcessEventArgs>? ProcessAttached;
        public event EventHandler<RobloxProcessEventArgs>? ProcessAttachFailed;
        public event EventHandler<RobloxProcessEventArgs>? ProcessExited;
    }
}
