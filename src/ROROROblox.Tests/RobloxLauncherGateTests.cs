using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// The gate exists so a launched client finishes reading its per-account settings before the
/// NEXT launch overwrites the shared settings files. The old fixed 250ms hold was measured from
/// Process.Start returning on a roblox-player: URI — i.e. from Windows accepting the protocol
/// invocation, BEFORE RobloxPlayerBeta exists. Observed live 2026-08-01: two accounts launched
/// ~1s apart, and the first ran with the second's FPS cap.
/// </summary>
public class RobloxLauncherGateTests
{
    /// <summary>Returns a scripted sequence of pid snapshots, one per call.</summary>
    private sealed class ScriptedProbe : IRobloxRunningProbe
    {
        private readonly Queue<int[]> _snapshots;
        public int Calls { get; private set; }
        public ScriptedProbe(params int[][] snapshots) => _snapshots = new Queue<int[]>(snapshots);
        public IReadOnlyList<int> GetRunningPlayerPids()
        {
            Calls++;
            // Last snapshot repeats forever once exhausted.
            return _snapshots.Count > 1 ? _snapshots.Dequeue() : _snapshots.Peek();
        }
        public IReadOnlyList<RobloxProcessInfo> GetRunningPlayers()
            => throw new NotSupportedException("gate only uses GetRunningPlayerPids");
    }

    [Fact]
    public async Task NewClientAppearing_ReleasesTheGateWithoutWaitingTheFullTimeout()
    {
        var clock = new FakeTimeProvider();
        // before: {100}. Then still {100}. Then {100, 555} — the new client.
        var probe = new ScriptedProbe(new[] { 100 }, new[] { 100 }, new[] { 100, 555 });

        var wait = RobloxLauncher.WaitForNewClientAsync(
            probe, before: new HashSet<int> { 100 }, clock, CancellationToken.None);

        // Task.Delay(TimeSpan, TimeProvider, ct) completes its Task synchronously when
        // Advance() passes its due time, but the AWAITING continuation (the rest of the poll
        // loop) resumes asynchronously, not inline within Advance(). Firing every Advance()
        // back-to-back would race the loop: the 2nd/3rd calls would land before the loop even
        // reached its next await, so the poll timer they were meant to satisfy wouldn't exist
        // yet — and a timer created afterward is due relative to a clock that never moves
        // again, so it would never fire. Pumping between advances lets each iteration reach
        // its next await (and arm the next timer against the still-advancing clock) first.
        await AdvancePastPollAsync(clock, probe, expectAtLeastCalls: 2);
        await AdvancePastPollAsync(clock, probe, expectAtLeastCalls: 3);
        clock.Advance(RobloxLauncher.SettleGrace);

        var outcome = await wait;
        Assert.Equal(NewClientWaitOutcome.Detected, outcome);
    }

    /// <summary>
    /// Advances the fake clock by one poll interval, then yields (bounded) until the probe has
    /// been called at least <paramref name="expectAtLeastCalls"/> times — i.e. until the poll
    /// loop's queued continuation has actually run and reached its next await point. See the
    /// comment at the call site for why a bare Advance() isn't enough for a multi-iteration wait.
    /// </summary>
    private static async Task AdvancePastPollAsync(FakeTimeProvider clock, ScriptedProbe probe, int expectAtLeastCalls)
    {
        clock.Advance(RobloxLauncher.NewClientPollInterval);
        for (var i = 0; i < 50 && probe.Calls < expectAtLeastCalls; i++)
        {
            await Task.Yield();
        }
    }

    [Fact]
    public async Task NoNewClientEver_ReleasesAtTheTimeoutRatherThanHanging()
    {
        var clock = new FakeTimeProvider();
        var probe = new ScriptedProbe(new[] { 100 });   // never changes

        var wait = RobloxLauncher.WaitForNewClientAsync(
            probe, before: new HashSet<int> { 100 }, clock, CancellationToken.None);

        clock.Advance(RobloxLauncher.NewClientWaitTimeout + TimeSpan.FromSeconds(1));

        var outcome = await wait;
        Assert.Equal(NewClientWaitOutcome.TimedOut, outcome);
    }

    [Fact]
    public async Task PreExistingPids_AreNotMistakenForTheNewClient()
    {
        var clock = new FakeTimeProvider();
        // Three windowless orphans Roblox left behind, present before AND after. No new client.
        var orphans = new[] { 14392, 20432, 48276 };
        var probe = new ScriptedProbe(orphans);

        var wait = RobloxLauncher.WaitForNewClientAsync(
            probe, before: new HashSet<int>(orphans), clock, CancellationToken.None);

        clock.Advance(RobloxLauncher.NewClientWaitTimeout + TimeSpan.FromSeconds(1));

        Assert.Equal(NewClientWaitOutcome.TimedOut, await wait);
    }

    [Fact]
    public async Task ProbeThrowing_DoesNotEscape_AndDegradesToTimeout()
    {
        var clock = new FakeTimeProvider();
        var probe = new ThrowingProbe();

        var wait = RobloxLauncher.WaitForNewClientAsync(
            probe, before: new HashSet<int>(), clock, CancellationToken.None);

        clock.Advance(RobloxLauncher.NewClientWaitTimeout + TimeSpan.FromSeconds(1));

        Assert.Equal(NewClientWaitOutcome.TimedOut, await wait);
    }

    private sealed class ThrowingProbe : IRobloxRunningProbe
    {
        public IReadOnlyList<int> GetRunningPlayerPids() => throw new InvalidOperationException("probe blew up");
        public IReadOnlyList<RobloxProcessInfo> GetRunningPlayers() => throw new NotSupportedException();
    }
}
