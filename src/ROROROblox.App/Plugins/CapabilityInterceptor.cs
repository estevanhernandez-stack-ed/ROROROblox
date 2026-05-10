using Grpc.Core;
using Grpc.Core.Interceptors;

namespace ROROROblox.App.Plugins;

/// <summary>
/// Server-side gRPC interceptor that gates each call by the calling plugin's
/// declared+consented capabilities. The current-plugin accessor is provided
/// per-connection (set during handshake); the consent lookup returns the
/// capabilities that plugin has been granted by the user.
///
/// Bootstrap calls (Handshake, free reads) are allowed without a known plugin.
/// Gated calls before handshake throw <see cref="StatusCode.FailedPrecondition"/>;
/// gated calls without consent throw <see cref="StatusCode.PermissionDenied"/>.
/// </summary>
public sealed class CapabilityInterceptor : Interceptor
{
    private readonly Func<string?> _currentPluginAccessor;
    private readonly Func<string, IReadOnlyList<string>> _consentLookup;

    public CapabilityInterceptor(
        Func<string?> currentPluginAccessor,
        Func<string, IReadOnlyList<string>> consentLookup)
    {
        _currentPluginAccessor = currentPluginAccessor ?? throw new ArgumentNullException(nameof(currentPluginAccessor));
        _consentLookup = consentLookup ?? throw new ArgumentNullException(nameof(consentLookup));
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        EnforceCapability(context);
        return await continuation(request, context).ConfigureAwait(false);
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        EnforceCapability(context);
        await continuation(request, responseStream, context).ConfigureAwait(false);
    }

    private void EnforceCapability(ServerCallContext context)
    {
        var methodName = RpcMethodCapabilityMap.ExtractMethodName(context.Method);
        var required = RpcMethodCapabilityMap.Required(methodName);
        if (required is null)
        {
            return; // ungated method
        }

        var pluginId = _currentPluginAccessor();
        if (pluginId is null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                "Handshake required before this call."));
        }

        var granted = _consentLookup(pluginId);
        if (!granted.Contains(required))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied,
                $"Plugin '{pluginId}' has not been granted '{required}'."));
        }
    }
}
