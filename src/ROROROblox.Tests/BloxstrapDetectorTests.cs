using ROROROblox.Core;

namespace ROROROblox.Tests;

public sealed class BloxstrapDetectorTests
{
    // Synthetic paths — the matcher only cares about the "Bloxstrap" substring, not the
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
}
