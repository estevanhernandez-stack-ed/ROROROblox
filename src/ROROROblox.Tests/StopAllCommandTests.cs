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
        public List<string> Calls { get; } = new();
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
        var stopper = new RecordingStopper();
        var (vm, _, _, path) = MainViewModelTests.Build(
            instanceStopper: stopper, runningProbe: new FakeRunningProbe(3));
        try
        {
            var order = new List<string>();
            vm.StopAllConfirm = _ => true;
            vm.ExpectCloseForAllObserved = () => order.Add("ExpectCloseForAll");

            vm.StopAllCommand.Execute(null);
            order.AddRange(stopper.Calls);

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
