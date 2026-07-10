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

    [Fact]
    public void GetAccountActivity_RequiresActivityCapability()
        => Assert.Equal(PluginCapability.HostQueriesAccountActivity, RpcMethodCapabilityMap.Required("GetAccountActivity"));

    [Fact]
    public void StopAccounts_RequiresStopAccountsCapability()
        => Assert.Equal(PluginCapability.HostCommandsStopAccounts, RpcMethodCapabilityMap.Required("StopAccounts"));

    // =====================================================================
    // Absence is not permission. Required() returns null for BOTH an ungated
    // method and an unknown one; TryGetRequired is what separates them, and the
    // interceptor denies the unknown case. Reading a null Required() as "no
    // capability needed" is exactly how UpdateUI/RemoveUI shipped ungated.
    // =====================================================================

    [Fact]
    public void TryGetRequired_UnknownMethod_ReturnsFalse()
        => Assert.False(RpcMethodCapabilityMap.TryGetRequired("NoSuchMethod", out _));

    [Fact]
    public void TryGetRequired_KnownUngatedMethod_ReturnsTrueWithNullCapability()
    {
        Assert.True(RpcMethodCapabilityMap.TryGetRequired("GetHostInfo", out var capability));
        Assert.Null(capability);
    }

    [Fact]
    public void TryGetRequired_KnownGatedMethod_ReturnsTrueWithCapability()
    {
        Assert.True(RpcMethodCapabilityMap.TryGetRequired("StopAccounts", out var capability));
        Assert.Equal(PluginCapability.HostCommandsStopAccounts, capability);
    }

    /// <summary>
    /// The forcing function. Add an rpc to plugin_contract.proto without a map entry and
    /// this test goes red — instead of the method shipping ungated, which is the bug class
    /// behind PR #60. The same assert runs at host startup.
    /// </summary>
    [Fact]
    public void EveryRoRoRoHostMethod_HasACapabilityMapEntry()
        => RpcMethodCapabilityMap.AssertExhaustive();
}
