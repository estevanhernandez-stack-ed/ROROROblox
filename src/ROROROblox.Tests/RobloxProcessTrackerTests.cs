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

    [Fact]
    public async Task InstallerRunning_ExtendsDeadline_AttachesLateRpb_NoAttachFailed()
    {
        // Base timeout 200ms, extended cap 1s, fast poll. The "RPB" only appears at ~400ms — past
        // the base 200ms deadline — so without the extension the tracker would have already failed.
        var rpbPid = SpawnSleeperPid();
        var trackStart = DateTimeOffset.UtcNow;
        var revealAfter = TimeSpan.FromMilliseconds(400);

        IReadOnlyList<Process> Candidates() =>
            DateTimeOffset.UtcNow - trackStart >= revealAfter ? ByIds(rpbPid) : Array.Empty<Process>();

        using var tracker = new RobloxProcessTracker(
            log: NullLogger<RobloxProcessTracker>.Instance,
            attachTimeout: TimeSpan.FromMilliseconds(200),
            installerExtendedTimeout: TimeSpan.FromSeconds(1),
            pollInterval: TimeSpan.FromMilliseconds(50),
            isInstallerRunning: () => true,
            candidateProcesses: Candidates);

        var accountId = Guid.NewGuid();
        var attached = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = false;
        tracker.ProcessAttached += (_, e) => attached.TrySetResult(e.Pid);
        tracker.ProcessAttachFailed += (_, _) => failed = true;

        await tracker.TrackLaunchAsync(accountId, trackStart);

        Assert.True(tracker.IsTracking(accountId), "Tracker should have attached the install-delayed RPB.");
        var pid = await attached.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(rpbPid, pid);
        Assert.False(failed, "ProcessAttachFailed must NOT fire when the installer is running and the RPB eventually appears.");
    }

    [Fact]
    public async Task InstallerAbsent_GivesUpAtBaseTimeout_FiresAttachFailed()
    {
        // No installer, RPB never appears: unchanged behavior — give up at the base timeout and fail.
        using var tracker = new RobloxProcessTracker(
            log: NullLogger<RobloxProcessTracker>.Instance,
            attachTimeout: TimeSpan.FromMilliseconds(200),
            installerExtendedTimeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(50),
            isInstallerRunning: () => false,
            candidateProcesses: Array.Empty<Process>);

        var accountId = Guid.NewGuid();
        var failed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        tracker.ProcessAttachFailed += (_, _) => failed.TrySetResult(true);

        var sw = Stopwatch.StartNew();
        await tracker.TrackLaunchAsync(accountId, DateTimeOffset.UtcNow);
        sw.Stop();

        Assert.True(await failed.Task.WaitAsync(TimeSpan.FromSeconds(2)),
            "ProcessAttachFailed should fire when no installer is running and no RPB appears.");
        Assert.False(tracker.IsTracking(accountId));
        // Should NOT have waited toward the long extended cap — it bails near the base timeout.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2),
            $"Gave up too late ({sw.ElapsedMilliseconds}ms) — base-timeout behavior should be unextended when no installer runs.");
    }

    [Fact]
    public async Task InstallerRunning_ButRpbNeverAppears_BoundedByExtendedCap_ThenFails()
    {
        // Installer runs the whole time but the RPB never shows. The extension must be BOUNDED:
        // it waits to the extended cap (~600ms here), then fails — it does not wait forever.
        var cap = TimeSpan.FromMilliseconds(600);
        using var tracker = new RobloxProcessTracker(
            log: NullLogger<RobloxProcessTracker>.Instance,
            attachTimeout: TimeSpan.FromMilliseconds(150),
            installerExtendedTimeout: cap,
            pollInterval: TimeSpan.FromMilliseconds(50),
            isInstallerRunning: () => true,
            candidateProcesses: Array.Empty<Process>);

        var accountId = Guid.NewGuid();
        var failed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        tracker.ProcessAttachFailed += (_, _) => failed.TrySetResult(true);

        var sw = Stopwatch.StartNew();
        await tracker.TrackLaunchAsync(accountId, DateTimeOffset.UtcNow);
        sw.Stop();

        Assert.True(await failed.Task.WaitAsync(TimeSpan.FromSeconds(2)),
            "Even with the installer running, the tracker must eventually fail at the extended cap when no RPB appears.");
        Assert.False(tracker.IsTracking(accountId));
        // It DID extend past the base 150ms (proving the extension engaged)...
        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(450),
            $"Extension didn't engage — bailed at ~{sw.ElapsedMilliseconds}ms, near the base timeout instead of the extended cap.");
        // ...but did NOT run away past the cap (proving the bound holds).
        Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(1800),
            $"Extension was not bounded — ran {sw.ElapsedMilliseconds}ms, well past the {cap.TotalMilliseconds}ms cap.");
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
        Assert.Equal(rpbPid, await attached.Task.WaitAsync(TimeSpan.FromSeconds(2)));
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

    [Fact]
    public void NullLoggerConvenienceCtor_DoesNotThrow()
    {
        // The parameterless ctor wires the real seams (live installer scan + real RPB scan) with a
        // NullLogger default. Construction must not throw.
        using var tracker = new RobloxProcessTracker();
        Assert.NotNull(tracker);
    }
}
