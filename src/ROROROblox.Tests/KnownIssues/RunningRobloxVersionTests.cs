using ROROROblox.Core.KnownIssues;

namespace ROROROblox.Tests.KnownIssues;

/// <summary>
/// Review Focus 2. The handler read answers "what will a launch run"; the installed read orders
/// folders by write time, which F-104 measured as a coin flip during a launch batch. Handler first.
/// </summary>
public class RunningRobloxVersionTests
{
    [Fact]
    public void TheHandlerVersionWins()
    {
        var version = RunningRobloxVersion.Read(() => "0, 740, 0, 7400927", () => "0, 739, 0, 7390687");

        Assert.Equal(Version.Parse("0.740.0.7400927"), version);
    }

    [Fact]
    public void AStrapOwnedOrMissingHandlerFallsBackToTheInstalledVersion()
    {
        var version = RunningRobloxVersion.Read(() => null, () => "0, 739, 0, 7390687");

        Assert.Equal(Version.Parse("0.739.0.7390687"), version);
    }

    [Fact]
    public void AnUnreadableHandlerFallsBackToo()
    {
        var version = RunningRobloxVersion.Read(() => "garbage", () => "0, 739, 0, 7390687");

        Assert.Equal(Version.Parse("0.739.0.7390687"), version);
    }

    [Fact]
    public void AThrowingReadIsTreatedAsUnknown()
    {
        var version = RunningRobloxVersion.Read(() => throw new IOException("locked"), () => null);

        Assert.Null(version);
    }

    [Fact]
    public void NoRobloxAtAllIsUnknown()
    {
        Assert.Null(RunningRobloxVersion.Read(() => null, () => null));
    }
}
