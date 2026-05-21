using ROROROblox.App.ViewModels;

namespace ROROROblox.Tests;

/// <summary>
/// Pure unit coverage for <see cref="MainViewModel.AccountMatchesFilter"/> — the tag-filter
/// match predicate (v1.6.0). Extracted from the per-row <c>IsFilteredOut</c> wiring so the
/// substring / case / empty-filter rules are testable without standing up the VM or WPF.
/// </summary>
public class AccountMatchesFilterTests
{
    // === Empty / whitespace filter matches everything ===

    [Fact]
    public void EmptyFilter_MatchesEverything()
    {
        Assert.True(MainViewModel.AccountMatchesFilter(["PS99"], "Buildbot", ""));
    }

    [Fact]
    public void WhitespaceFilter_MatchesEverything()
    {
        Assert.True(MainViewModel.AccountMatchesFilter(["PS99"], "Buildbot", "   "));
    }

    [Fact]
    public void EmptyFilter_MatchesEvenWithNoTagsOrName()
    {
        Assert.True(MainViewModel.AccountMatchesFilter([], "", ""));
    }

    // === Match by tag (case-insensitive substring) ===

    [Fact]
    public void MatchesByTag_ExactCase()
    {
        Assert.True(MainViewModel.AccountMatchesFilter(["PS99", "RCU"], "Buildbot", "RCU"));
    }

    [Fact]
    public void MatchesByTag_CaseInsensitive()
    {
        Assert.True(MainViewModel.AccountMatchesFilter(["PS99"], "Buildbot", "ps99"));
    }

    [Fact]
    public void MatchesByTag_Substring()
    {
        Assert.True(MainViewModel.AccountMatchesFilter(["plaza-main"], "Buildbot", "plaza"));
    }

    [Fact]
    public void MatchesByTag_SubstringCaseInsensitive()
    {
        Assert.True(MainViewModel.AccountMatchesFilter(["PLAZA-main"], "Buildbot", "laza"));
    }

    // === Match by name (RenderName, case-insensitive substring) ===

    [Fact]
    public void MatchesByName_ExactCase()
    {
        Assert.True(MainViewModel.AccountMatchesFilter([], "Buildbot", "Buildbot"));
    }

    [Fact]
    public void MatchesByName_CaseInsensitive()
    {
        Assert.True(MainViewModel.AccountMatchesFilter([], "Buildbot", "BUILD"));
    }

    [Fact]
    public void MatchesByName_Substring()
    {
        Assert.True(MainViewModel.AccountMatchesFilter([], "MyMainAccount", "main"));
    }

    [Fact]
    public void MatchesByName_WhenAccountHasNoTags()
    {
        Assert.True(MainViewModel.AccountMatchesFilter([], "Estevan", "este"));
    }

    // === No match hides ===

    [Fact]
    public void NoMatch_HidesWhenNeitherTagNorName()
    {
        Assert.False(MainViewModel.AccountMatchesFilter(["PS99"], "Buildbot", "adoptme"));
    }

    [Fact]
    public void NoMatch_WithNoTags()
    {
        Assert.False(MainViewModel.AccountMatchesFilter([], "Buildbot", "zzz"));
    }

    [Fact]
    public void NoMatch_WhitespaceInsideQueryStillComparesLiterally()
    {
        // A non-blank filter that has interior text is matched literally (only outer trim).
        Assert.False(MainViewModel.AccountMatchesFilter(["PS99"], "Buildbot", "ps 99"));
    }

    // === Trimming: filter is trimmed before comparison ===

    [Fact]
    public void Filter_IsTrimmedBeforeMatch_Tag()
    {
        Assert.True(MainViewModel.AccountMatchesFilter(["PS99"], "Buildbot", "  ps99  "));
    }

    [Fact]
    public void Filter_IsTrimmedBeforeMatch_Name()
    {
        Assert.True(MainViewModel.AccountMatchesFilter([], "Buildbot", "  build  "));
    }

    // === Defensive: null tags treated as no tags ===

    [Fact]
    public void NullTags_MatchByNameStillWorks()
    {
        Assert.True(MainViewModel.AccountMatchesFilter(null!, "Buildbot", "build"));
    }
}
