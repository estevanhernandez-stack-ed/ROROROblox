using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ROROROblox.App.Localization;
using ROROROblox.App.Notices;
using ROROROblox.Core.Diagnostics;
using ROROROblox.Core.Feeds;

namespace ROROROblox.Tests.Notices;

/// <summary>
/// The one Roblox line's resolver (v1.41 item 7). Sentences are read in English: the catalog is a
/// process-wide singleton, so the class sits in the culture-mutating collection and pins "en".
/// </summary>
[Collection("MutatesUiCulture")]
public sealed class RobloxNoticeResolverTests : IDisposable
{
    private readonly CultureInfo _previous = TranslationSource.Instance.CurrentCulture;

    public RobloxNoticeResolverTests() => TranslationSource.Instance.CurrentCulture = new CultureInfo("en");

    public void Dispose() => TranslationSource.Instance.CurrentCulture = _previous;

    private static readonly RobloxUpdateState PendingNewer =
        new(RobloxUpdateKind.Pending, RobloxUpdateDirection.Newer, new Version(0, 748, 0, 7481526));

    private static readonly RobloxUpdateState PendingOlder =
        new(RobloxUpdateKind.Pending, RobloxUpdateDirection.Older, new Version(0, 748, 0, 7481526));

    private static RobloxStatusReading Outage(RobloxStatusRow row = RobloxStatusRow.Games, int others = 0) =>
        new(row, RobloxStatusLevel.Outage, others);

    private static RobloxLineInputs Quiet() => new(
        ContestedText: "",
        Status: RobloxStatusReading.None,
        KnownIssueText: "",
        DriftText: "",
        Update: RobloxUpdateState.Unknown,
        HoldBannerShowing: false,
        HasMultipleAccounts: true,
        Compact: false);

    private static RobloxLineInputs Everything() => Quiet() with
    {
        ContestedText = "contested text",
        Status = Outage(),
        KnownIssueText = "known issue text",
        DriftText = "drift text",
        Update = PendingNewer,
    };

    private static RobloxLineInputs Only(params RobloxNoticeKind[] kinds)
    {
        var all = Everything();
        return Quiet() with
        {
            ContestedText = kinds.Contains(RobloxNoticeKind.Contested) ? all.ContestedText : "",
            Status = kinds.Contains(RobloxNoticeKind.Outage) ? all.Status : RobloxStatusReading.None,
            KnownIssueText = kinds.Contains(RobloxNoticeKind.KnownIssue) ? all.KnownIssueText : "",
            DriftText = kinds.Contains(RobloxNoticeKind.Drift) ? all.DriftText : "",
            Update = kinds.Contains(RobloxNoticeKind.Update) ? all.Update : RobloxUpdateState.Unknown,
        };
    }

    [Fact]
    public void NothingTrue_NothingIsDrawn()
    {
        var state = RobloxNoticeResolver.Resolve(Quiet());

        Assert.Null(state.Top);
        Assert.Empty(state.More);
    }

    [Fact]
    public void TheOrderIsContestedOutageKnownIssueDriftUpdate_WithDriftTextPresent()
    {
        var state = RobloxNoticeResolver.Resolve(Everything());

        Assert.Equal(RobloxNoticeKind.Contested, state.Top!.Kind);
        Assert.Equal(
            [RobloxNoticeKind.Outage, RobloxNoticeKind.KnownIssue, RobloxNoticeKind.Drift, RobloxNoticeKind.Update],
            state.More.Select(n => n.Kind).ToArray());
    }

    [Theory]
    [InlineData("Contested", "Outage")]
    [InlineData("Outage", "KnownIssue")]
    [InlineData("KnownIssue", "Drift")]
    [InlineData("Drift", "Update")]
    public void EachNoticeBeatsTheOneBelowIt(string higherName, string lowerName)
    {
        var higher = Enum.Parse<RobloxNoticeKind>(higherName);
        var lower = Enum.Parse<RobloxNoticeKind>(lowerName);
        var state = RobloxNoticeResolver.Resolve(Only(higher, lower));

        Assert.Equal(higher, state.Top!.Kind);
        Assert.Equal(lower, Assert.Single(state.More).Kind);
    }

    [Fact]
    public void AKnownIssueBehindAHigherNoticeIsInMore_AndItsTextIsKeptWhole()
    {
        var state = RobloxNoticeResolver.Resolve(Only(RobloxNoticeKind.Contested, RobloxNoticeKind.KnownIssue));

        var behind = Assert.Single(state.More);
        Assert.Equal(RobloxNoticeKind.KnownIssue, behind.Kind);
        Assert.Equal("known issue text", behind.Text);
    }

    [Fact]
    public void WhenTheHigherNoticeClears_TheKnownIssueIsTopAgain()
    {
        var hidden = RobloxNoticeResolver.Resolve(Only(RobloxNoticeKind.Contested, RobloxNoticeKind.KnownIssue));
        var cleared = RobloxNoticeResolver.Resolve(Only(RobloxNoticeKind.KnownIssue));

        Assert.NotEqual(RobloxNoticeKind.KnownIssue, hidden.Top!.Kind);
        Assert.Equal(RobloxNoticeKind.KnownIssue, cleared.Top!.Kind);
        Assert.Empty(cleared.More);
    }

    [Theory]
    [InlineData(RobloxUpdateKind.Unknown)]
    [InlineData(RobloxUpdateKind.Current)]
    public void OnlyAPendingUpdateIsAnUpdateNotice(RobloxUpdateKind kind)
    {
        var state = RobloxNoticeResolver.Resolve(Quiet() with { Update = new RobloxUpdateState(kind) });

        Assert.Null(state.Top);
    }

    [Fact]
    public void TheHoldBannerSilencesTheUpdateNotice()
    {
        var shown = RobloxNoticeResolver.Resolve(Quiet() with { Update = PendingNewer });
        var held = RobloxNoticeResolver.Resolve(Quiet() with { Update = PendingNewer, HoldBannerShowing = true });

        Assert.Equal(RobloxNoticeKind.Update, shown.Top!.Kind);
        Assert.Null(held.Top);
    }

    [Fact]
    public void TheHoldBannerDoesNotSilenceAnythingElse()
    {
        var state = RobloxNoticeResolver.Resolve(Everything() with { HoldBannerShowing = true });

        Assert.Equal(RobloxNoticeKind.Contested, state.Top!.Kind);
        Assert.DoesNotContain(state.More, n => n.Kind == RobloxNoticeKind.Update);
        Assert.Equal(3, state.More.Count);
    }

    [Fact]
    public void ANewerClientCarriesTheLink_ARollbackHasTheSentenceAndNoLink()
    {
        var newer = RobloxNoticeResolver.Resolve(Quiet() with { Update = PendingNewer }).Top!;
        var older = RobloxNoticeResolver.Resolve(Quiet() with { Update = PendingOlder }).Top!;

        Assert.True(newer.WithLink);
        Assert.False(older.WithLink);
        Assert.Equal(newer.Text, older.Text);
        Assert.NotEqual(string.Empty, older.Text);
    }

    [Fact]
    public void OneSavedAccountReadsTheShortSentence_SeveralReadTheLongOne()
    {
        var one = RobloxNoticeResolver.Resolve(Quiet() with { Update = PendingNewer, HasMultipleAccounts = false }).Top!;
        var many = RobloxNoticeResolver.Resolve(Quiet() with { Update = PendingNewer, HasMultipleAccounts = true }).Top!;

        Assert.Equal("Roblox has an update. The next launch installs it.", one.Text);
        Assert.Equal(Loc.Get("RobloxLine_Update_Many"), many.Text);
        Assert.NotEqual(one.Text, many.Text);
        Assert.Contains("Launch multiple", many.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompactAlwaysReadsTheShortSentence(bool multipleAccounts)
    {
        var notice = RobloxNoticeResolver.Resolve(Quiet() with
        {
            Update = PendingNewer,
            HasMultipleAccounts = multipleAccounts,
            Compact = true,
        }).Top!;

        Assert.Equal(Loc.Get("RobloxLine_Update_One"), notice.Text);
    }

    [Theory]
    [InlineData(RobloxStatusLevel.Outage, "Roblox reports an outage: Games")]
    [InlineData(RobloxStatusLevel.PartialOutage, "Roblox reports a partial outage: Games")]
    [InlineData(RobloxStatusLevel.Degraded, "Roblox reports degraded performance: Games")]
    [InlineData(RobloxStatusLevel.SecurityEvent, "Roblox reports a security issue: Games")]
    [InlineData(RobloxStatusLevel.Maintenance, "Roblox reports maintenance under way: Games")]
    [InlineData(RobloxStatusLevel.Unrecognised, "Roblox reports a problem: Games")]
    public void AnOutageNamesTheWorstRowAfterAColon_InThatLevelsOwnSentence(RobloxStatusLevel level, string expected)
    {
        var notice = RobloxNoticeResolver.Resolve(Quiet() with
        {
            Status = new RobloxStatusReading(RobloxStatusRow.Games, level, 0),
        }).Top!;

        Assert.Equal(RobloxNoticeKind.Outage, notice.Kind);
        Assert.Equal(expected, notice.Text);
        Assert.True(notice.WithLink);
    }

    [Theory]
    [InlineData(RobloxStatusRow.Website, "Website")]
    [InlineData(RobloxStatusRow.Games, "Games")]
    [InlineData(RobloxStatusRow.Avatar, "Avatars")]
    [InlineData(RobloxStatusRow.GameJoin, "Joining games")]
    public void EachRowHasItsOwnName(RobloxStatusRow row, string name)
    {
        var notice = RobloxNoticeResolver.Resolve(Quiet() with { Status = Outage(row) }).Top!;

        Assert.Equal($"Roblox reports an outage: {name}", notice.Text);
    }

    [Theory]
    [InlineData(1, "Roblox reports an outage: Website and 1 more")]
    [InlineData(2, "Roblox reports an outage: Website and 2 more")]
    [InlineData(3, "Roblox reports an outage: Website and 3 more")]
    public void OtherAffectedRowsAreCounted(int others, string expected)
    {
        var notice = RobloxNoticeResolver.Resolve(Quiet() with { Status = Outage(RobloxStatusRow.Website, others) }).Top!;

        Assert.Equal(expected, notice.Text);
    }

    [Fact]
    public void ContestedOutageAndDriftAreWarnings_KnownIssueAndUpdateAreNot()
    {
        var state = RobloxNoticeResolver.Resolve(Everything());
        var tones = new[] { state.Top! }.Concat(state.More).ToDictionary(n => n.Kind, n => n.Tone);

        Assert.Equal(RobloxNoticeTone.Warning, tones[RobloxNoticeKind.Contested]);
        Assert.Equal(RobloxNoticeTone.Warning, tones[RobloxNoticeKind.Outage]);
        Assert.Equal(RobloxNoticeTone.Info, tones[RobloxNoticeKind.KnownIssue]);
        Assert.Equal(RobloxNoticeTone.Warning, tones[RobloxNoticeKind.Drift]);
        Assert.Equal(RobloxNoticeTone.Info, tones[RobloxNoticeKind.Update]);
    }

    // ---- the strings -------------------------------------------------------------------------

    private static readonly string[] Forbidden = ["ban", "captcha", "verification", "session", "account", "RoRoRo"];

    private static Dictionary<string, string> RobloxLineValues()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate ROROROblox.slnx.");
        var path = Path.Combine(root!, "src", "ROROROblox.App", "Properties", "Strings.resx");
        return XDocument.Load(path).Root!
            .Elements("data")
            .Where(d => ((string?)d.Attribute("name") ?? "").StartsWith("RobloxLine_", StringComparison.Ordinal))
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");
    }

    [Fact]
    public void TheSeventeenSentencesCarryNoneOfTheForbiddenWords()
    {
        var values = RobloxLineValues();

        // Seventeen keys, two of them plural families (one/other/few/many) in the catalog.
        var bases = values.Keys.Select(k => Regex.Replace(k, "_(one|other|few|many)$", "")).Distinct().ToList();
        Assert.Equal(17, bases.Count);
        Assert.Equal(23, values.Count);

        foreach (var (key, value) in values)
        {
            Assert.NotEqual(string.Empty, value);
            foreach (var word in Forbidden)
            {
                Assert.False(value.Contains(word, StringComparison.OrdinalIgnoreCase), $"{key} contains \"{word}\": {value}");
            }
        }
    }

    [Fact]
    public void EveryPluralFamilyCarriesEveryCategoryTheAppAsksFor()
    {
        var values = RobloxLineValues();

        foreach (var family in new[] { "RobloxLine_Status_AndMore", "RobloxLine_More_Name" })
        {
            foreach (var category in new[] { "one", "other", "few", "many" })
            {
                Assert.True(values.ContainsKey($"{family}_{category}"), $"{family}_{category} is missing");
            }
        }
    }

    [Fact]
    public void EveryKeyTheCodeAsksForExistsInTheCatalog()
    {
        var values = RobloxLineValues();
        var rendered = new List<string>();
        foreach (var level in Enum.GetValues<RobloxStatusLevel>().Where(l => l != RobloxStatusLevel.Operational))
        {
            foreach (var row in Enum.GetValues<RobloxStatusRow>())
            {
                rendered.Add(RobloxNoticeResolver.Resolve(Quiet() with { Status = new RobloxStatusReading(row, level, 1) }).Top!.Text);
            }
        }

        // A catalog miss renders the key name itself.
        Assert.DoesNotContain(rendered, t => t.Contains("RobloxLine_", StringComparison.Ordinal));
        Assert.All(values.Keys, k => Assert.NotEqual(k, Loc.Get(k)));
    }
}
