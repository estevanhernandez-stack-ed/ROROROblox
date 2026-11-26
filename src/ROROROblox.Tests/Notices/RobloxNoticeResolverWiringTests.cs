using System.Globalization;
using System.Reflection;
using ROROROblox.App.KnownIssues;
using ROROROblox.App.Localization;
using ROROROblox.App.Notices;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
using ROROROblox.Core.Feeds;
using ROROROblox.Core.KnownIssues;
using ROROROblox.Tests.KnownIssues;
using static ROROROblox.Tests.KnownIssues.KnownIssueBuilder;

namespace ROROROblox.Tests.Notices;

/// <summary>
/// The Roblox line on the real <see cref="MainViewModel"/> (v1.41 item 7): every input the spec names
/// moves it, the more-count resets, and the two links open constants. Built through
/// MainViewModelTests.Build, which disposes the decorator and stops the 30 s ticker.
/// </summary>
[Collection("MutatesUiCulture")]
public sealed class RobloxNoticeResolverWiringTests : IDisposable
{
    private readonly CultureInfo _previous = TranslationSource.Instance.CurrentCulture;
    private readonly string _dismissedPath = Path.Combine(Path.GetTempPath(), "rororo-line-" + Guid.NewGuid().ToString("N") + ".json");
    private readonly List<string> _accountStores = [];

    public RobloxNoticeResolverWiringTests() => TranslationSource.Instance.CurrentCulture = new CultureInfo("en");

    public void Dispose()
    {
        TranslationSource.Instance.CurrentCulture = _previous;
        if (File.Exists(_dismissedPath)) File.Delete(_dismissedPath);
        foreach (var path in _accountStores.Where(File.Exists)) File.Delete(path);
    }

    private sealed class RecordingShell : IShellOpener
    {
        public List<string> Opened { get; } = [];

        public void Open(string path) => Opened.Add(path);
    }

    private MainViewModel Vm(IShellOpener? shell = null)
    {
        var (vm, _, _, path) = MainViewModelTests.Build(
            shellOpener: shell,
            uiDispatcher: new KnownIssuesNoticeModelTests.InlineDispatcher());
        _accountStores.Add(path);
        return vm;
    }

    private KnownIssuesNoticeModel IssueModel()
    {
        var model = new KnownIssuesNoticeModel(
            new KnownIssuesDismissals(_dismissedPath), () => null, new KnownIssuesNoticeModelTests.InlineDispatcher());
        model.Apply(new KnownIssuesSnapshot([Issue("a")], KnownIssuesSource.Release, DateTimeOffset.UnixEpoch));
        return model;
    }

    private static void SetHoldBanner(MainViewModel vm, bool showing) =>
        typeof(MainViewModel).GetProperty(nameof(MainViewModel.RobloxUpdating))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(vm, [showing]);

    private static AccountSummary Row(string name) =>
        new(new Account(Guid.NewGuid(), DisplayName: name, AvatarUrl: "", CreatedAt: DateTimeOffset.UtcNow, LastLaunchedAt: null));

    private static readonly RobloxUpdateState Newer =
        new(RobloxUpdateKind.Pending, RobloxUpdateDirection.Newer, new Version(0, 748, 0, 7481526));

    private static readonly RobloxStatusReading GamesOutage =
        new(RobloxStatusRow.Games, RobloxStatusLevel.Outage, 0);

    [Fact]
    public void ANewViewModelHasNoLine()
    {
        var vm = Vm();

        Assert.False(vm.RobloxLine.HasNotice);
        Assert.Null(vm.RobloxLine.Top);
        Assert.Equal(0, vm.RobloxLine.MoreCount);
    }

    [Fact]
    public void TheContestedWarningIsTheTopNotice_AndClearingItClearsTheLine()
    {
        var vm = Vm();

        vm.SetContested(true);
        Assert.True(vm.RobloxLine.Top!.ShowContestedActions);
        Assert.Equal(vm.ContestedBannerText, vm.RobloxLine.Top.Text);

        vm.SetContested(false);
        Assert.False(vm.RobloxLine.HasNotice);
    }

    [Fact]
    public void AKnownIssueUnderAContestedNameIsCountedInMore_NotDismissed()
    {
        var vm = Vm();
        vm.KnownIssuesNotice = IssueModel();
        Assert.True(vm.RobloxLine.Top!.ShowKnownIssueActions);

        vm.SetContested(true);

        Assert.True(vm.RobloxLine.Top!.ShowContestedActions);
        var behind = Assert.Single(vm.RobloxLine.More);
        Assert.True(behind.ShowKnownIssueActions);
        Assert.Equal(1, vm.RobloxLine.MoreCount);

        vm.SetContested(false);
        Assert.True(vm.RobloxLine.Top!.ShowKnownIssueActions);
        Assert.Empty(vm.RobloxLine.More);
    }

    [Fact]
    public void DismissingTheKnownIssueClearsItFromTheLine()
    {
        var vm = Vm();
        vm.KnownIssuesNotice = IssueModel();

        vm.DismissKnownIssueNoticeCommand.Execute(null);

        Assert.False(vm.RobloxLine.HasNotice);
    }

    [Fact]
    public void TheFeedReadingsMoveTheLine()
    {
        var vm = Vm();

        vm.SetRobloxFeedReadings(Newer, GamesOutage);

        Assert.True(vm.RobloxLine.Top!.ShowStatusLink);
        Assert.Equal("Roblox reports an outage: Games", vm.RobloxLine.Top.Text);
        var update = Assert.Single(vm.RobloxLine.More);
        Assert.True(update.ShowWhatChanged);

        vm.SetRobloxFeedReadings(RobloxUpdateState.Unknown, RobloxStatusReading.None);
        Assert.False(vm.RobloxLine.HasNotice);
    }

    [Fact]
    public void TheHoldBannerFlippingMovesTheUpdateNotice()
    {
        var vm = Vm();
        vm.SetRobloxFeedReadings(Newer, RobloxStatusReading.None);
        Assert.True(vm.RobloxLine.HasNotice);

        SetHoldBanner(vm, true);
        Assert.False(vm.RobloxLine.HasNotice);

        SetHoldBanner(vm, false);
        Assert.True(vm.RobloxLine.HasNotice);
    }

    [Fact]
    public void TheSavedAccountCountPicksTheSentence()
    {
        var vm = Vm();
        vm.SetRobloxFeedReadings(Newer, RobloxStatusReading.None);
        vm.Accounts.Add(Row("one"));
        Assert.Equal(Loc.Get("RobloxLine_Update_One"), vm.RobloxLine.Top!.Text);

        vm.Accounts.Add(Row("two"));
        Assert.Equal(Loc.Get("RobloxLine_Update_Many"), vm.RobloxLine.Top!.Text);

        vm.Accounts.RemoveAt(1);
        Assert.Equal(Loc.Get("RobloxLine_Update_One"), vm.RobloxLine.Top!.Text);
    }

    [Fact]
    public void CompactReadsTheShortSentenceWhateverTheAccountCount()
    {
        var vm = Vm();
        vm.Accounts.Add(Row("one"));
        vm.Accounts.Add(Row("two"));
        vm.SetRobloxFeedReadings(Newer, RobloxStatusReading.None);
        Assert.Equal(Loc.Get("RobloxLine_Update_Many"), vm.RobloxLine.Top!.Text);

        vm.IsCompact = true;

        Assert.Equal(Loc.Get("RobloxLine_Update_One"), vm.RobloxLine.Top!.Text);
    }

    [Fact]
    public void MoreCountLabelAndSpokenNameFollowTheBehindList()
    {
        var vm = Vm();
        vm.KnownIssuesNotice = IssueModel();
        vm.SetContested(true);
        vm.SetRobloxFeedReadings(Newer, GamesOutage);

        Assert.Equal(3, vm.RobloxLine.MoreCount);
        Assert.Equal("+3 more", vm.RobloxLine.MoreLabel);
        Assert.Equal("3 more Roblox notices", vm.RobloxLine.MoreSpokenName);
    }

    [Fact]
    public void IsExpandedResetsWhenTheCountReachesZero_AndOnlyThen()
    {
        var vm = Vm();
        vm.KnownIssuesNotice = IssueModel();
        vm.SetContested(true);
        vm.RobloxLine.IsExpanded = true;

        // Still one behind: the list stays open.
        vm.SetRobloxFeedReadings(RobloxUpdateState.Unknown, RobloxStatusReading.None);
        Assert.Equal(1, vm.RobloxLine.MoreCount);
        Assert.True(vm.RobloxLine.IsExpanded);

        vm.SetContested(false);
        Assert.Equal(0, vm.RobloxLine.MoreCount);
        Assert.False(vm.RobloxLine.IsExpanded);

        // And it does not come back open by itself.
        vm.SetContested(true);
        Assert.Equal(1, vm.RobloxLine.MoreCount);
        Assert.False(vm.RobloxLine.IsExpanded);
    }

    [Fact]
    public void ALineChangeRaisesPropertyChangedForTheWholeLine()
    {
        var vm = Vm();
        var raised = new List<string?>();
        vm.RobloxLine.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.SetContested(true);

        Assert.Contains(string.Empty, raised);
    }

    [Fact]
    public void TheTwoLinkCommandsOpenTheConstants()
    {
        var shell = new RecordingShell();
        var vm = Vm(shell);

        vm.OpenRobloxStatusCommand.Execute(null);
        vm.OpenRobloxReleaseNotesCommand.Execute(null);

        Assert.Equal([RobloxLinks.StatusPage, RobloxLinks.ReleaseNotes], shell.Opened);
        Assert.Equal("https://status.roblox.com", RobloxLinks.StatusPage);
        Assert.Equal("https://create.roblox.com/docs/release-notes", RobloxLinks.ReleaseNotes);
    }
}
