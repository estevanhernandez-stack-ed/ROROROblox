using ROROROblox.App.Plugins;

namespace ROROROblox.Tests;

public class PluginCapabilityTests
{
    [Fact]
    public void IsKnown_ReturnsTrue_ForDefinedCapability()
    {
        Assert.True(PluginCapability.IsKnown("host.events.account-launched"));
        Assert.True(PluginCapability.IsKnown("host.ui.tray-menu"));
        Assert.True(PluginCapability.IsKnown("system.synthesize-keyboard-input"));
        Assert.True(PluginCapability.IsKnown("system.read-screen"));
    }

    [Fact]
    public void IsKnown_ReturnsFalse_ForUnknown()
    {
        Assert.False(PluginCapability.IsKnown("host.events.bogus"));
        Assert.False(PluginCapability.IsKnown(""));
        Assert.False(PluginCapability.IsKnown("not.a.real.capability"));
    }

    [Fact]
    public void IsHostEnforced_ReturnsTrue_ForHostNamespace()
    {
        Assert.True(PluginCapability.IsHostEnforced("host.events.account-launched"));
        Assert.True(PluginCapability.IsHostEnforced("host.commands.request-launch"));
    }

    [Fact]
    public void IsHostEnforced_ReturnsFalse_ForSystemNamespace()
    {
        Assert.False(PluginCapability.IsHostEnforced("system.synthesize-keyboard-input"));
    }

    [Fact]
    public void Display_ReturnsHumanReadableExplanation()
    {
        var explanation = PluginCapability.Display("host.events.account-launched");
        Assert.Contains("when an account launches", explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Display_ReturnsHumanReadableExplanation_ForReadScreen()
    {
        var explanation = PluginCapability.Display("system.read-screen");
        Assert.Contains("read pixels", explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Unknown capability", explanation, StringComparison.OrdinalIgnoreCase);
    }
}
