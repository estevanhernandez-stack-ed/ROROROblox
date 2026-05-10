using Grpc.Core;
using ROROROblox.App.Plugins;

namespace ROROROblox.Tests;

public class CapabilityInterceptorTests
{
    [Fact]
    public async Task UnaryServerHandler_AllowsCallWhenCapabilityGranted()
    {
        var interceptor = new CapabilityInterceptor(
            currentPluginAccessor: () => "626labs.test",
            consentLookup: pluginId => new[] { "host.commands.request-launch" });

        UnaryServerMethod<object, string> continuation =
            (req, ctx) => Task.FromResult("ok");

        var ctx = FakeServerCallContext.Create("/rororo.plugin.v1.RoRoRoHost/RequestLaunch");
        var result = await interceptor.UnaryServerHandler<object, string>(new(), ctx, continuation);

        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task UnaryServerHandler_ThrowsPermissionDenied_WhenCapabilityMissing()
    {
        var interceptor = new CapabilityInterceptor(
            currentPluginAccessor: () => "626labs.test",
            consentLookup: pluginId => Array.Empty<string>());

        UnaryServerMethod<object, string> continuation =
            (req, ctx) => Task.FromResult("ok");

        var ctx = FakeServerCallContext.Create("/rororo.plugin.v1.RoRoRoHost/RequestLaunch");
        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.UnaryServerHandler<object, string>(new(), ctx, continuation));
        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
    }

    [Fact]
    public async Task UnaryServerHandler_AllowsHandshake_BeforePluginIsKnown()
    {
        var interceptor = new CapabilityInterceptor(
            currentPluginAccessor: () => null,
            consentLookup: _ => Array.Empty<string>());

        UnaryServerMethod<object, string> continuation =
            (req, ctx) => Task.FromResult("handshake-ok");

        var ctx = FakeServerCallContext.Create("/rororo.plugin.v1.RoRoRoHost/Handshake");
        var result = await interceptor.UnaryServerHandler<object, string>(new(), ctx, continuation);

        Assert.Equal("handshake-ok", result);
    }
}
