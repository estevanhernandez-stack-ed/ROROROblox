using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Time.Testing;
using ROROROblox.App.Localization;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// The rating ask as the main window sees it (2026-11-10): shown only when it is handed over at
/// startup, ended by either button, and never raised by a launch attaching mid-session.
/// Built through MainViewModelTests.Build, which disposes the decorator and stops the 30 s ticker.
/// </summary>
public sealed class RatingAskViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rororo-rating-vm-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 10, 15, 0, 0, TimeSpan.Zero));
    private readonly List<string> _accountStores = [];

    public RatingAskViewModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
        foreach (var path in _accountStores.Where(File.Exists)) File.Delete(path);
    }

    private RatingAsk AskAfter(int goodDays)
    {
        var ask = new RatingAsk(_dir, InstallChannel.Store, _time);
        for (var i = 0; i < goodDays; i++)
        {
            ask.RecordSuccessfulLaunch();
            _time.Advance(TimeSpan.FromDays(1));
        }
        return ask;
    }

    private (App.ViewModels.MainViewModel Vm, MainViewModelTests.FakeRobloxProcessTracker Tracker, RecordingShell Shell) Vm()
    {
        var shell = new RecordingShell();
        var (vm, _, tracker, path) = MainViewModelTests.Build(shellOpener: shell);
        _accountStores.Add(path);
        return (vm, tracker, shell);
    }

    [Fact]
    public void HandedAnAskThatIsDue_TheRowShows()
    {
        var (vm, _, _) = Vm();

        vm.RatingAsk = AskAfter(goodDays: 3);

        Assert.Equal(Loc.Get("MainWindow_RatingAsk"), vm.RatingAskText);
    }

    [Fact]
    public void HandedAnAskThatIsNotDue_TheRowStaysCollapsed()
    {
        var (vm, _, _) = Vm();

        vm.RatingAsk = AskAfter(goodDays: 2);

        Assert.Equal(string.Empty, vm.RatingAskText);
    }

    [Fact]
    public void ALaunchThatMakesTheThirdDay_RecordsIt_AndSaysNothingUntilTheNextStart()
    {
        // The ask must never appear in the middle of a launch. The third good day is recorded
        // while the row stays collapsed; the next start hands the ask over again and it shows.
        var (vm, tracker, _) = Vm();
        var ask = AskAfter(goodDays: 2);
        vm.RatingAsk = ask;

        tracker.RaiseAttached(new RobloxProcessEventArgs(Guid.NewGuid(), 4242));

        Assert.True(ask.ShouldAsk);
        Assert.Equal(string.Empty, vm.RatingAskText);

        var (nextStart, _, _) = Vm();
        nextStart.RatingAsk = new RatingAsk(_dir, InstallChannel.Store, _time);
        Assert.Equal(Loc.Get("MainWindow_RatingAsk"), nextStart.RatingAskText);
    }

    [Fact]
    public void RateRoRoRo_OpensTheStoreReviewPage_AndEndsTheAskForGood()
    {
        var (vm, _, shell) = Vm();
        vm.RatingAsk = AskAfter(goodDays: 3);

        vm.RateOnStoreCommand.Execute(null);

        Assert.Equal(new[] { RatingAsk.StoreReviewUri }, shell.Opened);
        Assert.Equal(string.Empty, vm.RatingAskText);
        Assert.False(new RatingAsk(_dir, InstallChannel.Store, _time).ShouldAsk);
    }

    [Fact]
    public void NotNow_OpensNothing_AndEndsTheAskForGood()
    {
        var (vm, _, shell) = Vm();
        vm.RatingAsk = AskAfter(goodDays: 3);

        vm.DismissRatingAskCommand.Execute(null);

        Assert.Empty(shell.Opened);
        Assert.Equal(string.Empty, vm.RatingAskText);
        Assert.False(new RatingAsk(_dir, InstallChannel.Store, _time).ShouldAsk);
    }

    /// <summary>
    /// The status strip is one Border whose Visibility collapses when EVERY row's text is empty,
    /// listed by hand in a MultiDataTrigger. A row added to the strip but not to that list is
    /// invisible whenever it is the only row with something to say, which for the rating ask is
    /// the normal case. This reads the markup and holds the two lists together.
    /// </summary>
    [Fact]
    public void EveryRowOfTheStatusStrip_IsNamedInTheConditionThatCollapsesIt()
    {
        var appDir = XamlStyleScanner.AppSourceDirectory();
        Assert.NotNull(appDir);
        var doc = XDocument.Load(Path.Combine(appDir!, "MainWindow.xaml"));
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        // The strip is the Border whose collapse condition names StatusBanner.
        var strip = doc.Descendants(ns + "Border").Single(b =>
            b.Elements(ns + "Border.Style").Descendants(ns + "Condition")
                .Any(c => (string?)c.Attribute("Binding") == "{Binding StatusBanner}"));

        var named = strip.Elements(ns + "Border.Style").Descendants(ns + "Condition")
            .Select(c => Regex.Match((string?)c.Attribute("Binding") ?? "", @"\{Binding (\w+)\}").Groups[1].Value)
            .Where(n => n.Length > 0)
            .ToHashSet();

        var rows = strip.Element(ns + "StackPanel")!.Elements()
            .Select(e => Regex.Match((string?)e.Attribute("Visibility") ?? "",
                @"\{Binding (\w+), Converter=\{StaticResource StringToVisibilityConverter\}\}").Groups[1].Value)
            .Where(n => n.Length > 0)
            .ToList();

        // Vacuity floor: the strip had five rows before the rating ask.
        Assert.True(rows.Count >= 6, $"expected at least six rows in the status strip, found {rows.Count}: the scan is not seeing them");
        Assert.Contains("RatingAskText", rows);
        var missing = rows.Where(r => !named.Contains(r)).ToList();
        Assert.True(missing.Count == 0,
            "these status-strip rows are not named in the strip's collapse condition, so the strip hides "
            + "them whenever they are the only row with text: " + string.Join(", ", missing));
    }

    private sealed class RecordingShell : IShellOpener
    {
        public List<string> Opened { get; } = [];
        public void Open(string path) => Opened.Add(path);
    }
}
