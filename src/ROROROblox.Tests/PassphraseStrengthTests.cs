using ROROROblox.App.Transport;

namespace ROROROblox.Tests;

/// <summary>
/// Tests for the pure passphrase-strength helper (v1.6.0 — spec §1 "Export flow"). The export bundle
/// protects <c>.ROBLOSECURITY</c> cookies; this gate is the only thing between a leaked file and an
/// offline brute-force, so the floor (≥12 chars) is enforced, not advisory. These lock that floor and
/// the meter's monotonicity (longer + more varied reads stronger).
/// </summary>
public class PassphraseStrengthTests
{
    // ---------- IsAcceptable: the enforced ≥12-char floor ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]              // whitespace-only — no real content
    [InlineData("short")]           // 5
    [InlineData("elevenchars")]     // 11 — one below the floor
    [InlineData("12345678901")]     // 11 digits
    public void IsAcceptable_RejectsBelowFloorAndEmpty(string? passphrase)
    {
        Assert.False(PassphraseStrength.IsAcceptable(passphrase));
    }

    [Theory]
    [InlineData("twelvecharss")]            // exactly 12
    [InlineData("correct horse battery")]   // long passphrase with spaces but real content
    [InlineData("aaaaaaaaaaaa")]            // 12 chars, weak content — still clears the FLOOR
    [InlineData("MyP@ssw0rd123!")]          // long + varied
    public void IsAcceptable_AcceptsAtOrAboveFloor(string passphrase)
    {
        Assert.True(PassphraseStrength.IsAcceptable(passphrase));
    }

    [Fact]
    public void IsAcceptable_FloorIsTwelve()
    {
        Assert.Equal(12, PassphraseStrength.MinimumLength);
        Assert.False(PassphraseStrength.IsAcceptable(new string('a', 11)));
        Assert.True(PassphraseStrength.IsAcceptable(new string('a', 12)));
    }

    [Fact]
    public void IsAcceptable_RejectsTwelveSpaces()
    {
        // A 12-space passphrase is length-12 but has no non-whitespace content — rejected.
        Assert.False(PassphraseStrength.IsAcceptable(new string(' ', 12)));
    }

    // ---------- Evaluate: score + label ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Evaluate_EmptyOrWhitespace_ScoresZero(string? passphrase)
    {
        var (score, label) = PassphraseStrength.Evaluate(passphrase);
        Assert.Equal(0, score);
        Assert.Equal("Too short", label);
    }

    [Fact]
    public void Evaluate_BelowFloor_NeverScoresAboveOne()
    {
        // No matter how varied, a too-short passphrase must not read "good."
        var (score, _) = PassphraseStrength.Evaluate("Ab1!Ab1!");  // 8 chars, 4 classes
        Assert.True(score <= 1, $"expected <=1, got {score}");
    }

    [Fact]
    public void Evaluate_ScoreIncreasesWithLength()
    {
        // Same character (no variety bump) — score should rise as length grows past the floor.
        int s12 = PassphraseStrength.Evaluate(new string('a', 12)).Score;
        int s16 = PassphraseStrength.Evaluate(new string('a', 16)).Score;
        int s24 = PassphraseStrength.Evaluate(new string('a', 24)).Score;

        Assert.True(s16 > s12, $"len16 ({s16}) should beat len12 ({s12})");
        Assert.True(s24 >= s16, $"len24 ({s24}) should be >= len16 ({s16})");
    }

    [Fact]
    public void Evaluate_ScoreIncreasesWithVariety()
    {
        // Same length (12), more character classes → higher score.
        int plain = PassphraseStrength.Evaluate(new string('a', 12)).Score;          // 1 class
        int varied = PassphraseStrength.Evaluate("aB3!aB3!aB3!").Score;              // 4 classes, 12 chars

        Assert.True(varied > plain, $"varied ({varied}) should beat plain ({plain})");
    }

    [Fact]
    public void Evaluate_StrongPassphrase_ReadsHigh()
    {
        // Long + 4 character classes should land at the top of the meter.
        var (score, label) = PassphraseStrength.Evaluate("Tr0ub4dour&3xtra-Long-Pass!");
        Assert.True(score >= 3, $"expected strong (>=3), got {score}");
        Assert.Contains(label, new[] { "Strong", "Very strong" });
    }

    [Fact]
    public void Evaluate_ScoreNeverExceedsFour()
    {
        var (score, _) = PassphraseStrength.Evaluate(new string('Z', 64) + "aB3!" + "----");
        Assert.InRange(score, 0, 4);
    }

    [Fact]
    public void Evaluate_AcceptableImpliesNonZeroScore()
    {
        // Anything that clears the floor should show at least "Weak" on the meter — the meter must
        // not contradict the gate.
        foreach (var p in new[] { new string('a', 12), "twelvecharss", "MyP@ssw0rd123!" })
        {
            Assert.True(PassphraseStrength.IsAcceptable(p));
            Assert.True(PassphraseStrength.Evaluate(p).Score >= 1);
        }
    }
}
