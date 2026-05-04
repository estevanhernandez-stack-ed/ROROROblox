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
    public void FromUrl_PrivateServerShareUrl_ReturnsPrivateServer()
    {
        var target = LaunchTarget.FromUrl(
            "https://www.roblox.com/games/920587237/Adopt-Me?privateServerLinkCode=ABC-123_xyz");

        var ps = Assert.IsType<LaunchTarget.PrivateServer>(target);
        Assert.Equal(920587237L, ps.PlaceId);
        Assert.Equal("ABC-123_xyz", ps.AccessCode);
    }

    [Fact]
    public void FromUrl_PrivateServerLinkCodeAlias_LinkCode_AlsoWorks()
    {
        var target = LaunchTarget.FromUrl(
            "https://www.roblox.com/games/100/Foo?linkCode=KKK");

        var ps = Assert.IsType<LaunchTarget.PrivateServer>(target);
        Assert.Equal("KKK", ps.AccessCode);
    }

    [Fact]
    public void FromUrl_PlaceLauncherWithAccessCode_ReturnsPrivateServer()
    {
        var target = LaunchTarget.FromUrl(
            "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestPrivateGame&placeId=42&accessCode=secret");

        var ps = Assert.IsType<LaunchTarget.PrivateServer>(target);
        Assert.Equal(42L, ps.PlaceId);
        Assert.Equal("secret", ps.AccessCode);
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
    public void BuildPlaceLauncherUrl_PrivateServer_ProducesRequestPrivateGameWithAccessCode()
    {
        var url = RobloxLauncher.BuildPlaceLauncherUrl(
            new LaunchTarget.PrivateServer(12345, "ABC_xyz-1"),
            browserTrackerId: "BT-1");

        Assert.Contains("request=RequestPrivateGame", url);
        Assert.Contains("placeId=12345", url);
        Assert.Contains("accessCode=ABC_xyz-1", url);
        Assert.Contains("linkCode=ABC_xyz-1", url);
    }

    [Fact]
    public void BuildPlaceLauncherUrl_PrivateServer_UrlEncodesAccessCode()
    {
        var url = RobloxLauncher.BuildPlaceLauncherUrl(
            new LaunchTarget.PrivateServer(12345, "code with spaces&special=chars"),
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
    public void BuildPlaceLauncherUrl_RejectsEmptyAccessCode()
    {
        Assert.Throws<ArgumentException>(() =>
            RobloxLauncher.BuildPlaceLauncherUrl(new LaunchTarget.PrivateServer(1, ""), "BT"));
    }

    [Fact]
    public void BuildPlaceLauncherUrl_RejectsNullTarget()
    {
        Assert.Throws<ArgumentNullException>(() =>
            RobloxLauncher.BuildPlaceLauncherUrl(null!, "BT"));
    }
}
