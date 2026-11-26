using ROROROblox.App.KnownIssues;
using ROROROblox.App.Localization;
using ROROROblox.App.Shell;
using ROROROblox.App.ViewModels;
using ROROROblox.Core.KnownIssues;
using static ROROROblox.Tests.KnownIssues.KnownIssueBuilder;

namespace ROROROblox.Tests.KnownIssues;

/// <summary>Built through MainViewModelTests.Build, which disposes the decorator and stops the 30 s ticker.</summary>
public sealed class MainViewModelKnownIssuesTests : IDisposable
{
    private readonly string _dismissedPath = Path.Combine(Path.GetTempPath(), "rororo-mvm-kri-" + Guid.NewGuid().ToString("N") + ".json");
    private readonly List<string> _accountStores = [];

    public void Dispose()
    {
        if (File.Exists(_dismissedPath)) File.Delete(_dismissedPath);
        foreach (var path in _accountStores.Where(File.Exists)) File.Delete(path);
    }

    private ROROROblox.App.ViewModels.MainViewModel Vm()
    {
        var (vm, _, _, path) = MainViewModelTests.Build(uiDispatcher: new KnownIssuesNoticeModelTests.InlineDispatcher());
        _accountStores.Add(path);
        return vm;
    }

    private KnownIssuesNoticeModel ModelWith(params KnownIssue[] issues)
    {
        var model = new KnownIssuesNoticeModel(
            new KnownIssuesDismissals(_dismissedPath), () => null, new KnownIssuesNoticeModelTests.InlineDispatcher());
        model.Apply(new KnownIssuesSnapshot(issues, KnownIssuesSource.Release, DateTimeOffset.UnixEpoch));
        return model;
    }

    [Fact]
    public void WithoutAModelThereIsNoNoticeAndAPlainMenuEntry()
    {
        var vm = Vm();

        Assert.Equal(string.Empty, vm.KnownIssueNoticeText);
        Assert.Equal(Loc.Get("MainWindow_KnownRobloxIssues"), vm.KnownIssuesMenuHeader);
    }

    [Fact]
    public void TheNoticeFollowsTheModel_AndWaitsInMoreBehindTheContestedWarning()
    {
        var vm = Vm();
        vm.KnownIssuesNotice = ModelWith(Issue("a"));
        var text = Loc.Format("MainWindow_KnownIssueNoticeOne", "Title of a");
        Assert.Equal(text, vm.KnownIssueNoticeText);
        Assert.Equal(text, vm.RobloxLine.Top!.Text);

        vm.SetContested(true);
        Assert.Equal(text, vm.KnownIssueNoticeText);
        Assert.Equal(MultiInstanceCopy.ContestedBanner, vm.RobloxLine.Top!.Text);
        Assert.Equal(text, Assert.Single(vm.RobloxLine.More).Text);

        vm.SetContested(false);
        Assert.Equal(text, vm.RobloxLine.Top!.Text);
        Assert.Empty(vm.RobloxLine.More);
    }

    [Fact]
    public void AModelAttachedWhileContestedIsInMore_NotHidden()
    {
        var vm = Vm();
        vm.SetContested(true);

        vm.KnownIssuesNotice = ModelWith(Issue("a"));

        var text = Loc.Format("MainWindow_KnownIssueNoticeOne", "Title of a");
        Assert.Equal(text, vm.KnownIssueNoticeText);
        Assert.Equal(text, Assert.Single(vm.RobloxLine.More).Text);
    }

    [Fact]
    public void ANoticeBehindAHigherOneIsNotUsedUp_AndDismissingItFromMoreUsesItUp()
    {
        var vm = Vm();
        vm.SetContested(true);
        vm.KnownIssuesNotice = ModelWith(Issue("a"));
        Assert.True(Assert.Single(vm.RobloxLine.More).ShowKnownIssueActions);

        vm.SetContested(false);
        Assert.True(vm.RobloxLine.Top!.ShowKnownIssueActions);

        vm.SetContested(true);
        vm.DismissKnownIssueNoticeCommand.Execute(null);

        Assert.Empty(vm.RobloxLine.More);
        vm.SetContested(false);
        Assert.Null(vm.RobloxLine.Top);
    }

    [Fact]
    public void DismissingClearsTheNotice()
    {
        var vm = Vm();
        vm.KnownIssuesNotice = ModelWith(Issue("a"));

        vm.DismissKnownIssueNoticeCommand.Execute(null);

        Assert.Equal(string.Empty, vm.KnownIssueNoticeText);
    }

    [Fact]
    public void TheCommandOpensTheKnownIssuesPage()
    {
        var vm = Vm();
        ShellPage? opened = null;
        vm.ShellPageOpener = page => opened = page;

        vm.OpenKnownIssuesCommand.Execute(null);

        Assert.Equal(ShellPage.KnownRobloxIssues, opened);
    }

    [Fact]
    public void TheMenuHeaderRaisesChangeWhenTheModelDoes()
    {
        var vm = Vm();
        var model = ModelWith();
        vm.KnownIssuesNotice = model;
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        model.Apply(new KnownIssuesSnapshot([Issue("a")], KnownIssuesSource.Release, DateTimeOffset.UnixEpoch));

        Assert.Contains(nameof(vm.KnownIssuesMenuHeader), changed);
        Assert.Contains(nameof(vm.KnownIssueNoticeText), changed);
    }
}
