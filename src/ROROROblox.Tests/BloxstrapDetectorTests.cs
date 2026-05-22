using ROROROblox.Core;

namespace ROROROblox.Tests;

public sealed class BloxstrapDetectorTests
{
    // Synthetic paths — the matcher only cares about the strap substring, not the
    // realistic Windows install location. Using D:\ avoids the local-path-guard pre-commit hook.
    [Theory]
    [InlineData(@"D:\AppData\Bloxstrap\Bloxstrap.exe", true)]
    [InlineData(@"D:\AppData\BLOXSTRAP\Bloxstrap.exe", true)]
    [InlineData(@"""D:\AppData\Bloxstrap\BloxstrapBootstrapper.exe"" %1", true)]
    [InlineData(@"D:\Program Files\Roblox\Versions\version-abc\RobloxPlayerBeta.exe %1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsBloxstrap_RecognizesBloxstrapPathInCommandString(string? command, bool expected)
    {
        Assert.Equal(expected, BloxstrapDetector.LooksLikeBloxstrap(command));
    }

    // LooksLikeStrap is the unified matcher: true when EITHER Bloxstrap or Fishstrap is the
    // handler. Vanilla Roblox launchers must read false.
    [Theory]
    [InlineData(@"D:\AppData\Bloxstrap\Bloxstrap.exe", true)]
    [InlineData(@"D:\AppData\BLOXSTRAP\BloxstrapBootstrapper.exe %1", true)]
    [InlineData(@"D:\AppData\Fishstrap\Fishstrap.exe", true)]
    [InlineData(@"D:\AppData\FISHSTRAP\FishstrapBootstrapper.exe %1", true)]
    [InlineData(@"""D:\AppData\Fishstrap\Fishstrap.exe"" %1", true)]
    [InlineData(@"D:\Program Files\Roblox\Versions\version-abc\RobloxPlayerBeta.exe %1", false)]
    [InlineData(@"D:\Program Files\Roblox\Versions\version-abc\RobloxPlayerLauncher.exe %1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikeStrap_RecognizesEitherStrap(string? command, bool expected)
    {
        Assert.Equal(expected, BloxstrapDetector.LooksLikeStrap(command));
    }

    [Fact]
    public void LooksLikeFishstrap_TrueForFishstrap_FalseForBloxstrap()
    {
        Assert.True(BloxstrapDetector.LooksLikeFishstrap(@"D:\AppData\Fishstrap\Fishstrap.exe"));
        Assert.False(BloxstrapDetector.LooksLikeFishstrap(@"D:\AppData\Bloxstrap\Bloxstrap.exe"));
        Assert.False(BloxstrapDetector.LooksLikeFishstrap(null));
    }

    // IsStrapHandlingLaunches() reads the handler command via an injectable seam, so tests
    // never touch the real registry.
    [Fact]
    public void IsStrapHandlingLaunches_TrueWhenBloxstrapIsHandler()
    {
        var detector = new BloxstrapDetector(() => @"D:\AppData\Bloxstrap\Bloxstrap.exe %1");
        Assert.True(detector.IsStrapHandlingLaunches());
    }

    [Fact]
    public void IsStrapHandlingLaunches_TrueWhenFishstrapIsHandler()
    {
        var detector = new BloxstrapDetector(() => @"D:\AppData\Fishstrap\Fishstrap.exe %1");
        Assert.True(detector.IsStrapHandlingLaunches());
    }

    [Fact]
    public void IsStrapHandlingLaunches_FalseForVanillaRobloxPlayerBeta()
    {
        var detector = new BloxstrapDetector(
            () => @"D:\Program Files\Roblox\Versions\version-abc\RobloxPlayerBeta.exe %1");
        Assert.False(detector.IsStrapHandlingLaunches());
    }

    [Fact]
    public void IsStrapHandlingLaunches_FalseForVanillaRobloxPlayerLauncher()
    {
        var detector = new BloxstrapDetector(
            () => @"D:\Program Files\Roblox\RobloxPlayerLauncher.exe %1");
        Assert.False(detector.IsStrapHandlingLaunches());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsStrapHandlingLaunches_FalseWhenNoOrEmptyHandler(string? command)
    {
        var detector = new BloxstrapDetector(() => command);
        Assert.False(detector.IsStrapHandlingLaunches());
    }

    [Fact]
    public void IsStrapHandlingLaunches_FalseAndDoesNotThrowWhenHandlerReadFails()
    {
        // Unreadable registry (sandboxed runner, locked-down PC) surfaces as a throwing reader.
        // Detector must degrade safe, not propagate.
        var detector = new BloxstrapDetector(() => throw new InvalidOperationException("registry locked"));
        Assert.False(detector.IsStrapHandlingLaunches());
    }

    // IsBloxstrapHandler() (the legacy banner caller) also routes through the seam, so a
    // Bloxstrap-only handler still reads true and a Fishstrap handler does NOT (banner is
    // Bloxstrap-FFlag-override specific).
    [Fact]
    public void IsBloxstrapHandler_TrueForBloxstrap_FalseForFishstrap()
    {
        Assert.True(new BloxstrapDetector(() => @"D:\AppData\Bloxstrap\Bloxstrap.exe %1").IsBloxstrapHandler());
        Assert.False(new BloxstrapDetector(() => @"D:\AppData\Fishstrap\Fishstrap.exe %1").IsBloxstrapHandler());
    }

    [Fact]
    public void IsBloxstrapHandler_FalseAndDoesNotThrowWhenHandlerReadFails()
    {
        var detector = new BloxstrapDetector(() => throw new InvalidOperationException("registry locked"));
        Assert.False(detector.IsBloxstrapHandler());
    }
}
