using ROROROblox.App.Discord;
using ROROROblox.Core;

namespace ROROROblox.Tests.Discord;

public class JoinUriParserTests
{
    [Fact]
    public void TryParse_JoinUri_ExtractsTheTarget()
    {
        var args = new[] { "ROROROblox.App.exe", "roblox-rororo://join/g%7C140403681187145%7Cjob-a" };

        Assert.True(JoinUriParser.TryParse(args, out var target));

        Assert.Equal("job-a", Assert.IsType<LaunchTarget.GameJob>(target).JobId);
    }

    [Fact]
    public void TryParse_NormalStartupArgs_ReturnsFalse()
    {
        Assert.False(JoinUriParser.TryParse(["ROROROblox.App.exe"], out _));
        Assert.False(JoinUriParser.TryParse(["ROROROblox.App.exe", "--tray"], out _));
    }

    [Fact]
    public void TryParse_EmptyArgs_ReturnsFalseInsteadOfThrowing()
    {
        // The registry entry historically shipped without %1, so the app received NO argument
        // where it expected a URI. That regression must not crash startup.
        Assert.False(JoinUriParser.TryParse([], out _));
    }

    [Fact]
    public void TryParse_UriWithGarbagePayload_ReturnsFalse()
    {
        Assert.False(JoinUriParser.TryParse(["exe", "roblox-rororo://join/not-a-secret"], out _));
    }
}
