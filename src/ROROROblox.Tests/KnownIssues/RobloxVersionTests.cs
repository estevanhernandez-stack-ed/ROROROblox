using ROROROblox.Core.KnownIssues;

namespace ROROROblox.Tests.KnownIssues;

/// <summary>
/// Two spellings of a Roblox version reach this feature. The feed author writes <c>0.740</c>; the
/// binary reports <c>0, 740, 0, 7400927</c> — read off every version folder on the owner's PC on
/// 2026-09-23 — which <see cref="Version.TryParse(string?, out Version?)"/> rejects outright.
/// </summary>
public class RobloxVersionTests
{
    [Theory]
    [InlineData("0.740", "0.740")]
    [InlineData("0.740.0", "0.740.0")]
    [InlineData("0.740.0.7400927", "0.740.0.7400927")]
    [InlineData(" 0.739 ", "0.739")]
    public void AFeedVersionWrittenTheWayRobloxWritesItParses(string text, string expected)
    {
        Assert.True(RobloxVersion.TryParseFeed(text, out var version));
        Assert.Equal(Version.Parse(expected), version);
    }

    [Theory]
    [InlineData("0.74")]        // reads as minor 74, a different number from 0.740
    [InlineData("0.7400")]
    [InlineData("1.2.3.4.5")]
    [InlineData("0.740.x")]
    [InlineData("")]
    [InlineData(null)]
    public void AFeedVersionInAnyOtherShapeIsRefused(string? text)
    {
        Assert.False(RobloxVersion.TryParseFeed(text, out _));
    }

    [Theory]
    [InlineData("0, 740, 0, 7400927", "0.740.0.7400927")]
    [InlineData("0,739,0,7390687", "0.739.0.7390687")]
    [InlineData("0.740.0.7400927", "0.740.0.7400927")]
    public void AnInstalledFileVersionParsesInEitherSpelling(string text, string expected)
    {
        Assert.True(RobloxVersion.TryParseInstalled(text, out var version));
        Assert.Equal(Version.Parse(expected), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not a version")]
    public void AnUnreadableInstalledVersionIsRefused(string? text)
    {
        Assert.False(RobloxVersion.TryParseInstalled(text, out _));
    }

    [Fact]
    public void RobloxsFourPartFormComparesCorrectlyAgainstATwoPartBound()
    {
        Assert.True(RobloxVersion.TryParseInstalled("0, 740, 0, 7400927", out var installed));
        Assert.True(RobloxVersion.TryParseFeed("0.740", out var fixedIn));
        Assert.True(installed >= fixedIn);

        Assert.True(RobloxVersion.TryParseInstalled("0, 739, 0, 7390687", out var older));
        Assert.True(older < fixedIn);
    }
}
