using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// F-018 — the product's core switch was reported only inside a right-click menu Windows hides
/// behind an overflow chevron by default. This drives the path that puts it on the main window.
/// <para>
/// The subscription is what these actually test. Six places call <c>UpdateStatus</c>, and a
/// second notification bolted on beside each of them is the shape of bug this repo has shipped
/// twice — a fix applied to one copy of a thing and missed on another. So the tray raises the
/// change from inside its own single funnel and the view model listens; there is no second call
/// site to forget.
/// </para>
/// </summary>
public class MultiInstanceFooterTests
{
    [Fact]
    public void TheFooterFollowsEveryTrayTransition()
    {
        var tray = new RecordingTray();
        var (vm, _, _, path) = MainViewModelTests.Build(tray: tray);
        try
        {
            tray.RaiseStatusChanged(MultiInstanceState.On);
            Assert.Equal(MultiInstanceStatusLine.StatusBar(MultiInstanceState.On), vm.MultiInstanceSummary);
            Assert.True(vm.MultiInstanceIsHealthy);

            tray.RaiseStatusChanged(MultiInstanceState.Error);
            Assert.Equal(MultiInstanceStatusLine.StatusBar(MultiInstanceState.Error), vm.MultiInstanceSummary);
            Assert.False(vm.MultiInstanceIsHealthy);

            tray.RaiseStatusChanged(MultiInstanceState.Off);
            Assert.Equal(MultiInstanceStatusLine.StatusBar(MultiInstanceState.Off), vm.MultiInstanceSummary);
            Assert.False(vm.MultiInstanceIsHealthy);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void TheFooterAnnouncesItsChangesToTheBinding()
    {
        // A property the view model updates without raising PropertyChanged is a footer that shows
        // the startup value forever — which looks exactly like a working feature until the mutex
        // is actually lost, i.e. the one moment it matters.
        var tray = new RecordingTray();
        var (vm, _, _, path) = MainViewModelTests.Build(tray: tray);
        try
        {
            var announced = new List<string>();
            vm.PropertyChanged += (_, e) => announced.Add(e.PropertyName ?? "");

            tray.RaiseStatusChanged(MultiInstanceState.On);

            Assert.Contains(nameof(MainViewModel.MultiInstanceSummary), announced);
            Assert.Contains(nameof(MainViewModel.MultiInstanceIsHealthy), announced);
            Assert.Contains(nameof(MainViewModel.MultiInstanceTooltip), announced);
            Assert.Contains(nameof(MainViewModel.MultiInstanceNeedsAttention), announced);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void OnlyTheErrorStateAsksForAttention()
    {
        // ON and OFF are both ordinary. ERROR is the only one where a user has something to do,
        // and it is also the one that arrives without any action of theirs — nothing on screen
        // changed, the lock was just lost underneath them.
        var tray = new RecordingTray();
        var (vm, _, _, path) = MainViewModelTests.Build(tray: tray);
        try
        {
            tray.RaiseStatusChanged(MultiInstanceState.On);
            Assert.False(vm.MultiInstanceNeedsAttention);

            tray.RaiseStatusChanged(MultiInstanceState.Off);
            Assert.False(vm.MultiInstanceNeedsAttention);

            tray.RaiseStatusChanged(MultiInstanceState.Error);
            Assert.True(vm.MultiInstanceNeedsAttention);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void TheTooltipFollowsToo()
    {
        // The footer line is three words; everything about what the state means for launching, and
        // where the switch lives, is in the tooltip. A stale tooltip on a fresh line is worse than
        // no tooltip.
        var tray = new RecordingTray();
        var (vm, _, _, path) = MainViewModelTests.Build(tray: tray);
        try
        {
            tray.RaiseStatusChanged(MultiInstanceState.Error);
            Assert.Equal(MultiInstanceStatusLine.StatusBarTooltip(MultiInstanceState.Error), vm.MultiInstanceTooltip);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void BeforeAnyTransitionItReportsOffRatherThanNothing()
    {
        // Startup order: the view model is built before App resolves the mutex. An empty footer
        // cell for that window would read as "this app has no such feature"; OFF is both true at
        // that moment and the safe thing to be wrong about, since it under-promises.
        var (vm, _, _, path) = MainViewModelTests.Build();
        try
        {
            Assert.Equal(MultiInstanceStatusLine.StatusBar(MultiInstanceState.Off), vm.MultiInstanceSummary);
            Assert.False(vm.MultiInstanceIsHealthy);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
