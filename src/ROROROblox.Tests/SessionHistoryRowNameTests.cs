using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// F-072. Each test here is a fragment that used to be announced on its own, with nothing tying it
/// to the four beside it.
/// </summary>
public class SessionHistoryRowNameTests
{
    private static string Row(
        string name = "estehernandez",
        string? game = "Pet Sim",
        bool priv = false,
        string started = "4:57 PM",
        string duration = "1 min",
        string? outcome = null,
        bool saved = false)
        => SessionHistoryRowName.Compose(name, game, priv, started, duration, outcome, saved);

    [Fact]
    public void OneRowIsOneSentenceInTheOrderItReads()
    {
        Assert.Equal("estehernandez, Pet Sim, started 4:57 PM, 1 min.", Row());
    }

    [Fact]
    public void ThePrivateBadgeIsSpokenBecauseAGlyphIsNot()
    {
        // The row shows a PRIVATE pill. A pill is silent, and "which server did I join" is exactly
        // the question a history list exists to answer.
        Assert.Contains("private server", Row(priv: true), StringComparison.Ordinal);
        Assert.DoesNotContain("private server", Row(priv: false), StringComparison.Ordinal);
    }

    [Fact]
    public void SavedIsSaidOnlyWhenItIsTrue()
    {
        // "Not saved" on every unbookmarked row is noise, and the row itself says nothing in that
        // case either — it shows a "+ Bookmark" button, which announces itself.
        Assert.EndsWith("saved.", Row(saved: true), StringComparison.Ordinal);
        Assert.DoesNotContain("saved", Row(saved: false), StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingGameNameReadsAsTheRowDisplaysIt()
    {
        foreach (var missing in new[] { null, "", "   " })
        {
            Assert.Contains("(unknown game)", Row(game: missing), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheOutcomeHintRidesAlongWhenThereIsOne()
    {
        Assert.Contains("closed early", Row(outcome: "closed early"), StringComparison.Ordinal);
        Assert.Equal("estehernandez, Pet Sim, started 4:57 PM, 1 min.", Row(outcome: null));
    }

    [Fact]
    public void AnEmptyAccountNameStillNamesSomething()
    {
        // The store can hold a session whose display name never resolved. "Unknown account" is a
        // worse row than a real name and a better one than a sentence that opens with a comma.
        Assert.StartsWith("Unknown account,", Row(name: "  "), StringComparison.Ordinal);
    }

    [Fact]
    public void TheNameSaysWhatTheRowSaysAndNoMore()
    {
        // Duration text is passed in rather than recomputed, so the announcement cannot drift from
        // the pixels — "still running" is a real value the row renders and must survive verbatim.
        Assert.Contains("still running", Row(duration: "still running"), StringComparison.Ordinal);
    }
}
