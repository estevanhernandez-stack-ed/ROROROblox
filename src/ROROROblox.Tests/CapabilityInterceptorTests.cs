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

    // =====================================================================
    // Fail closed. Before this, an rpc absent from RpcMethodCapabilityMap was
    // treated as ungated and ran — which is how UpdateUI/RemoveUI shipped without
    // a gate (PR #60). An unrecognized method must now be denied, and must never
    // reach the continuation.
    // =====================================================================

    [Fact]
    public async Task UnaryServerHandler_ThrowsPermissionDenied_ForUnknownMethod()
    {
        var interceptor = new CapabilityInterceptor(
            currentPluginAccessor: () => "626labs.test",
            consentLookup: _ => new[] { "host.commands.request-launch" });

        var reached = false;
        UnaryServerMethod<object, string> continuation =
            (req, ctx) => { reached = true; return Task.FromResult("ok"); };

        var ctx = FakeServerCallContext.Create("/rororo.plugin.v1.RoRoRoHost/SomeFutureRpc");
        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.UnaryServerHandler<object, string>(new(), ctx, continuation));

        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
        Assert.False(reached, "an unknown method must never reach the handler");
    }

    [Fact]
    public async Task UnaryServerHandler_AllowsKnownUngatedMethod()
    {
        // GetHostInfo is mapped to null — deliberately ungated, and must stay callable.
        var interceptor = new CapabilityInterceptor(
            currentPluginAccessor: () => null,
            consentLookup: _ => Array.Empty<string>());

        UnaryServerMethod<object, string> continuation =
            (req, ctx) => Task.FromResult("host-info");

        var ctx = FakeServerCallContext.Create("/rororo.plugin.v1.RoRoRoHost/GetHostInfo");
        var result = await interceptor.UnaryServerHandler<object, string>(new(), ctx, continuation);

        Assert.Equal("host-info", result);
    }

    [Fact]
    public async Task ServerStreamingHandler_ThrowsPermissionDenied_ForUnknownMethod()
    {
        var interceptor = new CapabilityInterceptor(
            currentPluginAccessor: () => "626labs.test",
            consentLookup: _ => Array.Empty<string>());

        var reached = false;
        ServerStreamingServerMethod<object, string> continuation =
            (req, stream, ctx) => { reached = true; return Task.CompletedTask; };

        var ctx = FakeServerCallContext.Create("/rororo.plugin.v1.RoRoRoHost/SomeFutureStream");
        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.ServerStreamingServerHandler<object, string>(
                new(), new NullStreamWriter<string>(), ctx, continuation));

        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
        Assert.False(reached, "an unknown streaming method must never reach the handler");
    }

    private sealed class NullStreamWriter<T> : IServerStreamWriter<T>
    {
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(T message) => Task.CompletedTask;
    }
}
