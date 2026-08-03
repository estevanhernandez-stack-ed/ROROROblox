using ROROROblox.App.Discord;

namespace ROROROblox.Tests.Discord;

/// <summary>
/// Covers the pure command-string helper only. The registry writes (<see cref="JoinUriScheme.Register"/>,
/// <see cref="JoinUriScheme.FixupDiscordSchemeCommand"/>) touch HKCU and must never run under test —
/// they'd mutate the developer's actual registry.
/// </summary>
public class JoinUriSchemeTests
{
    [Fact]
    public void BuildCommandValue_QuotesTheExeAndAppendsTheArgumentPlaceholder()
    {
        var value = JoinUriScheme.BuildCommandValue(@"C:\Program Files\RoRoRo\RoRoRo.exe");

        Assert.Equal("\"C:\\Program Files\\RoRoRo\\RoRoRo.exe\" \"%1\"", value);
    }

    [Fact]
    public void BuildCommandValue_WithoutSpaces_StillIncludesThePlaceholder()
    {
        // Without the "%1" placeholder Windows launches the exe with no argument at all when the
        // scheme is dispatched — this is the exact bug Lachee's own RegisterUriScheme() ships with.
        var value = JoinUriScheme.BuildCommandValue(@"C:\RoRoRo.exe");

        Assert.Contains("\"%1\"", value);
        Assert.EndsWith("\"%1\"", value);
    }
}
