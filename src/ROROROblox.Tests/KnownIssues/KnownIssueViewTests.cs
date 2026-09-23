using System.Globalization;
using ROROROblox.App.KnownIssues;
using ROROROblox.App.Localization;
using ROROROblox.Core.KnownIssues;
using static ROROROblox.Tests.KnownIssues.KnownIssueBuilder;

namespace ROROROblox.Tests.KnownIssues;

public class KnownIssueViewTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static readonly Version V739 = Version.Parse("0.739.0.7390687");
    private static readonly Version V740 = Version.Parse("0.740.0.7400927");

    [Fact]
    public void TheEntryCarriesItsTextAndPostedDate()
    {
        var view = KnownIssueView.From(Issue("a"), V740, Culture);

        Assert.Equal("Title of a", view.Title);
        Assert.Equal("Symptom of a", view.Symptom);
        Assert.Equal("Workaround for a", view.Workaround);
        Assert.Equal(Loc.Format("KnownIssuesPage_Posted", new DateOnly(2026, 9, 23).ToString("d", Culture)), view.PostedText);
    }

    [Fact]
    public void NoVersionsMeansNoVersionLine()
    {
        Assert.Null(KnownIssueView.From(Issue("a"), V740, Culture).VersionText);
    }

    [Fact]
    public void TheVersionLineSaysAffectedFixedNotYetOrUnknown()
    {
        Assert.Equal(Loc.Format("KnownIssuesPage_VersionAffected", "0.739"),
            KnownIssueView.From(Issue("a", fixedIn: new Version(0, 740)), V739, Culture).VersionText);
        Assert.Equal(Loc.Format("KnownIssuesPage_VersionFixed", "0.740", "0.740"),
            KnownIssueView.From(Issue("a", fixedIn: new Version(0, 740)), V740, Culture).VersionText);
        Assert.Equal(Loc.Format("KnownIssuesPage_VersionNotYet", "0.740", "0.739"),
            KnownIssueView.From(Issue("a", from: new Version(0, 740)), V739, Culture).VersionText);
        Assert.Equal(Loc.Get("KnownIssuesPage_VersionUnknown"),
            KnownIssueView.From(Issue("a", fixedIn: new Version(0, 740)), null, Culture).VersionText);
    }

    /// <summary>
    /// Final-fix wave, commit D: a two-part bound compared against a shortened running version
    /// ("0.740") reads fine, but a bound written with more precision than that ("0.740.0.7400838")
    /// compared against a shortened running version ("0.740") looks like a mismatch that isn't one.
    /// Once the bound carries a build number, the running version is shown in full so the two
    /// numbers in the sentence are actually comparable.
    /// </summary>
    [Fact]
    public void AFourPartFixedInShowsTheRunningVersionInFullToo()
    {
        var fixedIn = Version.Parse("0.740.0.7400838");
        var running = Version.Parse("0.740.0.7400927");

        var view = KnownIssueView.From(Issue("a", fixedIn: fixedIn), running, Culture);

        Assert.Equal(
            Loc.Format("KnownIssuesPage_VersionFixed", "0.740.0.7400838", "0.740.0.7400927"),
            view.VersionText);
    }

    [Fact]
    public void AKnownFeatureGetsAButtonThatGoesThere()
    {
        var view = KnownIssueView.From(Issue("a", helps: new KnownIssueHelp(KnownIssueFeatures.MemoryWatchdog, "Watch memory.")), V740, Culture);

        Assert.Equal("Watch memory.", view.HelpText);
        Assert.Equal(Loc.Get("KnownIssuesPage_OpenMemorySettings"), view.HelpButtonText);
        Assert.Equal(KnownIssueFeatureRoute.MemorySettings, view.HelpRoute);
    }

    /// <summary>Review Focus 1: an entry written for a newer app still reads on this one.</summary>
    [Fact]
    public void AnUnknownFeatureShowsItsTextWithNoButton()
    {
        var view = KnownIssueView.From(Issue("a", helps: new KnownIssueHelp("teleport-helper", "Soon.")), V740, Culture);

        Assert.Equal("Soon.", view.HelpText);
        Assert.Null(view.HelpButtonText);
        Assert.Equal(KnownIssueFeatureRoute.None, view.HelpRoute);
    }

    [Fact]
    public void EachLinkIsNamedForAScreenReaderAndShowsItsAddress()
    {
        var link = new KnownIssueLink("DevForum", "https://devforum.roblox.com/t/4032374");

        var view = KnownIssueView.From(Issue("a", links: [link]), V740, Culture);

        var shown = Assert.Single(view.Links);
        Assert.Equal("DevForum", shown.Label);
        Assert.Equal("https://devforum.roblox.com/t/4032374", shown.Url);
        Assert.Equal(Loc.Format("KnownIssuesPage_OpenLink", "DevForum"), shown.AccessibleName);
    }
}
