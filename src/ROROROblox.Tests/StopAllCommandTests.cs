using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// F-001 — Stop all reached the app only from the tray until now. These pin the behaviour that
/// moved out of App.xaml.cs, especially the ordering: ExpectCloseForAll() must run BEFORE
/// StopAll(), or every deliberate close raises a drop-out alert.
/// </summary>
public class StopAllCommandTests
{
    private sealed class RecordingStopper : IRobloxInstanceStopper
    {
        // Optionally shares its call log with an external list (Accepted_ExpectsClosesBeforeStopping
        // passes its own `order` list here) so StopAll() appends to that SAME list at the moment it
        // actually runs, instead of a separate list merged in after Execute() returns. A post-hoc
        // merge can't distinguish "happened first" from "happened at all" -- only a shared list
        // written to at call time proves order.
        public List<string> Calls { get; }
        public RecordingStopper(List<string>? calls = null) => Calls = calls ?? new();
        public int StopAll() { Calls.Add("StopAll"); return 3; }
        public bool StopAccount(Guid accountId) => true;
        public int StopWindowless() => 0;
    }

    private sealed class FakeRunningProbe : IRobloxRunningProbe
    {
        private readonly int _count;
        public FakeRunningProbe(int count) => _count = count;
        public IReadOnlyList<int> GetRunningPlayerPids() => Enumerable.Range(1000, _count).ToList();
        public IReadOnlyList<RobloxProcessInfo> GetRunningPlayers() => Array.Empty<RobloxProcessInfo>();
    }

    [Fact]
    public void NothingRunning_DoesNotConfirmAndDoesNotStop()
    {
        var stopper = new RecordingStopper();
        var confirmed = false;
        var (vm, _, _, path) = MainViewModelTests.Build(
            instanceStopper: stopper, runningProbe: new FakeRunningProbe(0));
        try
        {
            vm.StopAllConfirm = _ => { confirmed = true; return true; };

            vm.StopAllCommand.Execute(null);

            Assert.False(confirmed, "No clients are running, so nothing should be confirmed.");
            Assert.Empty(stopper.Calls);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Declined_DoesNotStop()
    {
        var stopper = new RecordingStopper();
        var (vm, _, _, path) = MainViewModelTests.Build(
            instanceStopper: stopper, runningProbe: new FakeRunningProbe(3));
        try
        {
            vm.StopAllConfirm = _ => false;

            vm.StopAllCommand.Execute(null);

            Assert.Empty(stopper.Calls);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Accepted_ExpectsClosesBeforeStopping()
    {
        // `order` and `stopper.Calls` are the SAME list -- both the observer callback and
        // StopAll() append to it at the instant they actually run, so the list's contents ARE the
        // real execution order, not a reconstruction of it. (A prior version of this test appended
        // stopper.Calls to a separate list after Execute() returned, which produced the same
        // "passing" assertion regardless of which call actually ran first -- it could only fail if
        // one of the two calls never fired at all.)
        var order = new List<string>();
        var stopper = new RecordingStopper(order);
        var (vm, _, _, path) = MainViewModelTests.Build(
            instanceStopper: stopper, runningProbe: new FakeRunningProbe(3));
        try
        {
            vm.StopAllConfirm = _ => true;
            vm.ExpectCloseForAllObserved = () => order.Add("ExpectCloseForAll");

            vm.StopAllCommand.Execute(null);

            Assert.Equal(new[] { "ExpectCloseForAll", "StopAll" }, order);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ConfirmReceivesTheRunningCount()
    {
        var seen = -1;
        var (vm, _, _, path) = MainViewModelTests.Build(
            instanceStopper: new RecordingStopper(), runningProbe: new FakeRunningProbe(5));
        try
        {
            vm.StopAllConfirm = n => { seen = n; return false; };

            vm.StopAllCommand.Execute(null);

            Assert.Equal(5, seen);
        }
        finally { File.Delete(path); }
    }
}
