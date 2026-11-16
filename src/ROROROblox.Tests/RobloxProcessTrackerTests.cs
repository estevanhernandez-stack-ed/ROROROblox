using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// Coverage of <see cref="RobloxProcessTracker"/>'s attach-timeout behavior — specifically the
/// v1.7.0 install-deferral rider (spec §"Components > Riders > 6. Install-aware tracker
/// attach-timeout"): when <c>RobloxPlayerInstaller.exe</c> is running during the attach wait, the
/// tracker extends its deadline (to the ~120s defender-matching cap) so an install-delayed
/// <c>RobloxPlayerBeta</c> still attaches instead of firing a false <c>ProcessAttachFailed</c>.
///
/// Two seams keep this deterministic and Roblox-free:
///  - <c>isInstallerRunning</c> (<c>Func&lt;bool&gt;</c>) — the install signal (item 1's
///    <see cref="RobloxUpdateProbe.IsInstallerRunning"/> shape), injected straight.
///  - <c>candidateProcesses</c> (<c>Func&lt;IReadOnlyList&lt;Process&gt;&gt;</c>) — the raw
///    candidate-process source. The default scans real <c>RobloxPlayerBeta</c> processes; tests
///    reveal real-but-benign child processes at a controlled wall-clock threshold so the genuine
///    attach path (EnableRaisingEvents + Exited) AND the tracker's own claim/FIFO/start-time
///    filtering run without touching a real Roblox client.
/// </summary>
public sealed class RobloxProcessTrackerTests : IDisposable
{
    private readonly List<Process> _spawned = new();

    public void Dispose()
    {
        foreach (var t in _trackers)
        {
            try { t.Dispose(); } catch { }
        }
        foreach (var p in _spawned)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
            try { p.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// Spawn a real, long-lived, benign child process so the tracker's attach path (which needs a
    /// live OS process for EnableRaisingEvents + the Exited hook) has something genuine to attach to.
    /// Returns its pid; fresh handles are minted per poll so the tracker can dispose freely.
    /// </summary>
    private int SpawnSleeperPid()
    {
        // ping loops ~30s without needing stdin; long enough for any of these short-timeout tests.
        var psi = new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 > nul")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        var p = Process.Start(psi)!;
        _spawned.Add(p);
        return p.Id;
    }

    private static IReadOnlyList<Process> ByIds(params int[] ids)
    {
        var list = new List<Process>();
        foreach (var id in ids)
        {
            try { list.Add(Process.GetProcessById(id)); }
            catch { /* exited between polls — skip */ }
        }
        return list;
    }

    /// <summary>
    /// Time the test controls, instead of time the test races (F-116).
    /// <para>
    /// Both deadline tests below blocked a merge on 2026-08-21 and both passed on re-run. They were
    /// measuring the CI runner: the tracker read <c>DateTimeOffset.UtcNow</c> and slept on
    /// <c>Task.Delay</c>, so a stalled agent could burn the whole extended cap between two polls.
    /// Advancing a fake clock from the wait itself makes the loop's arithmetic exact and its
    /// duration irrelevant — the assertions become claims about the DECISION rather than about how
    /// busy the machine was. No timeout was lengthened to achieve it; lengthening one would have
    /// hidden the sensitivity rather than removed it.
    /// </para>
    /// </summary>
    private sealed class VirtualTime : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

        /// <summary>How many times the tracker waited. The poll count IS the loop's progress.</summary>
        public int Waits { get; private set; }

        public Task Advance(TimeSpan by, CancellationToken ct)
        {
            Waits++;
            UtcNow += by;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task InstallerRunning_ExtendsDeadline_AttachesLateRpb_NoAttachFailed()
    {
        // Base timeout 200ms, extended cap 1s, fast poll. The "RPB" only appears at ~400ms — past
        // the base 200ms deadline — so without the extension the tracker would have already failed.
        var rpbPid = SpawnSleeperPid();
        var time = new VirtualTime();
        var trackStart = time.UtcNow;

        // Revealed after 8 polls — 400ms of VIRTUAL time, past the 200ms base deadline and inside
        // the 1s extension. Keyed to the poll count rather than to a stopwatch, so a runner that
        // stalls for a second between polls changes nothing.
        IReadOnlyList<Process> Candidates() =>
            time.Waits >= 8 ? ByIds(rpbPid) : Array.Empty<Process>();

        using var tracker = new RobloxProcessTracker(
            log: NullLogger<RobloxProcessTracker>.Instance,
            attachTimeout: TimeSpan.FromMilliseconds(200),
            installerExtendedTimeout: TimeSpan.FromSeconds(1),
            pollInterval: TimeSpan.FromMilliseconds(50),
            isInstallerRunning: () => true,
            candidateProcesses: Candidates,
            clock: time,
            delay: time.Advance);

        var accountId = Guid.NewGuid();
        var attached = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = false;
        tracker.ProcessAttached += (_, e) => attached.TrySetResult(e.Pid);
        tracker.ProcessAttachFailed += (_, _) => failed = true;

        await tracker.TrackLaunchAsync(accountId, trackStart);

        Assert.True(tracker.IsTracking(accountId), "Tracker should have attached the install-delayed RPB.");
        var pid = await attached.Task.WaitAsync(TestWaits.Liveness);
        Assert.Equal(rpbPid, pid);
        Assert.False(failed, "ProcessAttachFailed must NOT fire when the installer is running and the RPB eventually appears.");
    }

    [Fact]
    public async Task InstallerAbsent_GivesUpAtBaseTimeout_FiresAttachFailed()
    {
        // No installer, RPB never appears: unchanged behavior — give up at the base timeout and fail.
        //
        // THE THIRD TEST IN THIS FILE TO NEED VIRTUAL TIME, and it blocked PR #126 while the two
        // beside it were already fixed. Its assertion was an UPPER bound on a Stopwatch — "gave up
        // in under 2 seconds" — which is the falsifiable kind: load can only make elapsed larger,
        // so a stalled runner reports correct behaviour as a failure. It read 3026ms against a
        // 200ms base timeout, which measures the agent, not the tracker.
        var time = new VirtualTime();
        var start = time.UtcNow;
        using var tracker = new RobloxProcessTracker(
            log: NullLogger<RobloxProcessTracker>.Instance,
            attachTimeout: TimeSpan.FromMilliseconds(200),
            installerExtendedTimeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(50),
            isInstallerRunning: () => false,
            candidateProcesses: Array.Empty<Process>,
            clock: time,
            delay: time.Advance);

        var accountId = Guid.NewGuid();
        var failed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        tracker.ProcessAttachFailed += (_, _) => failed.TrySetResult(true);

        await tracker.TrackLaunchAsync(accountId, start);
        var waited = time.UtcNow - start;

        Assert.True(await failed.Task.WaitAsync(TestWaits.Liveness),
            "ProcessAttachFailed should fire when no installer is running and no RPB appears.");
        Assert.False(tracker.IsTracking(accountId));

        // Virtual, so this is a statement about the loop rather than the machine — and it can now be
        // tight. The old bound was 2 SECONDS against a 200ms timeout, ten times the real limit,
        // because it had to survive a loaded agent. This is the base timeout plus one poll, which is
        // what the code actually promises, and it catches an accidental extension of even one tick.
        Assert.True(waited <= TimeSpan.FromMilliseconds(250),
            $"Gave up too late ({waited.TotalMilliseconds}ms of virtual time) — base-timeout behavior should be unextended when no installer runs.");
    }

    [Fact]
    public async Task InstallerRunning_ButRpbNeverAppears_BoundedByExtendedCap_ThenFails()
    {
        // Installer runs the whole time but the RPB never shows. The extension must be BOUNDED:
        // it waits to the extended cap (~600ms here), then fails — it does not wait forever.
        var cap = TimeSpan.FromMilliseconds(600);
        var time = new VirtualTime();
        var start = time.UtcNow;
        using var tracker = new RobloxProcessTracker(
            log: NullLogger<RobloxProcessTracker>.Instance,
            attachTimeout: TimeSpan.FromMilliseconds(150),
            installerExtendedTimeout: cap,
            pollInterval: TimeSpan.FromMilliseconds(50),
            isInstallerRunning: () => true,
            candidateProcesses: Array.Empty<Process>,
            clock: time,
            delay: time.Advance);

        var accountId = Guid.NewGuid();
        var failed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        tracker.ProcessAttachFailed += (_, _) => failed.TrySetResult(true);

        await tracker.TrackLaunchAsync(accountId, start);
        var waited = time.UtcNow - start;

        Assert.True(await failed.Task.WaitAsync(TestWaits.Liveness),
            "Even with the installer running, the tracker must eventually fail at the extended cap when no RPB appears.");
        Assert.False(tracker.IsTracking(accountId));

        // VIRTUAL elapsed, so these are statements about the loop's arithmetic rather than about
        // the machine. It DID extend past the base 150ms (the extension engaged)...
        Assert.True(waited >= TimeSpan.FromMilliseconds(450),
            $"Extension didn't engage — bailed after {waited.TotalMilliseconds}ms of virtual time, near the base timeout.");

        // ...and did NOT run past the cap. The old bound was 1800ms against a 600ms cap, three times
        // the real limit, because it had to tolerate a loaded runner; virtual time needs no slack, so
        // this now asserts the cap the code actually promises plus one poll.
        Assert.True(waited <= cap + TimeSpan.FromMilliseconds(50),
            $"Extension was not bounded — ran {waited.TotalMilliseconds}ms of virtual time past the {cap.TotalMilliseconds}ms cap.");
    }

    [Fact]
    public async Task NormalLaunch_NoInstaller_AttachesEarlyRpb_Unchanged()
    {
        // Sanity that the seam doesn't regress the happy path: no installer, RPB present immediately.
        var rpbPid = SpawnSleeperPid();
        using var tracker = new RobloxProcessTracker(
            log: NullLogger<RobloxProcessTracker>.Instance,
            attachTimeout: TimeSpan.FromSeconds(2),
            installerExtendedTimeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(50),
            isInstallerRunning: () => false,
            candidateProcesses: () => ByIds(rpbPid));

        var accountId = Guid.NewGuid();
        var attached = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        tracker.ProcessAttached += (_, e) => attached.TrySetResult(e.Pid);

        await tracker.TrackLaunchAsync(accountId, DateTimeOffset.UtcNow);

        Assert.True(tracker.IsTracking(accountId));
        Assert.Equal(rpbPid, await attached.Task.WaitAsync(TestWaits.Liveness));
    }

    [Fact]
    public async Task FifoClaim_ConcurrentLaunches_AttachDistinctProcesses()
    {
        // Regression guard for the FIFO per-pid claim: two launches racing over a shared candidate
        // source must not both claim the same pid. The tracker's own _claimedPidToAccount filtering
        // (which the candidate seam does NOT bypass) is what enforces uniqueness here.
        var pidA = SpawnSleeperPid();
        var pidB = SpawnSleeperPid();

        using var tracker = new RobloxProcessTracker(
            log: NullLogger<RobloxProcessTracker>.Instance,
            attachTimeout: TimeSpan.FromSeconds(2),
            installerExtendedTimeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(25),
            isInstallerRunning: () => false,
            candidateProcesses: () => ByIds(pidA, pidB));

        var accA = Guid.NewGuid();
        var accB = Guid.NewGuid();
        await Task.WhenAll(
            tracker.TrackLaunchAsync(accA, DateTimeOffset.UtcNow),
            tracker.TrackLaunchAsync(accB, DateTimeOffset.UtcNow));

        Assert.True(tracker.IsTracking(accA));
        Assert.True(tracker.IsTracking(accB));
        var attachedA = tracker.Attached[accA].Pid;
        var attachedB = tracker.Attached[accB].Pid;
        Assert.NotEqual(attachedA, attachedB);
        Assert.Contains(attachedA, new[] { pidA, pidB });
        Assert.Contains(attachedB, new[] { pidA, pidB });
    }

    /// <summary>Spawn a process that exits immediately, and wait until it has.</summary>
    private Process SpawnDeadProcess()
    {
        var psi = new ProcessStartInfo("cmd.exe", "/c exit")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        var p = Process.Start(psi)!;
        _spawned.Add(p);
        p.WaitForExit();
        return p;
    }

    private static RobloxProcessTracker NewSeamTracker() => new(
        log: NullLogger<RobloxProcessTracker>.Instance,
        attachTimeout: TimeSpan.FromSeconds(2),
        installerExtendedTimeout: TimeSpan.FromSeconds(5),
        pollInterval: TimeSpan.FromMilliseconds(50),
        isInstallerRunning: () => false,
        candidateProcesses: Array.Empty<Process>);

    [Fact]
    public void AttachToProcess_ProcessAlreadyExited_FiresProcessExitedSynchronouslyAndDoesNotGhost()
    {
        // The 2026-06-12 review's ghost-row bug: EnableRaisingEvents was set BEFORE the Exited
        // handler attached, so a process that exited in the window (routine — Roblox's
        // anti-multilaunch bootstrapper kills and respawns the attached pid quickly) raised
        // Exited to zero subscribers. The row stayed IsRunning forever and LaunchEligibility
        // excluded the account from batches until app restart.
        //
        // Calling the internal AttachToProcess directly with an already-exited handle stages
        // the race deterministically (both public entry points pre-filter on HasExited, so
        // only the in-window exit reaches this code in production). Contract: the exit must
        // be observed BY THE TIME AttachToProcess returns — synchronous synthesis, not a
        // threadpool coin-flip.
        using var tracker = NewSeamTracker();
        var accountId = Guid.NewGuid();
        var exitedFor = new List<Guid>();
        tracker.ProcessExited += (_, e) => { lock (exitedFor) exitedFor.Add(e.AccountId); };

        var dead = SpawnDeadProcess();
        tracker.AttachToProcess(accountId, dead);

        Assert.False(tracker.IsTracking(accountId),
            "An exit during attach must not leave a permanent ghost 'running' entry.");
        lock (exitedFor) Assert.Contains(accountId, exitedFor);
    }

    [Fact]
    public async Task AttachToProcess_ProcessAlreadyExited_ProcessExitedFiresExactlyOnce()
    {
        // The fix delivers the exit two ways (the real Exited callback + the post-attach
        // HasExited double-check). Consumers must see exactly one ProcessExited — a double
        // fire would double-stamp session history end times.
        using var tracker = NewSeamTracker();
        var accountId = Guid.NewGuid();
        var exitedCount = 0;
        tracker.ProcessExited += (_, _) => Interlocked.Increment(ref exitedCount);

        var dead = SpawnDeadProcess();
        tracker.AttachToProcess(accountId, dead);

        // Give the genuine threadpool Exited callback time to land too, then count.
        await Task.Delay(500);
        Assert.Equal(1, exitedCount);
    }

    [Fact]
    public async Task LiveProcessExitsAfterAttach_StillFiresProcessExitedAndStopsTracking()
    {
        // Regression sanity for the normal path: the reordering/once-guard must not break
        // ordinary exit delivery for a process that was alive at attach time.
        var pid = SpawnSleeperPid();
        using var tracker = new RobloxProcessTracker(
            log: NullLogger<RobloxProcessTracker>.Instance,
            attachTimeout: TimeSpan.FromSeconds(2),
            installerExtendedTimeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(50),
            isInstallerRunning: () => false,
            candidateProcesses: () => ByIds(pid));

        var accountId = Guid.NewGuid();
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        tracker.ProcessExited += (_, _) => exited.TrySetResult(true);

        await tracker.TrackLaunchAsync(accountId, DateTimeOffset.UtcNow);
        Assert.True(tracker.IsTracking(accountId));

        Process.GetProcessById(pid).Kill(entireProcessTree: true);

        Assert.True(await exited.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(tracker.IsTracking(accountId));
    }

    // ---- A throwing subscriber must neither escape the raise site nor starve the ones behind it ----
    //
    // ProcessExited is raised from the Process.Exited thread-pool callback. A subscriber that throws
    // (MainViewModel.OnProcessExited marshalling through a dispatcher that is shutting down) used to
    // escape that callback unhandled and take the process down, and starve every later subscriber.

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<RobloxProcessTracker>
    {
        public List<Microsoft.Extensions.Logging.LogLevel> Levels { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Levels) Levels.Add(logLevel);
        }
    }

    private static RobloxProcessTracker NewSeamTracker(Microsoft.Extensions.Logging.ILogger<RobloxProcessTracker> log) => new(
        log: log,
        attachTimeout: TimeSpan.FromSeconds(2),
        installerExtendedTimeout: TimeSpan.FromSeconds(5),
        pollInterval: TimeSpan.FromMilliseconds(50),
        isInstallerRunning: () => false,
        candidateProcesses: Array.Empty<Process>);

    [Fact]
    public void ProcessExited_ThrowingSubscriber_DoesNotEscapeAndDoesNotStarveTheNext()
    {
        var log = new CapturingLogger();
        using var tracker = NewSeamTracker(log);
        var accountId = Guid.NewGuid();
        var secondSaw = new List<(Guid Account, int Pid)>();
        tracker.ProcessExited += (_, _) => throw new TaskCanceledException("dispatcher shut down");
        tracker.ProcessExited += (_, e) => secondSaw.Add((e.AccountId, e.Pid));

        var dead = SpawnDeadProcess();
        var pid = dead.Id;

        // HandleExited is what the Process.Exited callback runs; calling it directly means a
        // regression fails this test rather than crashing the test host on a pool thread.
        var ex = Record.Exception(() => tracker.HandleExited(accountId, dead));

        Assert.Null(ex);
        Assert.Single(secondSaw);
        Assert.Equal((accountId, pid), secondSaw[0]);
        lock (log.Levels) Assert.Contains(Microsoft.Extensions.Logging.LogLevel.Warning, log.Levels);
    }

    [Fact]
    public void ProcessExited_SubscribersStillRunInSubscriptionOrder()
    {
        using var tracker = NewSeamTracker();
        var order = new List<int>();
        tracker.ProcessExited += (_, _) => order.Add(1);
        tracker.ProcessExited += (_, _) => throw new InvalidOperationException("boom");
        tracker.ProcessExited += (_, _) => order.Add(3);

        tracker.HandleExited(Guid.NewGuid(), SpawnDeadProcess());

        Assert.Equal(new[] { 1, 3 }, order);
    }

    [Fact]
    public void ProcessAttached_ThrowingSubscriber_DoesNotStarveTheNextOrFakeAnExit()
    {
        var pid = SpawnSleeperPid();
        using var tracker = NewSeamTracker();
        var accountId = Guid.NewGuid();
        var secondAttachedPids = new List<int>();
        var exitedCount = 0;
        tracker.ProcessAttached += (_, _) => throw new TaskCanceledException("dispatcher shut down");
        tracker.ProcessAttached += (_, e) => secondAttachedPids.Add(e.Pid);
        tracker.ProcessExited += (_, _) => Interlocked.Increment(ref exitedCount);

        var ex = Record.Exception(() => tracker.AttachToProcess(accountId, Process.GetProcessById(pid)));

        Assert.Null(ex);
        Assert.Equal(new[] { pid }, secondAttachedPids);
        // A throwing subscriber is not an attach failure: the live client stays tracked.
        Assert.True(tracker.IsTracking(accountId));
        Assert.Equal(0, Volatile.Read(ref exitedCount));
    }

    [Fact]
    public async Task ProcessAttachFailed_ThrowingSubscriber_DoesNotEscapeAndDoesNotStarveTheNext()
    {
        var time = new VirtualTime();
        var start = time.UtcNow;
        using var tracker = new RobloxProcessTracker(
            log: NullLogger<RobloxProcessTracker>.Instance,
            attachTimeout: TimeSpan.FromMilliseconds(200),
            installerExtendedTimeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(50),
            isInstallerRunning: () => false,
            candidateProcesses: Array.Empty<Process>,
            clock: time,
            delay: time.Advance);

        var accountId = Guid.NewGuid();
        var secondSaw = new List<Guid>();
        tracker.ProcessAttachFailed += (_, _) => throw new TaskCanceledException("dispatcher shut down");
        tracker.ProcessAttachFailed += (_, e) => secondSaw.Add(e.AccountId);

        var ex = await Record.ExceptionAsync(() => tracker.TrackLaunchAsync(accountId, start));

        Assert.Null(ex);
        Assert.Equal(new[] { accountId }, secondSaw);
    }

    // ---- Relaunching a running account: the older client's exit must not take the live one with it ----
    //
    // Launch, "Play as" and Follow can all start a second client for an account that is already
    // running. The second attach replaces the account's slot. Until v1.39 the OLD process's exit then
    // removed the slot by account id alone, which threw away the NEW client's slot: a live client
    // the app showed as not running, that Stop would not act on, and that plugins no longer saw.

    private sealed class TwoClients
    {
        public required RobloxProcessTracker Tracker { get; init; }
        public required Guid Account { get; init; }
        public required int PidX { get; init; }
        public required int PidY { get; init; }
        public readonly TaskCompletionSource<RobloxProcessEventArgs> ExitedX = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<RobloxProcessEventArgs> ExitedY = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// Account A attached to process X, then (a second launch) to process Y. Both go through the
    /// real polled-launch path so the pid claims are taken exactly as in production.
    /// </summary>
    private async Task<TwoClients> AttachSameAccountTwiceAsync()
    {
        var pidX = SpawnSleeperPid();
        var pidY = SpawnSleeperPid();
        var tracker = new RobloxProcessTracker(
            log: NullLogger<RobloxProcessTracker>.Instance,
            attachTimeout: TimeSpan.FromSeconds(5),
            installerExtendedTimeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(50),
            isInstallerRunning: () => false,
            candidateProcesses: () => ByIds(pidX, pidY));
        _trackers.Add(tracker);

        var account = Guid.NewGuid();
        var launchedAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        await tracker.TrackLaunchAsync(account, launchedAt);
        Assert.Equal(pidX, tracker.Attached[account].Pid);
        await tracker.TrackLaunchAsync(account, launchedAt);
        Assert.Equal(pidY, tracker.Attached[account].Pid);

        var clients = new TwoClients { Tracker = tracker, Account = account, PidX = pidX, PidY = pidY };
        tracker.ProcessExited += (_, e) =>
        {
            if (e.Pid == pidX) clients.ExitedX.TrySetResult(e);
            if (e.Pid == pidY) clients.ExitedY.TrySetResult(e);
        };
        return clients;
    }

    private readonly List<RobloxProcessTracker> _trackers = new();

    private static void KillPid(int pid)
    {
        using var p = Process.GetProcessById(pid);
        p.Kill(entireProcessTree: true);
    }

    [Fact]
    public async Task OlderClientExiting_AfterARelaunch_LeavesTheNewClientTracked_AndIsMarkedSuperseded()
    {
        var c = await AttachSameAccountTwiceAsync();

        KillPid(c.PidX);
        var exit = await c.ExitedX.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(exit.Superseded, "X was replaced by Y; its exit is the older client's, not the account's.");
        Assert.Equal(c.Account, exit.AccountId);
        Assert.True(c.Tracker.IsTracking(c.Account), "The live client's slot must survive the older client's exit.");
        Assert.Equal(c.PidY, c.Tracker.Attached[c.Account].Pid);
    }

    [Fact]
    public async Task NewerClientExiting_RemovesTheSlot_AndIsNotSuperseded()
    {
        var c = await AttachSameAccountTwiceAsync();
        KillPid(c.PidX);
        await c.ExitedX.Task.WaitAsync(TimeSpan.FromSeconds(10));

        KillPid(c.PidY);
        var exit = await c.ExitedY.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(exit.Superseded, "Y is the account's own client; its exit is the account stopping.");
        Assert.False(c.Tracker.IsTracking(c.Account));
        Assert.DoesNotContain(c.Account, c.Tracker.Attached.Keys);
    }

    [Fact]
    public async Task PidClaims_AreReleasedPerProcess_NotPerAccount()
    {
        var c = await AttachSameAccountTwiceAsync();
        Assert.True(c.Tracker.TryResolveAccountByPid(c.PidX, out var ownerX));
        Assert.Equal(c.Account, ownerX);
        Assert.True(c.Tracker.TryResolveAccountByPid(c.PidY, out _));

        KillPid(c.PidX);
        await c.ExitedX.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(c.Tracker.TryResolveAccountByPid(c.PidX, out _), "X's claim is released when X exits.");
        Assert.True(c.Tracker.TryResolveAccountByPid(c.PidY, out var ownerY), "Y's claim is untouched until Y exits.");
        Assert.Equal(c.Account, ownerY);

        KillPid(c.PidY);
        await c.ExitedY.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(c.Tracker.TryResolveAccountByPid(c.PidY, out _));
    }

    [Fact]
    public async Task NewerClientExitingFirst_ThenTheOlder_StillMarksTheOlderSuperseded_AndLeavesNoGhostSlot()
    {
        var c = await AttachSameAccountTwiceAsync();

        KillPid(c.PidY);
        var exitY = await c.ExitedY.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(exitY.Superseded);
        Assert.False(c.Tracker.IsTracking(c.Account));

        // X was replaced by Y even though Y is already gone. The account is not running either way,
        // and X's late exit must neither resurrect a slot nor read as the account stopping a second time.
        KillPid(c.PidX);
        var exitX = await c.ExitedX.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(exitX.Superseded);
        Assert.False(c.Tracker.IsTracking(c.Account));
        Assert.False(c.Tracker.TryResolveAccountByPid(c.PidX, out _));
    }

    [Fact]
    public async Task TheSupersededClient_IsStillHeldUntilItExits_AndStopActsOnTheLiveOne()
    {
        var c = await AttachSameAccountTwiceAsync();

        // Stop is keyed by account: it must reach the client the slot holds (Y), not the replaced one.
        Assert.True(c.Tracker.Kill(c.Account));
        var exitY = await c.ExitedY.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(exitY.Superseded);
        Assert.False(c.ExitedX.Task.IsCompleted, "Killing the live client must not touch the replaced one.");

        // The replaced client is still watched: its own exit still arrives, once, carrying its own pid.
        KillPid(c.PidX);
        var exitX = await c.ExitedX.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(c.PidX, exitX.Pid);
        Assert.True(exitX.Superseded);
    }

    [Fact]
    public void AnExitWithNoRelaunch_IsNotSuperseded()
    {
        // The flag means "a later attach replaced this process", nothing else. A process that was
        // never replaced, and one nobody attached at all, are both plain exits.
        using var tracker = NewSeamTracker();
        var seen = new List<bool>();
        tracker.ProcessExited += (_, e) => seen.Add(e.Superseded);

        tracker.HandleExited(Guid.NewGuid(), SpawnDeadProcess());

        Assert.Equal(new[] { false }, seen);
    }

    [Fact]
    public void NullLoggerConvenienceCtor_DoesNotThrow()
    {
        // The parameterless ctor wires the real seams (live installer scan + real RPB scan) with a
        // NullLogger default. Construction must not throw.
        using var tracker = new RobloxProcessTracker();
        Assert.NotNull(tracker);
    }
}
