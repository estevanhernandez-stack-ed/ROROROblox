using System.Text.RegularExpressions;
using ROROROblox.Core;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// The idle-warn threshold: the offered set and the nearest-value rule, the picker staying on that
/// set, and the view model's tick re-reading it so a settings.json edit reaches the monitor.
/// </summary>
public class IdleSettingsTests : IDisposable
{
    private readonly string _filePath = Path.Combine(Path.GetTempPath(), $"rororo-idle-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { File.Delete(_filePath); } catch (IOException) { }
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(9, 10)]
    [InlineData(10, 10)]
    [InlineData(11, 10)]   // tie between 10 and 12 takes the lower, the earlier warning
    [InlineData(13, 12)]
    [InlineData(14, 15)]
    [InlineData(16, 15)]
    [InlineData(17, 18)]
    [InlineData(42, 18)]
    [InlineData(0, 15)]    // not a preference, a bad write: the default
    [InlineData(-5, 15)]
    public void Normalise_MapsToTheNearestOfferedValue(int stored, int expected)
    {
        Assert.Equal(expected, IdleWarnThresholdOptions.Normalise(stored));
    }

    [Fact]
    public void EveryOfferedValue_IsItsOwnNearest_AndTheDefaultIsOffered()
    {
        foreach (var v in IdleWarnThresholdOptions.Offered)
        {
            Assert.Equal(v, IdleWarnThresholdOptions.Normalise(v));
        }
        Assert.Contains(IdleWarnThresholdOptions.Default, IdleWarnThresholdOptions.Offered);
    }

    [Fact]
    public async Task Getter_NormalisesAnOutOfSetStoredValue_WithoutRewritingTheFile()
    {
        File.WriteAllText(_filePath, "{ \"version\": 1, \"idleWarnThresholdMinutes\": 1 }");
        var before = File.ReadAllText(_filePath);

        using var settings = new AppSettings(_filePath);

        Assert.Equal(10, await settings.GetIdleWarnThresholdMinutesAsync());
        Assert.Equal(before, File.ReadAllText(_filePath));
    }

    [Fact]
    public async Task Setter_StoresTheNormalisedValue()
    {
        using var settings = new AppSettings(_filePath);
        await settings.SetIdleWarnThresholdMinutesAsync(17);
        Assert.Equal(18, await settings.GetIdleWarnThresholdMinutesAsync());
    }

    [Fact]
    public void ThePicker_OffersExactlyTheCoreSet()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.NotNull(root);
        var xaml = File.ReadAllText(Path.Combine(root!, "src", "ROROROblox.App", "Preferences", "SettingsPage.xaml"));
        var start = xaml.IndexOf("x:Name=\"IdleWarnThresholdPicker\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "IdleWarnThresholdPicker is gone; this fence is blind.");
        var end = xaml.IndexOf("</ComboBox>", start, StringComparison.Ordinal);
        var tags = Regex.Matches(xaml[start..end], @"<ComboBoxItem[^>]*Tag=""(\d+)""")
            .Select(m => int.Parse(m.Groups[1].Value)).ToArray();

        Assert.Equal(IdleWarnThresholdOptions.Offered, tags);
    }

    [Fact]
    public async Task TheTick_RereadsTheThreshold_ForAHandEditedSettingsFile()
    {
        var settings = new MainViewModelTests.FakeAppSettings { IdleWarnThresholdMinutes = 15 };
        var monitor = new MainViewModelTests.FakeActivityMonitor();
        var (vm, _, _, path) = MainViewModelTests.Build(settings: settings, activityMonitor: monitor);
        try
        {
            await vm.InitializeIdleSettingsAsync(settings);
            Assert.Equal(TimeSpan.FromMinutes(15), monitor.WarnThreshold);

            settings.IdleWarnThresholdMinutes = 12;   // the edit
            await vm.RefreshIdleSettingsAsync();       // what the 30 s tick runs

            Assert.Equal(TimeSpan.FromMinutes(12), monitor.WarnThreshold);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TheViewModel_SubscribesTheIdleReread_ToThePeriodicTick()
    {
        // PeriodicTick is internal and raised only by the DispatcherTimer, so a test cannot fire it.
        // Reading the wiring is what keeps the method above from being a method nothing calls.
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.NotNull(root);
        var src = File.ReadAllText(Path.Combine(root!, "src", "ROROROblox.App", "ViewModels", "MainViewModel.cs"));
        Assert.Contains("PeriodicTick += OnIdleSettingsTick;", src, StringComparison.Ordinal);
    }
}
