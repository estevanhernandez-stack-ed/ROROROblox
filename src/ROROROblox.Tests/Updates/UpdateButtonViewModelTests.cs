using Microsoft.Extensions.Time.Testing;
using ROROROblox.App.Localization;
using ROROROblox.App.Updates;
using ROROROblox.Core;
using Xunit;

namespace ROROROblox.Tests.Updates;

/// <summary>
/// What the main window's header button shows and does, per state. The view model is the face of
/// <see cref="UpdateAvailability"/>; the service itself is covered in <see cref="UpdateAvailabilityTests"/>.
/// </summary>
[Collection("MutatesUiCulture")]
public sealed class UpdateButtonViewModelTests : IDisposable
{
    private readonly List<string> _accountStores = [];

    public void Dispose()
    {
        foreach (var path in _accountStores.Where(File.Exists)) File.Delete(path);
    }

    private sealed class RecordingShell : IShellOpener
    {
        public List<string> Opened { get; } = [];
        public void Open(string path) => Opened.Add(path);
    }

    private sealed class InlineUi : IUiDispatcher
    {
        public void Invoke(Action action) => action();
    }

    private sealed class YesProbe : IStoreUpdateProbe
    {
        public Task<bool> IsUpdateWaitingAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private (App.ViewModels.MainViewModel Vm, RecordingShell Shell, Func<int> Quits) Build(UpdateAvailability? service)
    {
        var shell = new RecordingShell();
        var (vm, _, _, path) = MainViewModelTests.Build(shellOpener: shell, uiDispatcher: new InlineUi());
        _accountStores.Add(path);
        var quits = 0;
        vm.QuitApplication = () => quits++;
        vm.UpdateAvailability = service;
        return (vm, shell, () => quits);
    }

    private static UpdateAvailability Service(bool packaged) =>
        new(packaged, new YesProbe(), new FakeTimeProvider());

    [Fact]
    public void WithNothingWaiting_TheButtonIsNotThere_AndItsCommandDoesNothing()
    {
        var (vm, shell, quits) = Build(Service(packaged: true));

        Assert.False(vm.UpdateButtonVisible);
        vm.UpdateButtonCommand.Execute(null);

        Assert.Empty(shell.Opened);
        Assert.Equal(0, quits());
    }

    [Fact]
    public void WithNoServiceAtAll_TheButtonIsNotThere()
    {
        var (vm, _, _) = Build(null);

        Assert.False(vm.UpdateButtonVisible);
        Assert.Equal(string.Empty, vm.UpdateButtonTooltip);
    }

    [Fact]
    public async Task AStoreUpdate_ShowsTheStoreLabelAndTooltip_AndTheClickOpensTheStorePage()
    {
        var service = Service(packaged: true);
        var (vm, shell, quits) = Build(service);
        var notified = new List<string>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName ?? "");

        await service.CheckNowAsync();

        Assert.True(vm.UpdateButtonVisible);
        Assert.Equal(Loc.Get("MainWindow_UpdateAvailable"), vm.UpdateButtonLabel);
        Assert.Equal(Loc.Get("MainWindow_UpdateStoreTooltip"), vm.UpdateButtonTooltip);
        Assert.Contains(nameof(vm.UpdateButtonVisible), notified);
        Assert.Contains(nameof(vm.UpdateButtonLabel), notified);

        vm.UpdateButtonCommand.Execute(null);

        Assert.Equal(["ms-windows-store://pdp/?productid=9NMJCS390KWB"], shell.Opened);
        Assert.Equal(0, quits());
    }

    [Fact]
    public void AStagedUpdate_ShowsQuitToUpdate_NamesTheVersion_AndTheClickTakesTheQuitPath()
    {
        var service = Service(packaged: false);
        var (vm, shell, quits) = Build(service);

        service.ReportStaged("1.40.0");

        Assert.True(vm.UpdateButtonVisible);
        Assert.Equal(Loc.Get("MainWindow_UpdateQuitToUpdate"), vm.UpdateButtonLabel);
        Assert.Equal(Loc.Format("MainWindow_UpdateStagedTooltip", "1.40.0"), vm.UpdateButtonTooltip);
        Assert.Contains("1.40.0", vm.UpdateButtonTooltip);

        vm.UpdateButtonCommand.Execute(null);

        Assert.Equal(1, quits());
        Assert.Empty(shell.Opened);
    }

    [Fact]
    public void AStagedUpdate_WithNoVersionKnown_UsesTheVersionlessTooltip()
    {
        var service = Service(packaged: false);
        var (vm, _, _) = Build(service);

        service.ReportStaged("");

        Assert.Equal(Loc.Get("MainWindow_UpdateStagedTooltipNoVersion"), vm.UpdateButtonTooltip);
    }

    [Fact]
    public void ADevBuild_NeverShowsTheButton()
    {
        var (vm, _, _) = Build(Service(packaged: false));

        Assert.False(vm.UpdateButtonVisible);
    }

    [Fact]
    public void AChangeRaisedOffTheUiThread_IsMarshalledThroughTheDispatcher()
    {
        var shell = new RecordingShell();
        var queued = new List<Action>();
        var (vm, _, _, path) = MainViewModelTests.Build(shellOpener: shell, uiDispatcher: new DeferredUi(queued));
        _accountStores.Add(path);
        var service = Service(packaged: false);
        vm.UpdateAvailability = service;
        var raised = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.UpdateButtonVisible)) raised++; };

        service.ReportStaged("1.40.0");

        Assert.Equal(0, raised); // nothing was raised on the caller's thread
        Assert.Single(queued);
        queued[0]();
        Assert.Equal(1, raised);
    }

    private sealed class DeferredUi(List<Action> queued) : IUiDispatcher
    {
        public void Invoke(Action action) => queued.Add(action);
    }

    [Fact]
    public void EveryNewKey_IsAnEnglishStringInTheNeutralCatalog_AndInAllSixCultures()
    {
        string[] keys =
        [
            "MainWindow_UpdateAvailable", "MainWindow_UpdateQuitToUpdate", "MainWindow_UpdateStoreTooltip",
            "MainWindow_UpdateStagedTooltip", "MainWindow_UpdateStagedTooltipNoVersion",
        ];
        var previous = TranslationSource.Instance.CurrentCulture;
        try
        {
            foreach (var key in keys)
            {
                var english = Loc.Get(key);
                Assert.NotEqual(key, english);
                Assert.False(string.IsNullOrWhiteSpace(english));
            }

            Assert.Equal("Update available", Loc.Get("MainWindow_UpdateAvailable"));
            Assert.Equal("Quit to update", Loc.Get("MainWindow_UpdateQuitToUpdate"));

            foreach (var culture in new[] { "de", "es", "fr", "pl", "pt-BR", "ru" })
            {
                TranslationSource.Instance.CurrentCulture = new System.Globalization.CultureInfo(culture);
                foreach (var key in keys)
                {
                    var value = Loc.Get(key);
                    Assert.NotEqual(key, value);
                    Assert.False(string.IsNullOrWhiteSpace(value), $"{culture}/{key}");
                }

                Assert.NotEqual("Update available", Loc.Get("MainWindow_UpdateAvailable"));
                Assert.Contains("Microsoft Store", Loc.Get("MainWindow_UpdateStoreTooltip"));
                Assert.Contains("1.2.3", Loc.Format("MainWindow_UpdateStagedTooltip", "1.2.3"));
            }
        }
        finally
        {
            TranslationSource.Instance.CurrentCulture = previous;
        }
    }
}
