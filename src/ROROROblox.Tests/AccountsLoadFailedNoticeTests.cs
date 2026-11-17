using System.IO;
using System.Reflection;
using ROROROblox.App.About;
using ROROROblox.App.Modals;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Item 8 (c) and the dialog half of (b). A failed account load leaves an empty list that reads as "every
/// account is gone", so the window says why, in a strip that is not the StatusBanner (the next action
/// overwrites that). It must not show on a normal empty list or a first run, it clears when a later load
/// works, and the first-run tour does not open on top of it. The Start fresh dialog stays open and names the
/// file when the store could not set it aside.
/// </summary>
public sealed class AccountsLoadFailedNoticeTests
{
    /// <summary>Forwards to the real store, except ListAsync throws while <see cref="Failure"/> is set.</summary>
    public class FlakyStore : DispatchProxy
    {
        public IAccountStore Inner { get; set; } = null!;
        public Exception? Failure { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(IAccountStore.ListAsync) && Failure is { } ex)
            {
                return Task.FromException<IReadOnlyList<Account>>(ex);
            }
            try { return targetMethod.Invoke(Inner, args); }
            catch (TargetInvocationException tie) when (tie.InnerException is not null) { throw tie.InnerException; }
        }
    }

    private static (MainViewModel Vm, FlakyStore Flaky, string Path) Build(Exception? failure)
    {
        FlakyStore? flaky = null;
        var (vm, _, _, path) = MainViewModelTests.Build(wrapStore: inner =>
        {
            var proxy = DispatchProxy.Create<IAccountStore, FlakyStore>();
            flaky = (FlakyStore)(object)proxy;
            flaky.Inner = inner;
            flaky.Failure = failure;
            return proxy;
        });
        return (vm, flaky!, path);
    }

    [Fact]
    public async Task AnUnexpectedStoreFailure_ShowsTheNotice_WithWhatHappenedAndWhatToDo()
    {
        var (vm, _, path) = Build(new NullReferenceException("simulated"));
        try
        {
            await vm.LoadAsync();

            Assert.True(vm.AccountsLoadFailed);
            Assert.Contains("couldn't be read", vm.AccountsLoadFailedText, StringComparison.Ordinal);
            Assert.Contains("wasn't changed", vm.AccountsLoadFailedText, StringComparison.Ordinal);
            Assert.Contains("Restart RoRoRo", vm.AccountsLoadFailedText, StringComparison.Ordinal);
            Assert.Empty(vm.Accounts);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ANormalEmptyList_AndAFirstRun_ShowNoNotice()
    {
        var (vm, _, path) = Build(failure: null);
        try
        {
            Assert.False(File.Exists(path)); // a first run: no vault yet

            await vm.LoadAsync();

            Assert.False(vm.AccountsLoadFailed);
            Assert.Equal(string.Empty, vm.AccountsLoadFailedText);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ALaterLoadThatWorks_TakesTheNoticeDown_AndRaisesPropertyChanged()
    {
        var (vm, flaky, path) = Build(new InvalidCastException("simulated"));
        try
        {
            await vm.LoadAsync();
            Assert.True(vm.AccountsLoadFailed);

            var changed = new List<string?>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            flaky.Failure = null;
            await vm.LoadAsync();

            Assert.False(vm.AccountsLoadFailed);
            Assert.Equal(string.Empty, vm.AccountsLoadFailedText);
            Assert.Contains(nameof(MainViewModel.AccountsLoadFailedText), changed);
            Assert.Contains(nameof(MainViewModel.AccountsLoadFailed), changed);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task TheCorruptVaultPath_HasItsOwnDialog_AndShowsNoLoadFailedNotice()
    {
        var (vm, _, path) = Build(new AccountStoreCorruptException("simulated"));
        try
        {
            vm.DpapiCorruptPrompt = () => false;
            vm.QuitApplication = () => { };

            await vm.LoadAsync();

            Assert.False(vm.AccountsLoadFailed);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task TheFirstRunTour_DoesNotOpenOverAFailedLoad()
    {
        var (vm, _, path) = Build(new NullReferenceException("simulated"));
        try
        {
            await vm.LoadAsync();

            // The same call MainWindow.OnLoaded makes: first run, empty list, failed load.
            Assert.False(WelcomeWindow.ShouldShowOnStartup(isFirstRun: true, vm.Accounts.Count, vm.AccountsLoadFailed));
            // The rule for a genuine first run is unchanged.
            Assert.True(WelcomeWindow.ShouldShowOnStartup(isFirstRun: true, accountCount: 0, accountsLoadFailed: false));
            Assert.True(WelcomeWindow.ShouldShowOnStartup(isFirstRun: true, accountCount: 0));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void TheMainWindowShowsTheNotice_AndItKeepsTheStripVisible()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate ROROROblox.slnx.");
        var xaml = File.ReadAllText(Path.Combine(root!, "src", "ROROROblox.App", "MainWindow.xaml"));

        Assert.Contains("""<Condition Binding="{Binding AccountsLoadFailedText}" Value="" />""", xaml);
        Assert.Contains("""Text="{Binding AccountsLoadFailedText}" """.TrimEnd(), xaml);

        var code = File.ReadAllText(Path.Combine(root!, "src", "ROROROblox.App", "MainWindow.xaml.cs"));
        Assert.Contains("mvm.AccountsLoadFailed", code);
    }

    // ---- the dialog's decision ----

    [Fact]
    public async Task StartFreshAttempt_ASetAsideThatWorked_ClosesTheDialog()
    {
        var outcome = await StartFreshAttempt.RunAsync(() => Task.FromResult(new StartFreshResult(["accounts.dat.corrupt-1"], [])));

        Assert.True(outcome.Succeeded);
    }

    [Fact]
    public async Task StartFreshAttempt_AFailedSetAside_KeepsTheDialogOpen_AndNamesTheFile()
    {
        var outcome = await StartFreshAttempt.RunAsync(() => Task.FromResult(new StartFreshResult([], ["accounts.dat"])));

        Assert.False(outcome.Succeeded);
        Assert.Contains("accounts.dat", outcome.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Nothing was deleted", outcome.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartFreshAttempt_AStoreThatThrows_IsAFailureToShow_NotACrash()
    {
        var outcome = await StartFreshAttempt.RunAsync(() => throw new InvalidOperationException("simulated"));

        Assert.False(outcome.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(outcome.FailureMessage));
    }

    [Fact]
    public void TheDialog_StaysOpenOnFailure_AndQuitStaysTheDefault()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate ROROROblox.slnx.");
        var modals = Path.Combine(root!, "src", "ROROROblox.App", "Modals");
        var code = File.ReadAllText(Path.Combine(modals, "DpapiCorruptWindow.xaml.cs"));
        var xaml = File.ReadAllText(Path.Combine(modals, "DpapiCorruptWindow.xaml"));

        // DialogResult is only ever set inside the success branch, after the attempt reports success.
        var success = code.IndexOf("if (outcome.Succeeded)", StringComparison.Ordinal);
        var first = code.IndexOf("DialogResult = true", StringComparison.Ordinal);
        Assert.True(success >= 0 && first > success, "DialogResult = true must sit behind outcome.Succeeded.");
        Assert.Equal(1, code.Split("DialogResult = true").Length - 1);
        Assert.DoesNotContain("catch\n", code.Replace("\r\n", "\n")); // no bare swallow like the old rename

        Assert.Contains("IsDefault=\"True\"", xaml);
        var quitButton = xaml[xaml.IndexOf("Content=\"{loc:Loc DpapiCorruptWindow_Quit}\"", StringComparison.Ordinal)..];
        Assert.Contains("IsCancel=\"True\"", quitButton[..quitButton.IndexOf("/>", StringComparison.Ordinal)]);
    }
}
