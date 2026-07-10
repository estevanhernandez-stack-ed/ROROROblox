using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// The windowless-only gate is the safety story for silently closing Roblox at startup. A single
/// windowed client — possibly mid-game — must always force the confirming modal instead.
/// </summary>
public class SeamlessTakeoverTests
{
    [Fact]
    public void WindowlessOnly_AllWindowless_True()
        => Assert.True(SeamlessTakeover.WindowlessOnly(new[]
        {
            new RobloxProcessInfo(1, HasWindow: false),
            new RobloxProcessInfo(2, HasWindow: false),
        }));

    [Fact]
    public void WindowlessOnly_AnyWindowed_False()
        => Assert.False(SeamlessTakeover.WindowlessOnly(new[]
        {
            new RobloxProcessInfo(1, HasWindow: false),
            new RobloxProcessInfo(2, HasWindow: true), // in-game — never silently close
        }));

    [Fact]
    public void WindowlessOnly_SingleWindowed_False()
        => Assert.False(SeamlessTakeover.WindowlessOnly(new[] { new RobloxProcessInfo(1, HasWindow: true) }));

    [Fact]
    public void WindowlessOnly_Empty_False()
        => Assert.False(SeamlessTakeover.WindowlessOnly(System.Array.Empty<RobloxProcessInfo>()));
}
