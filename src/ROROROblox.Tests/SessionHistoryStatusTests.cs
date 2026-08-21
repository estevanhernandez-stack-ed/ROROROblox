using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// F-038. The point of every one of these is that two different facts must not produce the same
/// sentence — the shipped window produced the same sentence for "you have no history" and "your
/// history could not be read", which is the more alarming of the two stated as the more reassuring.
/// </summary>
public class SessionHistoryStatusTests
{
    [Fact]
    public void AnUnreadableHistoryNeverReadsAsAnEmptyOne()
    {
        var empty = SessionHistoryStatus.Placeholder(SessionHistoryOutcome.Empty);
        var broken = SessionHistoryStatus.Placeholder(SessionHistoryOutcome.Unreadable);

        Assert.NotEqual(empty.Headline, broken.Headline);
        Assert.NotEqual(empty.Detail, broken.Detail);

        // And the broken one must not tell the user to go and make some history — the file may be
        // full of it.
        Assert.DoesNotContain("Launch As", broken.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheStatusLineSeparatesAllThreeOutcomes()
    {
        var lines = new[]
        {
            SessionHistoryStatus.StatusLine(SessionHistoryOutcome.Loaded, 3, null),
            SessionHistoryStatus.StatusLine(SessionHistoryOutcome.Empty, 0, null),
            SessionHistoryStatus.StatusLine(SessionHistoryOutcome.Unreadable, 0, "in use by another process"),
        };

        Assert.Equal(3, lines.Distinct().Count());
    }

    [Fact]
    public void TheFailureLineCarriesTheReason()
    {
        var line = SessionHistoryStatus.StatusLine(SessionHistoryOutcome.Unreadable, 0, "The process cannot access the file");

        Assert.Contains("The process cannot access the file", line, StringComparison.Ordinal);
    }

    [Fact]
    public void AReasonlessFailureStillReadsAsASentence()
    {
        // Exception.Message is not guaranteed to be useful, or present. "Couldn't read history: ."
        // is worse than saying nothing extra.
        foreach (var blank in new[] { null, "", "   " })
        {
            var line = SessionHistoryStatus.StatusLine(SessionHistoryOutcome.Unreadable, 0, blank);

            Assert.Equal("Couldn't read history.", line);
        }
    }

    [Fact]
    public void OneLaunchIsNotOneLaunches()
    {
        Assert.Equal("1 launch recorded.", SessionHistoryStatus.StatusLine(SessionHistoryOutcome.Loaded, 1, null));
        Assert.Equal("2 launches recorded.", SessionHistoryStatus.StatusLine(SessionHistoryOutcome.Loaded, 2, null));
    }

    [Fact]
    public void AFailedClearSaysNothingWasDeleted()
    {
        // The reason this line exists at all: a Clear that silently did nothing looked exactly like
        // a Clear that worked, and the user's next move — closing the window satisfied — is the
        // same either way.
        var line = SessionHistoryStatus.ClearFailed("access denied");

        Assert.Contains("access denied", line, StringComparison.Ordinal);
        Assert.Contains("Nothing was deleted", line, StringComparison.Ordinal);
        Assert.NotEqual(SessionHistoryStatus.Cleared, line);
    }
}
