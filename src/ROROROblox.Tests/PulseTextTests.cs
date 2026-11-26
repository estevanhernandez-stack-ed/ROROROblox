using System.Globalization;
using ROROROblox.App.Games;
using ROROROblox.App.Localization;
using ROROROblox.App.ViewModels;
using ROROROblox.Core.Library;

namespace ROROROblox.Tests;

/// <summary>
/// v1.41 item 10: the words for a game's pulse. The count is the full number grouped by the app's
/// language; "updated" follows the "last played" ladder inside eleven months and a month and year past it;
/// each half drops on its own and the two join with the Library's existing joiner.
/// </summary>
[Collection("MutatesUiCulture")]
public class PulseTextTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
    private readonly CultureInfo _prevUi = TranslationSource.Instance.CurrentCulture;
    private readonly CultureInfo _prevFormat = CultureInfo.CurrentCulture;

    public PulseTextTests()
    {
        TranslationSource.Instance.CurrentCulture = new CultureInfo("en");
        AccountSummary.ClockOverride.Value = () => Now;
    }

    public void Dispose()
    {
        AccountSummary.ClockOverride.Value = null;
        TranslationSource.Instance.CurrentCulture = _prevUi;
        CultureInfo.CurrentCulture = _prevFormat;
    }

    [Fact]
    public void Count_IsTheFullNumber_Grouped()
    {
        Assert.Equal("81,827 playing", PulseText.Count(new GamePulseView(81827, null), isPrivateServer: false));
    }

    [Fact]
    public void Count_ZeroReads_ZeroPlaying()
    {
        Assert.Equal("0 playing", PulseText.Count(new GamePulseView(0, null), isPrivateServer: false));
    }

    [Fact]
    public void Count_IsEmpty_ForAPrivateServer_AnExpiredCount_AndNoReading()
    {
        Assert.Equal(string.Empty, PulseText.Count(new GamePulseView(500, null), isPrivateServer: true));
        Assert.Equal(string.Empty, PulseText.Count(new GamePulseView(null, Now.AddDays(-1)), isPrivateServer: false));
        Assert.Equal(string.Empty, PulseText.Count(null, isPrivateServer: false));
    }

    [Fact]
    public void Count_GroupsByTheAppLanguage_NotTheWindowsRegion()
    {
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        TranslationSource.Instance.CurrentCulture = new CultureInfo("de");

        var text = PulseText.Count(new GamePulseView(81827, null), isPrivateServer: false);

        Assert.Contains("81.827", text);
        Assert.DoesNotContain("81,827", text);
    }

    [Fact]
    public void Updated_InsideAWeek_UsesTheLastPlayedLadder()
    {
        Assert.Equal("updated 3 hr ago", PulseText.Updated(new GamePulseView(1, Now.AddHours(-3))));
        Assert.Equal("updated " + AccountSummary.RelativeAgo(Now.AddDays(-3)), PulseText.Updated(new GamePulseView(1, Now.AddDays(-3))));
    }

    [Fact]
    public void Updated_PastAWeekAndInsideElevenMonths_IsTheShortDate_WithNoYear()
    {
        var when = Now.AddDays(-40);
        var text = PulseText.Updated(new GamePulseView(1, when));

        Assert.Equal("updated " + AccountSummary.RelativeAgo(when), text);
        Assert.DoesNotContain("2026", text);
    }

    [Fact]
    public void Updated_PastElevenMonths_IsMonthAndYear()
    {
        var when = new DateTimeOffset(new DateTime(2023, 3, 9, 12, 0, 0, DateTimeKind.Local));
        Assert.Equal("updated Mar 2023", PulseText.Updated(new GamePulseView(1, when)));
    }

    [Fact]
    public void Updated_AtTheElevenMonthEdge_SwitchesFromTheShortDateToMonthYear()
    {
        var inside = Now.AddMonths(-11).AddDays(1);
        var outside = Now.AddMonths(-11).AddDays(-1);

        Assert.Equal("updated " + AccountSummary.RelativeAgo(inside), PulseText.Updated(new GamePulseView(1, inside)));
        Assert.Equal("updated " + DisplayDateFor(outside), PulseText.Updated(new GamePulseView(1, outside)));
    }

    private static string DisplayDateFor(DateTimeOffset when)
        => DisplayDate.MonthYear(when);

    [Fact]
    public void Updated_WithTheAppLanguageAndTheRegionSetApart_FollowsTheAppLanguage()
    {
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        TranslationSource.Instance.CurrentCulture = new CultureInfo("fr");
        var when = new DateTimeOffset(new DateTime(2023, 3, 9, 12, 0, 0, DateTimeKind.Local));

        var text = PulseText.Updated(new GamePulseView(1, when));

        Assert.StartsWith("mis à jour", text);
        Assert.Contains("2023", text);
        Assert.DoesNotContain("Mar", text);
    }

    [Fact]
    public void Updated_IsEmpty_WhenTheDateIsAbsent()
    {
        Assert.Equal(string.Empty, PulseText.Updated(new GamePulseView(5, null)));
        Assert.Equal(string.Empty, PulseText.Updated(null));
    }

    [Fact]
    public void Joined_JoinsBothHalves_WithTheExistingJoiner()
    {
        var text = PulseText.Joined(new GamePulseView(81827, Now.AddHours(-3)), isPrivateServer: false);

        Assert.Equal("81,827 playing · updated 3 hr ago", text);
    }

    [Fact]
    public void Joined_EachHalfDropsOnItsOwn()
    {
        Assert.Equal("5 playing", PulseText.Joined(new GamePulseView(5, null), isPrivateServer: false));
        Assert.Equal("updated 3 hr ago", PulseText.Joined(new GamePulseView(null, Now.AddHours(-3)), isPrivateServer: false));
        Assert.Equal(string.Empty, PulseText.Joined(new GamePulseView(null, null), isPrivateServer: false));
        Assert.Equal(string.Empty, PulseText.Joined(null, isPrivateServer: false));
    }

    [Fact]
    public void Joined_ForAPrivateServer_IsTheUpdatedHalfOnly()
    {
        Assert.Equal("updated 3 hr ago", PulseText.Joined(new GamePulseView(9000, Now.AddHours(-3)), isPrivateServer: true));
    }
}
