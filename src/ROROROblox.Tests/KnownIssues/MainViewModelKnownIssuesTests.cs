using ROROROblox.App.KnownIssues;
using ROROROblox.App.Localization;
using ROROROblox.App.Shell;
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
    public void TheNoticeFollowsTheModel_AndStepsAsideForTheContestedWarning()
    {
        var vm = Vm();
        vm.KnownIssuesNotice = ModelWith(Issue("a"));
        Assert.Equal(Loc.Format("MainWindow_KnownIssueNoticeOne", "Title of a"), vm.KnownIssueNoticeText);

        vm.SetContested(true);
        Assert.Equal(string.Empty, vm.KnownIssueNoticeText);

        vm.SetContested(false);
        Assert.NotEqual(string.Empty, vm.KnownIssueNoticeText);
    }

    [Fact]
    public void AModelAttachedWhileContestedStartsSuppressed()
    {
        var vm = Vm();
        vm.SetContested(true);

        vm.KnownIssuesNotice = ModelWith(Issue("a"));

        Assert.Equal(string.Empty, vm.KnownIssueNoticeText);
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
