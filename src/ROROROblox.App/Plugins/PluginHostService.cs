using Grpc.Core;
using ROROROblox.PluginContract;

namespace ROROROblox.App.Plugins;

/// <summary>
/// gRPC server-side implementation of the RoRoRoHost service. Plugins connect over the
/// per-plugin named pipe and call into this surface.
///
/// Marked partial — items 10-14 will extend the same class with read RPCs (GetHostInfo,
/// GetRunningAccounts), event streaming (SubscribeAccountLaunched, etc.), command surface
/// (RequestLaunch), and UI surface (AddTrayMenuItem, etc.). Keeping each surface in its
/// own file keeps blast radius tight when the spec shifts.
/// </summary>
public sealed partial class PluginHostService : RoRoRoHost.RoRoRoHostBase
{
    private readonly IInstalledPluginsLookup _registry;
    private readonly string _hostVersion;
    private readonly string _supportedContractVersion;

    public PluginHostService(IInstalledPluginsLookup registry, string hostVersion, string supportedContractVersion)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _hostVersion = hostVersion ?? throw new ArgumentNullException(nameof(hostVersion));
        _supportedContractVersion = supportedContractVersion ?? throw new ArgumentNullException(nameof(supportedContractVersion));
    }

    public override Task<HandshakeResponse> Handshake(HandshakeRequest request, ServerCallContext context)
    {
        var plugin = _registry.FindById(request.PluginId);
        if (plugin is null)
        {
            return Task.FromResult(new HandshakeResponse
            {
                Accepted = false,
                RejectReason = $"Plugin {request.PluginId} is not installed.",
                HostVersion = _hostVersion,
                ContractVersion = _supportedContractVersion,
            });
        }

        if (request.ContractVersion != _supportedContractVersion)
        {
            return Task.FromResult(new HandshakeResponse
            {
                Accepted = false,
                RejectReason = $"Plugin contract version {request.ContractVersion} not supported. Host expects {_supportedContractVersion}.",
                HostVersion = _hostVersion,
                ContractVersion = _supportedContractVersion,
            });
        }

        return Task.FromResult(new HandshakeResponse
        {
            Accepted = true,
            HostVersion = _hostVersion,
            ContractVersion = _supportedContractVersion,
        });
    }
}
