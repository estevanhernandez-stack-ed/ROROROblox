using ROROROblox.Core;

namespace ROROROblox.Tests;

public class LaunchTargetTests
{
    // === FromUrl parsing ===

    [Fact]
    public void FromUrl_PublicGameUrl_ReturnsPlace()
    {
        var target = LaunchTarget.FromUrl("https://www.roblox.com/games/920587237/Adopt-Me");

        var place = Assert.IsType<LaunchTarget.Place>(target);
        Assert.Equal(920587237L, place.PlaceId);
    }

    [Fact]
    public void FromUrl_PublicGameUrlWithoutSlug_ReturnsPlace()
    {
        var target = LaunchTarget.FromUrl("https://www.roblox.com/games/920587237");

        Assert.Equal(new LaunchTarget.Place(920587237), target);
    }

    [Fact]
    public void FromUrl_BareNumeric_ReturnsPlace()
    {
        var target = LaunchTarget.FromUrl("920587237");

        Assert.Equal(new LaunchTarget.Place(920587237), target);
    }

    [Fact]
    public void FromUrl_PrivateServerShareUrl_ReturnsPrivateServerWithLinkCodeKind()
    {
        var target = LaunchTarget.FromUrl(
            "https://www.roblox.com/games/920587237/Adopt-Me?privateServerLinkCode=ABC-123_xyz");

        var ps = Assert.IsType<LaunchTarget.PrivateServer>(target);
        Assert.Equal(920587237L, ps.PlaceId);
        Assert.Equal("ABC-123_xyz", ps.Code);
        Assert.Equal(PrivateServerCodeKind.LinkCode, ps.Kind);
    }

    [Fact]
    public void FromUrl_LinkCodeAlias_AlsoTaggedAsLinkCode()
    {
        var target = LaunchTarget.FromUrl(
            "https://www.roblox.com/games/100/Foo?linkCode=KKK");

        var ps = Assert.IsType<LaunchTarget.PrivateServer>(target);
        Assert.Equal("KKK", ps.Code);
        Assert.Equal(PrivateServerCodeKind.LinkCode, ps.Kind);
    }

    [Fact]
    public void FromUrl_PlaceLauncherWithAccessCode_TaggedAsAccessCode()
    {
        var target = LaunchTarget.FromUrl(
            "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestPrivateGame&placeId=42&accessCode=secret");

        var ps = Assert.IsType<LaunchTarget.PrivateServer>(target);
        Assert.Equal(42L, ps.PlaceId);
        Assert.Equal("secret", ps.Code);
        Assert.Equal(PrivateServerCodeKind.AccessCode, ps.Kind);
    }

    // === TryParseShareLink (the newer roblox.com/share?code=X&type=Y form) ===

    [Fact]
    public void TryParseShareLink_ServerType_ExtractsCodeAndType()
    {
        var ok = LaunchTarget.TryParseShareLink(
            "https://www.roblox.com/share?code=a5ad1dae7cb0bd47bd7f665614d5a3e6&type=Server",
            out var code, out var linkType);

        Assert.True(ok);
        Assert.Equal("a5ad1dae7cb0bd47bd7f665614d5a3e6", code);
        Assert.Equal("Server", linkType);
    }

    [Fact]
    public void TryParseShareLink_TypeReversedQueryOrder_StillExtracted()
    {
        var ok = LaunchTarget.TryParseShareLink(
            "https://www.roblox.com/share?type=Server&code=ABC",
            out var code, out var linkType);

        Assert.True(ok);
        Assert.Equal("ABC", code);
        Assert.Equal("Server", linkType);
    }

    [Fact]
    public void TryParseShareLink_MissingType_DefaultsToServer()
    {
        var ok = LaunchTarget.TryParseShareLink(
            "https://www.roblox.com/share?code=ABC",
            out var code, out var linkType);

        Assert.True(ok);
        Assert.Equal("ABC", code);
        Assert.Equal("Server", linkType);
    }

    [Fact]
    public void TryParseShareLink_NotAShareUrl_ReturnsFalse()
    {
        Assert.False(LaunchTarget.TryParseShareLink(
            "https://www.roblox.com/games/12345/Foo?privateServerLinkCode=X",
            out _, out _));

        Assert.False(LaunchTarget.TryParseShareLink("https://example.com/share?code=X&type=Server", out _, out _));
        Assert.False(LaunchTarget.TryParseShareLink("", out _, out _));
        Assert.False(LaunchTarget.TryParseShareLink(null, out _, out _));
    }

    [Fact]
    public void TryParseShareLink_MissingCode_ReturnsFalse()
    {
        Assert.False(LaunchTarget.TryParseShareLink(
            "https://www.roblox.com/share?type=Server",
            out _, out _));
    }

    [Fact]
    public void FromUrl_RobloxComShareUrl_ReturnsNull_RequiresAsyncResolution()
    {
        // The /share?code=X token URL needs an authenticated API call to resolve to a real
        // (placeId, linkCode) pair. Sync FromUrl returns null; callers route to the async
        // resolver in MainViewModel.
        Assert.Null(LaunchTarget.FromUrl(
            "https://www.roblox.com/share?code=ABC&type=Server"));
    }

    [Fact]
    public void FromUrl_LinkCodeBeatsAccessCode_WhenBothPresent()
    {
        // Defensive: a URL containing both params (rare but possible from messy paste) prefers
        // linkCode since that's what the share-link path needs and is the stricter
        // server-side-resolved path. Either way Roblox will validate.
        var target = LaunchTarget.FromUrl(
            "https://www.roblox.com/games/42/Foo?privateServerLinkCode=LINK&accessCode=ACCESS");

        var ps = Assert.IsType<LaunchTarget.PrivateServer>(target);
        Assert.Equal("LINK", ps.Code);
        Assert.Equal(PrivateServerCodeKind.LinkCode, ps.Kind);
    }

    [Fact]
    public void FromUrl_PlaceLauncherWithoutAccessCode_ReturnsPlace()
    {
        var target = LaunchTarget.FromUrl(
            "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame&placeId=42");

        Assert.Equal(new LaunchTarget.Place(42), target);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("https://example.com/anything")]
    [InlineData("https://www.roblox.com/users/12345/profile")]
    public void FromUrl_UnparseableInput_ReturnsNull(string? input)
    {
        Assert.Null(LaunchTarget.FromUrl(input));
    }

    [Fact]
    public void FromUrl_ZeroPlaceId_ReturnsNull()
    {
        Assert.Null(LaunchTarget.FromUrl("0"));
        Assert.Null(LaunchTarget.FromUrl("https://www.roblox.com/games/0"));
    }

    // === BuildPlaceLauncherUrl ===

    [Fact]
    public void BuildPlaceLauncherUrl_Place_ProducesRequestGameForm()
    {
        var url = RobloxLauncher.BuildPlaceLauncherUrl(
            new LaunchTarget.Place(12345),
            browserTrackerId: "BT-1");

        Assert.Contains("PlaceLauncher.ashx", url);
        Assert.Contains("request=RequestGame", url);
        Assert.Contains("browserTrackerId=BT-1", url);
        Assert.Contains("placeId=12345", url);
        Assert.Contains("isPlayTogetherGame=false", url);
    }

    [Fact]
    public void BuildPlaceLauncherUrl_PrivateServer_LinkCodeKind_EmitsOnlyLinkCodeSlot()
    {
        var url = RobloxLauncher.BuildPlaceLauncherUrl(
            new LaunchTarget.PrivateServer(12345, "SHARE_TOKEN", PrivateServerCodeKind.LinkCode),
            browserTrackerId: "BT-1");

        Assert.Contains("request=RequestPrivateGame", url);
        Assert.Contains("placeId=12345", url);
        Assert.Contains("linkCode=SHARE_TOKEN", url);
        // Critical: don't ALSO emit accessCode= when the value is a link code — Roblox returns
        // permission-denied if a linkCode value is sent in the accessCode slot.
        Assert.DoesNotContain("accessCode=", url);
    }

    [Fact]
    public void BuildPlaceLauncherUrl_PrivateServer_AccessCodeKind_EmitsOnlyAccessCodeSlot()
    {
        var url = RobloxLauncher.BuildPlaceLauncherUrl(
            new LaunchTarget.PrivateServer(12345, "RAW_ACCESS", PrivateServerCodeKind.AccessCode),
            browserTrackerId: "BT-1");

        Assert.Contains("request=RequestPrivateGame", url);
        Assert.Contains("placeId=12345", url);
        Assert.Contains("accessCode=RAW_ACCESS", url);
        Assert.DoesNotContain("linkCode=", url);
    }

    [Fact]
    public void BuildPlaceLauncherUrl_PrivateServer_UrlEncodesCode()
    {
        var url = RobloxLauncher.BuildPlaceLauncherUrl(
            new LaunchTarget.PrivateServer(12345, "code with spaces&special=chars", PrivateServerCodeKind.LinkCode),
            browserTrackerId: "1");

        Assert.Contains(Uri.EscapeDataString("code with spaces&special=chars"), url);
    }

    [Fact]
    public void BuildPlaceLauncherUrl_FollowFriend_ProducesRequestFollowUserForm()
    {
        var url = RobloxLauncher.BuildPlaceLauncherUrl(
            new LaunchTarget.FollowFriend(98765),
            browserTrackerId: "BT-1");

        Assert.Contains("request=RequestFollowUser", url);
        Assert.Contains("userId=98765", url);
        // FollowFriend doesn't carry placeId — Roblox follows the user wherever they are.
        Assert.DoesNotContain("placeId=", url);
    }

    [Fact]
    public void BuildPlaceLauncherUrl_DefaultGame_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            RobloxLauncher.BuildPlaceLauncherUrl(new LaunchTarget.DefaultGame(), "BT"));
    }

    [Fact]
    public void BuildPlaceLauncherUrl_RejectsEmptyTrackerId()
    {
        Assert.Throws<ArgumentException>(() =>
            RobloxLauncher.BuildPlaceLauncherUrl(new LaunchTarget.Place(1), ""));
    }

    [Fact]
    public void BuildPlaceLauncherUrl_RejectsZeroPlaceId()
    {
        Assert.Throws<ArgumentException>(() =>
            RobloxLauncher.BuildPlaceLauncherUrl(new LaunchTarget.Place(0), "BT"));
    }

    [Fact]
    public void BuildPlaceLauncherUrl_RejectsZeroFollowUserId()
    {
        Assert.Throws<ArgumentException>(() =>
            RobloxLauncher.BuildPlaceLauncherUrl(new LaunchTarget.FollowFriend(0), "BT"));
    }

    [Fact]
    public void BuildPlaceLauncherUrl_RejectsEmptyCode()
    {
        Assert.Throws<ArgumentException>(() =>
            RobloxLauncher.BuildPlaceLauncherUrl(
                new LaunchTarget.PrivateServer(1, "", PrivateServerCodeKind.LinkCode), "BT"));
    }

    [Fact]
    public void BuildPlaceLauncherUrl_RejectsNullTarget()
    {
        Assert.Throws<ArgumentNullException>(() =>
            RobloxLauncher.BuildPlaceLauncherUrl(null!, "BT"));
    }
}
