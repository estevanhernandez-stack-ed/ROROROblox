using ROROROblox.App.Plugins;

namespace ROROROblox.Tests;

public class RpcMethodCapabilityMapTests
{
    [Fact]
    public void RequestLaunchTarget_RequiresLaunchTargetCapability()
        => Assert.Equal(PluginCapability.HostCommandsLaunchTarget, RpcMethodCapabilityMap.Required("RequestLaunchTarget"));

    [Fact]
    public void GetCurrentServer_RequiresCurrentServerCapability()
        => Assert.Equal(PluginCapability.HostQueriesCurrentServer, RpcMethodCapabilityMap.Required("GetCurrentServer"));

    [Fact]
    public void RequestLaunchTarget_IsKnown()
        => Assert.True(RpcMethodCapabilityMap.IsKnown("RequestLaunchTarget"));

    [Fact]
    public void ExtractMethodName_PullsTrailingComponent()
        => Assert.Equal("RequestLaunchTarget", RpcMethodCapabilityMap.ExtractMethodName("/rororo.plugin.v1.RoRoRoHost/RequestLaunchTarget"));
}
