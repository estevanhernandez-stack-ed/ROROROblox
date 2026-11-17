using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Plugins.Adapters;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// A tracked slot whose <see cref="Process"/> has been disposed. HandleExited (and tracker Dispose)
/// dispose the object, and a caller that took the slot just before can then read
/// <c>HasExited</c> / <c>Id</c> on it: both throw InvalidOperationException ("No process is
/// associated with this object"). The old catch blocks read <c>Process.Id</c> again, so
/// RequestClose / Kill threw instead of returning false and a plugin's StopAccount failed.
/// The tests stage that state with a live throwaway child whose handle is disposed while it is
/// still in the tracker's slot table, which is the same state the race leaves behind.
/// </summary>
public sealed class RobloxProcessTrackerDisposedSlotTests : IDisposable
{
    private readonly List<int> _childPids = new();
    private readonly List<RobloxProcessTracker> _trackers = new();

    public void Dispose()
    {
        foreach (var t in _trackers) { try { t.Dispose(); } catch { } }
        // Only our own throwaway children, by pid. Never a name scan.
        foreach (var pid in _childPids)
        {
            try { using var p = Process.GetProcessById(pid); p.Kill(); } catch { }
        }
    }

    private RobloxProcessTracker NewTracker()
    {
        var t = new RobloxProcessTracker(
            NullLogger<RobloxProcessTracker>.Instance,
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(50),
            () => false, Array.Empty<Process>);
        _trackers.Add(t);
        return t;
    }

    /// <summary>Attach a live child, then dispose the tracked Process object under the tracker.</summary>
    private (RobloxProcessTracker Tracker, Guid Account, int Pid) TrackerWithDisposedSlot()
    {
        var psi = new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 > nul")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        var child = Process.Start(psi)!;
        var pid = child.Id;
        _childPids.Add(pid);

        var tracker = NewTracker();
        var account = Guid.NewGuid();
        tracker.AttachToProcess(account, child);
        Assert.True(tracker.IsTracking(account));

        child.Dispose();
        // Prove the state is the real exception, not an invented one.
        Assert.Throws<InvalidOperationException>(() => child.HasExited);
        Assert.Throws<InvalidOperationException>(() => child.Id);
        return (tracker, account, pid);
    }

    [Fact]
    public void RequestClose_DisposedProcess_ReturnsFalseWithoutThrowing()
    {
        var (tracker, account, _) = TrackerWithDisposedSlot();
        Assert.False(tracker.RequestClose(account));
    }

    [Fact]
    public void Kill_DisposedProcess_ReturnsFalseWithoutThrowing()
    {
        var (tracker, account, _) = TrackerWithDisposedSlot();
        Assert.False(tracker.Kill(account));
    }

    [Fact]
    public void Attached_DisposedProcess_ProjectsThePidCapturedAtAttach()
    {
        var (tracker, account, pid) = TrackerWithDisposedSlot();
        var attached = Record.Exception(() => tracker.Attached);
        Assert.Null(attached);
        Assert.Equal(pid, tracker.Attached[account].Pid);
    }

    [Fact]
    public void LogLine_ForTheDeadSlot_NamesThePidFromAttach()
    {
        var log = new ListLogger();
        var tracker = new RobloxProcessTracker(
            log, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(50),
            () => false, Array.Empty<Process>);
        _trackers.Add(tracker);
        var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 > nul")
        { CreateNoWindow = true, UseShellExecute = false })!;
        _childPids.Add(child.Id);
        var account = Guid.NewGuid();
        tracker.AttachToProcess(account, child);
        child.Dispose();

        Assert.False(tracker.Kill(account));
        // Whatever level it logs at, the message must not have thrown while formatting its pid.
        Assert.DoesNotContain(log.Entries, e => e.Contains("Kill failed") && !e.Contains(_childPids[0].ToString()));
    }

    [Fact]
    public async Task PluginStopSequence_ForTheDeadSlot_CompletesInsteadOfThrowing()
    {
        var (tracker, account, _) = TrackerWithDisposedSlot();
        var stopper = new ProcessTrackerAccountStopper(
            tracker, settings: null, log: null, delay: (_, _) => Task.CompletedTask);

        var ex = await Record.ExceptionAsync(
            () => stopper.RunStopSequenceAsync(account, ClientStopSequence.DefaultGrace));

        Assert.Null(ex);
    }

    private sealed class ListLogger : ILogger<RobloxProcessTracker>
    {
        public List<string> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add(formatter(state, exception));
    }
}
