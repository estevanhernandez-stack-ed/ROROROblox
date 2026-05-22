using ROROROblox.Core;

namespace ROROROblox.Tests;

public class SavedPrivateServerShareUrlTests
{
    private static SavedPrivateServer Make(string code, PrivateServerCodeKind kind) => new(
        Guid.NewGuid(), 920587237, code, kind, "name", "Place", "", DateTimeOffset.UtcNow, null);

    [Fact]
    public void LinkCode_BuildsPrivateServerLinkCodeUrl()
    {
        var url = Make("ABC-LINK", PrivateServerCodeKind.LinkCode).ToShareUrl();
        Assert.Contains("920587237", url);
        Assert.Contains("privateServerLinkCode=ABC-LINK", url);
        // Round-trips back to a PrivateServer LinkCode target.
        var parsed = LaunchTarget.FromUrl(url);
        var ps = Assert.IsType<LaunchTarget.PrivateServer>(parsed);
        Assert.Equal(PrivateServerCodeKind.LinkCode, ps.Kind);
        Assert.Equal("ABC-LINK", ps.Code);
    }

    [Fact]
    public void AccessCode_RoundTripsToAccessCodeTarget()
    {
        var url = Make("XYZ-ACCESS", PrivateServerCodeKind.AccessCode).ToShareUrl();
        var parsed = LaunchTarget.FromUrl(url);
        var ps = Assert.IsType<LaunchTarget.PrivateServer>(parsed);
        Assert.Equal(PrivateServerCodeKind.AccessCode, ps.Kind);
        Assert.Equal("XYZ-ACCESS", ps.Code);
    }
}
