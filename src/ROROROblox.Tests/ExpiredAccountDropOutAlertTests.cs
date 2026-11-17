using System.IO;
using ROROROblox.Core.Discord;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// An account whose session expired (401) or was rate-limited (403) while its client ran is out of the
/// presence poll, so only the process exit can say it dropped. The exit raises the same
/// <see cref="AlertKind.AccountDroppedOut"/> ApplyPresence raises for every other account.
/// </summary>
public class ExpiredAccountDropOutAlertTests
{
    private const long PetSim = 8737899170;

    private static async Task<(MainViewModel Vm, AccountSummary Row, MainViewModelTests.FakeRobloxProcessTracker Tracker, List<AlertTrigger> Raised, string Path)> RunningInGameAsync()
    {
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher: new MainViewModelTests.RecordingSuccessLauncher());
        var row = new AccountSummary(await store.AddAsync("Alt", "", "cookie")) { RobloxUserId = 12345 };
        vm.Accounts.Add(row);
        await vm.LaunchAccountForPluginAsync(row, new LaunchTarget.Place(PlaceId: PetSim));
        tracker.AttachedMap[row.Id] = new TrackedProcess(9001, DateTimeOffset.UtcNow);
        tracker.RaiseAttached(new RobloxProcessEventArgs(row.Id, 9001));
        vm.ApplyPresence(new AccountPresenceEventArgs(row.Id, UserPresenceType.InGame, PetSim, "Pet Sim 99", DateTimeOffset.UtcNow));
        var raised = new List<AlertTrigger>();
        vm.AlertsRaised += (_, t) => raised.AddRange(t);
        vm.IsShuttingDown = static () => false;
        return (vm, row, tracker, raised, path);
    }

    private static void Exit(MainViewModelTests.FakeRobloxProcessTracker tracker, AccountSummary row)
    {
        tracker.AttachedMap.Remove(row.Id);
        tracker.RaiseExited(new RobloxProcessEventArgs(row.Id, 9001) { OccurredAtUtc = DateTimeOffset.UtcNow });
    }

    private static void Cleanup(MainViewModel vm, string path)
    {
        vm.StopPeriodicRefresh();
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    [Fact]
    public async Task AnExpiredRunningAccountWhoseClientExits_RaisesOneDropOut_NamingTheGameItLastHad()
    {
        var (vm, row, tracker, raised, path) = await RunningInGameAsync();
        try
        {
            vm.ApplySessionExpired(row.Id, 0);
            Assert.Null(row.CurrentGameName); // the row itself must not keep the frozen game
            Assert.Empty(raised);

            Exit(tracker, row);

            var alert = Assert.Single(raised);
            Assert.Equal(AlertKind.AccountDroppedOut, alert.Kind);
            Assert.Equal(row.Id, alert.AccountId);
            Assert.Equal("Pet Sim 99", alert.GameName);
        }
        finally { Cleanup(vm, path); }
    }

    [Fact]
    public async Task AnExpiredAccountWithNoKnownGame_StillRaises_WithoutOne()
    {
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher: new MainViewModelTests.RecordingSuccessLauncher());
        try
        {
            var row = new AccountSummary(await store.AddAsync("Alt", "", "cookie")) { RobloxUserId = 12345, IsRunning = true, RunningPid = 9001 };
            vm.Accounts.Add(row);
            var raised = new List<AlertTrigger>();
            vm.AlertsRaised += (_, t) => raised.AddRange(t);
            vm.IsShuttingDown = static () => false;
            vm.ApplySessionExpired(row.Id, 0);

            Exit(tracker, row);

            Assert.Null(Assert.Single(raised).GameName);
        }
        finally { Cleanup(vm, path); }
    }

    [Fact]
    public async Task AnExitAfterAUserAskedStop_RaisesNothing()
    {
        var (vm, row, tracker, raised, path) = await RunningInGameAsync();
        try
        {
            vm.ApplySessionExpired(row.Id, 0);
            vm.ExpectClose(row.Id);

            Exit(tracker, row);

            Assert.Empty(raised);
        }
        finally { Cleanup(vm, path); }
    }

    [Fact]
    public async Task AnExitDuringAppShutdown_RaisesNothing()
    {
        var (vm, row, tracker, raised, path) = await RunningInGameAsync();
        try
        {
            vm.ApplySessionExpired(row.Id, 0);
            vm.IsShuttingDown = static () => true;

            Exit(tracker, row);

            Assert.Empty(raised);
        }
        finally { Cleanup(vm, path); }
    }

    [Fact]
    public async Task ANonExpiredAccountsExit_RaisesNothingFromThisPath_AndPresenceStillOwnsTheAlert()
    {
        var (vm, row, tracker, raised, path) = await RunningInGameAsync();
        try
        {
            Exit(tracker, row);
            Assert.Empty(raised); // ghost case: the exit alone never alerts

            vm.ApplyPresence(new AccountPresenceEventArgs(row.Id, UserPresenceType.Offline, null, null, DateTimeOffset.UtcNow));
            Assert.Equal("Pet Sim 99", Assert.Single(raised).GameName);
        }
        finally { Cleanup(vm, path); }
    }

    [Fact]
    public async Task ASupersededExitOfAnExpiredAccount_RaisesNothing()
    {
        var (vm, row, tracker, raised, path) = await RunningInGameAsync();
        try
        {
            vm.ApplySessionExpired(row.Id, 0);

            tracker.RaiseExited(new RobloxProcessEventArgs(row.Id, 8000) { Superseded = true });

            Assert.Empty(raised);
        }
        finally { Cleanup(vm, path); }
    }

    [Fact]
    public async Task APresenceConfirmationAfterTheExit_DoesNotRaiseASecond()
    {
        var (vm, row, tracker, raised, path) = await RunningInGameAsync();
        try
        {
            vm.ApplySessionExpired(row.Id, 0);
            Exit(tracker, row);
            Assert.Single(raised);

            vm.ApplyPresence(new AccountPresenceEventArgs(row.Id, UserPresenceType.Offline, null, null, DateTimeOffset.UtcNow));

            Assert.Single(raised);
        }
        finally { Cleanup(vm, path); }
    }

    [Fact]
    public async Task AReauthBeforeTheExit_HandsTheAlertBackToPresence()
    {
        var (vm, row, tracker, raised, path) = await RunningInGameAsync();
        try
        {
            vm.ApplySessionExpired(row.Id, 0);
            row.SessionExpired = false; // what ReauthenticateAsync does as its last act
            vm.ApplyPresence(new AccountPresenceEventArgs(row.Id, UserPresenceType.InGame, PetSim, "Pet Sim 99", DateTimeOffset.UtcNow));

            Exit(tracker, row);
            Assert.Empty(raised);

            vm.ApplyPresence(new AccountPresenceEventArgs(row.Id, UserPresenceType.Offline, null, null, DateTimeOffset.UtcNow));
            Assert.Single(raised);
        }
        finally { Cleanup(vm, path); }
    }

    [Fact]
    public async Task ARateLimitedRunningAccountWhoseClientExits_RaisesOneDropOut()
    {
        var (vm, row, tracker, raised, path) = await RunningInGameAsync();
        try
        {
            vm.ApplySessionLimited(row.Id);

            Exit(tracker, row);

            var alert = Assert.Single(raised);
            Assert.Equal(AlertKind.AccountDroppedOut, alert.Kind);
            Assert.Equal("Pet Sim 99", alert.GameName);
        }
        finally { Cleanup(vm, path); }
    }

    // A Windows shutdown or logoff ends every client alongside RoRoRo. App.OnSessionEnding marks every
    // close as expected (the stamp lapses after a minute, so a cancelled shutdown cannot leave alerts
    // silenced the way a stuck shutting-down flag would).
    [Fact]
    public async Task AnExitAfterEveryCloseWasMarkedExpected_RaisesNothing()
    {
        var (vm, row, tracker, raised, path) = await RunningInGameAsync();
        try
        {
            vm.ApplySessionExpired(row.Id, 0);
            vm.ExpectCloseForAll();

            Exit(tracker, row);

            Assert.Empty(raised);
        }
        finally { Cleanup(vm, path); }
    }

    [Fact]
    public void SessionEnding_MarksEveryCloseExpected_BeforeHandingOnToWpf()
    {
        // App.OnSessionEnding cannot run in a test (no Application in this host), so this is a source
        // fence: the call is there, and it comes before base.OnSessionEnding.
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate the repo root from the test bin directory.");
        const string file = "src/ROROROblox.App/App.xaml.cs";
        var code = SettingsReachabilityTests.WithoutComments(
            file, File.ReadAllLines(Path.Combine(root!, file.Replace('/', Path.DirectorySeparatorChar))));

        var start = Array.FindIndex(code, l => l.Contains("protected override void OnSessionEnding(", StringComparison.Ordinal));
        Assert.True(start >= 0, "App.OnSessionEnding moved; update this fence.");
        var end = Array.FindIndex(code, start, l => l.Contains("base.OnSessionEnding(e);", StringComparison.Ordinal));
        Assert.True(end > start, "App.OnSessionEnding must still call base.OnSessionEnding.");

        Assert.Contains(code.Skip(start).Take(end - start), l => l.Contains("ExpectCloseForAll()", StringComparison.Ordinal));
    }
}
