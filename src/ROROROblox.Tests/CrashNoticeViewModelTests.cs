using System.IO;
using Microsoft.Extensions.Time.Testing;
using ROROROblox.App.Localization;
using ROROROblox.App.Shell;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// The crash notice (2026-11-10). RoRoRo has recorded how the last session ended since v1.35, and
/// until now only logged it: a crash was a window that vanished, and the next start said nothing.
/// 219 devices reported a crash in 30 days and the Store had a stack for 12 of them. The notice asks
/// for a support bundle, which is the only way a crash can reach the developer without telemetry.
/// Built through MainViewModelTests.Build, which disposes the decorator and stops the 30 s ticker.
/// </summary>
public sealed class CrashNoticeViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rororo-crash-notice-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _accountStores = [];

    public CrashNoticeViewModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
        foreach (var path in _accountStores.Where(File.Exists)) File.Delete(path);
    }

    private App.ViewModels.MainViewModel Vm()
    {
        var (vm, _, _, path) = MainViewModelTests.Build();
        _accountStores.Add(path);
        return vm;
    }

    private static LastExit Exit(string reason) =>
        new(reason, reason == LastExitRecorder.Crash ? "System.InvalidOperationException 0x80131509 boom" : string.Empty,
            new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));

    private RatingAsk DueRatingAsk()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 15, 0, 0, TimeSpan.Zero));
        var ask = new RatingAsk(_dir, InstallChannel.Store, time);
        for (var i = 0; i < RatingAsk.DaysNeeded; i++)
        {
            ask.RecordSuccessfulLaunch();
            time.Advance(TimeSpan.FromDays(1));
        }
        Assert.True(ask.ShouldAsk);
        return ask;
    }

    [Fact]
    public void AfterACrash_TheNextStartSaysSo()
    {
        var vm = Vm();

        vm.PreviousExit = Exit(LastExitRecorder.Crash);

        Assert.Equal(Loc.Get("MainWindow_CrashNotice"), vm.CrashNoticeText);
    }

    [Theory]
    [InlineData(LastExitRecorder.Clean)]
    [InlineData(LastExitRecorder.SessionEnding)]
    [InlineData(LastExitRecorder.None)]
    // Unknown is a session nobody got to end: End Task, a Store update, a power cut. Calling that a
    // crash would put the notice in front of everyone after every Store update.
    [InlineData(LastExitRecorder.Unknown)]
    public void AnyOtherEnding_SaysNothing(string reason)
    {
        var vm = Vm();

        vm.PreviousExit = Exit(reason);

        Assert.Equal(string.Empty, vm.CrashNoticeText);
    }

    [Fact]
    public void TheNoticeNeverPrintsTheCrashDetail()
    {
        // The detail is an exception type and message. It belongs in the bundle, not on screen.
        var vm = Vm();

        vm.PreviousExit = Exit(LastExitRecorder.Crash);

        Assert.DoesNotContain("InvalidOperationException", vm.CrashNoticeText);
        Assert.DoesNotContain("boom", vm.CrashNoticeText);
    }

    [Fact]
    public void OpenDiagnostics_OpensThatPage_AndClearsTheNotice()
    {
        var vm = Vm();
        var opened = new List<ShellPage>();
        vm.ShellPageOpener = opened.Add;
        vm.PreviousExit = Exit(LastExitRecorder.Crash);

        vm.OpenDiagnosticsFromCrashNoticeCommand.Execute(null);

        Assert.Equal(new[] { ShellPage.Diagnostics }, opened);
        Assert.Equal(string.Empty, vm.CrashNoticeText);
    }

    [Fact]
    public void Dismiss_ClearsTheNotice_AndOpensNothing()
    {
        var vm = Vm();
        var opened = new List<ShellPage>();
        vm.ShellPageOpener = opened.Add;
        vm.PreviousExit = Exit(LastExitRecorder.Crash);

        vm.DismissCrashNoticeCommand.Execute(null);

        Assert.Empty(opened);
        Assert.Equal(string.Empty, vm.CrashNoticeText);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AStartThatReportsACrash_DoesNotAlsoAskForARating(bool crashHandedOverFirst)
    {
        // "It crashed" and "please rate it" in the same strip is the wrong thing to say twice. The
        // rating ask waits for a start that has nothing to apologise for, whichever order the App
        // hands the two over in. It is not answered, so it shows on a later start.
        var vm = Vm();
        var ask = DueRatingAsk();

        if (crashHandedOverFirst)
        {
            vm.PreviousExit = Exit(LastExitRecorder.Crash);
            vm.RatingAsk = ask;
        }
        else
        {
            vm.RatingAsk = ask;
            vm.PreviousExit = Exit(LastExitRecorder.Crash);
        }

        Assert.NotEqual(string.Empty, vm.CrashNoticeText);
        Assert.Equal(string.Empty, vm.RatingAskText);
        Assert.True(ask.ShouldAsk);
    }

    [Fact]
    public void DismissingTheCrashNotice_DoesNotBringTheRatingAskBackThisSession()
    {
        var vm = Vm();
        vm.PreviousExit = Exit(LastExitRecorder.Crash);
        vm.RatingAsk = DueRatingAsk();

        vm.DismissCrashNoticeCommand.Execute(null);

        Assert.Equal(string.Empty, vm.RatingAskText);
    }
}
