using System.IO;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

/// <summary>
/// Pure-input tests + golden-fixture coverage. Fixtures live at Discord/Fixtures/*.txt,
/// copied next to the test DLL by the .csproj; they exercise real-shape roblox-player URIs
/// for both the legacy accessCode= private-server format and the newer privateServerLinkCode=
/// link-share format.
/// </summary>
public class ServerShareExtractorTests
{
    private static string LoadFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Discord", "Fixtures", name);
        return File.ReadAllText(path).Trim();
    }

    [Fact]
    public void PrivateAccessCodeFixture_ReturnsDecodedUrl()
    {
        var uri = LoadFixture("launch-uri-private-accesscode.txt");

        var result = ServerShareExtractor.TryExtractPrivateServerUrl(uri);

        Assert.NotNull(result);
        Assert.Contains("accessCode=ABC123-FAKE-CODE", result);
        Assert.StartsWith("https://assetgame.roblox.com/", result);
    }

    [Fact]
    public void PrivateLinkCodeFixture_ReturnsDecodedUrl()
    {
        var uri = LoadFixture("launch-uri-private-linkcode.txt");

        var result = ServerShareExtractor.TryExtractPrivateServerUrl(uri);

        Assert.NotNull(result);
        Assert.Contains("privateServerLinkCode=LINKCODESHARE-FAKE", result);
        Assert.StartsWith("https://www.roblox.com/games/", result);
    }

    [Fact]
    public void PrivateLauncherLinkCodeFixture_ReturnsDecodedUrl()
    {
        // RobloxLauncher.BuildPlaceLauncherUrl emits "linkCode=" (no privateServer prefix)
        // for LaunchTarget.PrivateServer with Kind=LinkCode. This is the URI shape the
        // launcher actually produces — different from the website share-link format.
        // Bug bash 2026-05-06: every LinkCode launch was being missed by the extractor.
        var uri = LoadFixture("launch-uri-private-launcher-linkcode.txt");

        var result = ServerShareExtractor.TryExtractPrivateServerUrl(uri);

        Assert.NotNull(result);
        Assert.Contains("linkCode=LAUNCHER-LINKCODE-FAKE", result);
        Assert.Contains("PlaceLauncher.ashx", result);
    }

    [Fact]
    public void PublicGameFixture_ReturnsNull()
    {
        var uri = LoadFixture("launch-uri-public.txt");

        var result = ServerShareExtractor.TryExtractPrivateServerUrl(uri);

        Assert.Null(result);
    }

    [Fact]
    public void MalformedFixture_ReturnsNull_DoesNotThrow()
    {
        var uri = LoadFixture("launch-uri-malformed.txt");

        var result = ServerShareExtractor.TryExtractPrivateServerUrl(uri);

        Assert.Null(result);
    }

    [Fact]
    public void MissingKeyFixture_ReturnsNull()
    {
        var uri = LoadFixture("launch-uri-missing-key.txt");

        var result = ServerShareExtractor.TryExtractPrivateServerUrl(uri);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullOrWhitespaceInput_ReturnsNull(string? input)
    {
        var result = ServerShareExtractor.TryExtractPrivateServerUrl(input!);

        Assert.Null(result);
    }

    [Fact]
    public void NonRobloxScheme_ReturnsNull_WhenNoPlaceLauncherUrl()
    {
        // Anything without a placelauncherurl segment falls through to null, regardless of scheme.
        var result = ServerShareExtractor.TryExtractPrivateServerUrl("https://example.com/foo?bar=baz");

        Assert.Null(result);
    }

    [Fact]
    public void KeyMatchIsCaseInsensitive()
    {
        // Roblox is consistent today, but we don't want a one-character casing change to break us.
        var uri = "roblox-player:1+launchmode:play+PlaceLauncherURL:https%3A%2F%2Fx%2F%3FaccessCode%3DZ";

        var result = ServerShareExtractor.TryExtractPrivateServerUrl(uri);

        Assert.NotNull(result);
        Assert.Contains("accessCode=Z", result);
    }

    [Fact]
    public void AccessCodeMatchIsCaseInsensitive()
    {
        // Mixed-case accessCode key in the decoded URL still triggers the match.
        var uri = "roblox-player:1+placelauncherurl:https%3A%2F%2Fx%2F%3FAccessCode%3DXYZ";

        var result = ServerShareExtractor.TryExtractPrivateServerUrl(uri);

        Assert.NotNull(result);
    }

    [Fact]
    public void EmptyValueAfterColon_ReturnsNull()
    {
        var uri = "roblox-player:1+placelauncherurl:";

        var result = ServerShareExtractor.TryExtractPrivateServerUrl(uri);

        Assert.Null(result);
    }

    [Fact]
    public void SegmentsWithoutColon_AreSkipped()
    {
        // "gameinfo" has no value; we skip it instead of treating it as a key match.
        var uri = "roblox-player:1+launchmode:play+gameinfo+placelauncherurl:https%3A%2F%2Fx%2F%3FaccessCode%3DA";

        var result = ServerShareExtractor.TryExtractPrivateServerUrl(uri);

        Assert.NotNull(result);
        Assert.Contains("accessCode=A", result);
    }
}
