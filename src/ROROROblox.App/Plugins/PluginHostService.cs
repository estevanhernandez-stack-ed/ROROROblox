using System.Threading.Channels;
using Grpc.Core;
using ROROROblox.PluginContract;

namespace ROROROblox.App.Plugins;

/// <summary>
/// gRPC server-side implementation of the RoRoRoHost service. Plugins connect over the
/// per-plugin named pipe and call into this surface.
///
/// Marked partial — items 11-14 will extend the same class with the capability gate
/// (RpcMethodCapabilityMap + interceptor), event streaming (SubscribeAccountLaunched,
/// etc.), command surface (RequestLaunch), and UI surface (AddTrayMenuItem, etc.).
/// Keeping each surface in its own file keeps blast radius tight when the spec shifts.
/// </summary>
public sealed partial class PluginHostService : RoRoRoHost.RoRoRoHostBase
{
    private readonly IInstalledPluginsLookup _registry;
    private readonly string _hostVersion;
    private readonly string _supportedContractVersion;
    private readonly IPluginHostStateProvider _hostState;
    private readonly IRunningAccountsProvider _runningAccounts;
    private readonly IPluginEventBus _eventBus;
    private readonly IPluginLaunchInvoker _launcher;

    public PluginHostService(
        IInstalledPluginsLookup registry,
        string hostVersion,
        string supportedContractVersion,
        IPluginHostStateProvider hostState,
        IRunningAccountsProvider runningAccounts,
        IPluginEventBus eventBus,
        IPluginLaunchInvoker launcher)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _hostVersion = hostVersion ?? throw new ArgumentNullException(nameof(hostVersion));
        _supportedContractVersion = supportedContractVersion ?? throw new ArgumentNullException(nameof(supportedContractVersion));
        _hostState = hostState ?? throw new ArgumentNullException(nameof(hostState));
        _runningAccounts = runningAccounts ?? throw new ArgumentNullException(nameof(runningAccounts));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
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

    public override Task<HostInfo> GetHostInfo(Empty request, ServerCallContext context)
    {
        return Task.FromResult(new HostInfo
        {
            Version = _hostVersion,
            MultiInstanceEnabled = _hostState.MultiInstanceEnabled,
            MultiInstanceState = _hostState.MultiInstanceState,
        });
    }

    public override Task<RunningAccountsList> GetRunningAccounts(Empty request, ServerCallContext context)
    {
        var list = new RunningAccountsList();
        foreach (var snapshot in _runningAccounts.Snapshot())
        {
            list.Accounts.Add(new RunningAccount
            {
                AccountId = snapshot.AccountId,
                RobloxUserId = snapshot.RobloxUserId,
                DisplayName = snapshot.DisplayName,
                ProcessId = snapshot.ProcessId,
            });
        }
        return Task.FromResult(list);
    }

    // =====================================================================
    // Server-streaming event subscriptions (item 12 / plan task 14).
    //
    // Each subscribe RPC creates a per-call bounded channel (capacity 64,
    // DropOldest), attaches a handler to the bus, and pumps events to the
    // gRPC stream. The stream completes when the caller cancels (typically
    // when the plugin process disconnects, which the supervisor in item 13
    // surfaces as a cancelled CancellationToken on this context).
    //
    // Bounded over unbounded so a stuck consumer can't grow memory without
    // limit; DropOldest over Wait so a slow consumer doesn't block the
    // producer (the App layer raising events). The 5s write-timeout / treat-
    // as-crashed semantics from spec §plugin live with the supervisor side
    // of the connection, not here — v1 simply drops the oldest event.
    // =====================================================================

    public override async Task SubscribeAccountLaunched(
        SubscriptionRequest request,
        IServerStreamWriter<AccountLaunchedEvent> responseStream,
        ServerCallContext context)
    {
        var channel = Channel.CreateBounded<AccountLaunchedEvent>(
            new BoundedChannelOptions(64)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });

        void Handler(RunningAccountSnapshot s)
        {
            channel.Writer.TryWrite(new AccountLaunchedEvent
            {
                AccountId = s.AccountId,
                RobloxUserId = s.RobloxUserId,
                DisplayName = s.DisplayName,
                ProcessId = s.ProcessId,
                LaunchedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
        }

        _eventBus.AccountLaunched += Handler;
        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(context.CancellationToken).ConfigureAwait(false))
            {
                await responseStream.WriteAsync(evt).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* clean stream end on caller disconnect */ }
        finally
        {
            _eventBus.AccountLaunched -= Handler;
            channel.Writer.TryComplete();
        }
    }

    public override async Task SubscribeAccountExited(
        SubscriptionRequest request,
        IServerStreamWriter<AccountExitedEvent> responseStream,
        ServerCallContext context)
    {
        var channel = Channel.CreateBounded<AccountExitedEvent>(
            new BoundedChannelOptions(64)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });

        void Handler(RunningAccountSnapshot s, long exitedAtUnixMs)
        {
            channel.Writer.TryWrite(new AccountExitedEvent
            {
                AccountId = s.AccountId,
                RobloxUserId = s.RobloxUserId,
                ProcessId = s.ProcessId,
                ExitedAtUnixMs = exitedAtUnixMs,
            });
        }

        _eventBus.AccountExited += Handler;
        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(context.CancellationToken).ConfigureAwait(false))
            {
                await responseStream.WriteAsync(evt).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* clean stream end on caller disconnect */ }
        finally
        {
            _eventBus.AccountExited -= Handler;
            channel.Writer.TryComplete();
        }
    }

    public override async Task SubscribeMutexStateChanged(
        SubscriptionRequest request,
        IServerStreamWriter<MutexStateEvent> responseStream,
        ServerCallContext context)
    {
        var channel = Channel.CreateBounded<MutexStateEvent>(
            new BoundedChannelOptions(64)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });

        void Handler(string state) => channel.Writer.TryWrite(new MutexStateEvent { State = state });

        _eventBus.MutexStateChanged += Handler;
        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(context.CancellationToken).ConfigureAwait(false))
            {
                await responseStream.WriteAsync(evt).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* clean stream end on caller disconnect */ }
        finally
        {
            _eventBus.MutexStateChanged -= Handler;
            channel.Writer.TryComplete();
        }
    }

    // =====================================================================
    // Command surface (item 13 / plan task 15).
    //
    // RequestLaunch is the plugin-side trigger for the launch pipeline. The
    // RPC hands off to IPluginLaunchInvoker — the App-layer adapter wires it
    // to the cookie-capture / auth-ticket / roblox-player: URI flow. Capability
    // gate (host.commands.request-launch) is enforced upstream by the
    // CapabilityInterceptor (item 11) via RpcMethodCapabilityMap, so this body
    // assumes the call already passed consent.
    // =====================================================================

    public override async Task<LaunchResult> RequestLaunch(LaunchRequest request, ServerCallContext context)
    {
        var (ok, reason, pid) = await _launcher.RequestLaunchAsync(request.AccountId).ConfigureAwait(false);
        return new LaunchResult
        {
            Ok = ok,
            FailureReason = reason ?? string.Empty,
            ProcessId = pid,
        };
    }
}
