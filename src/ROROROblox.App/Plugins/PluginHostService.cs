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
    private readonly PluginUITranslator _uiTranslator;
    private readonly IActivitySnapshotProvider _activityProvider;
    private readonly IAccountActivityMarker _activityMarker;
    private readonly IPluginAccountStopper _accountStopper;

    /// <summary>
    /// Source of the host's active palette. <b>Optional on purpose, and the exception among these
    /// dependencies.</b> PluginHostService is constructed at 30 places across the two test
    /// projects; making this required would edit all 30 for no behavioural gain in 29 of them.
    /// <para>
    /// Null means "this host has no theme feed configured" — GetTheme fails cleanly and the
    /// subscription ends rather than hanging. That is correct for a test that does not care about
    /// theming and <b>wrong everywhere else</b>, so an optional dependency silently unwired in
    /// production would be a real defect. <c>ThemeFeedWiringTests</c> reads the app's real
    /// registration and asserts the source is supplied; that test is the price of the convenience.
    /// </para>
    /// </summary>
    private readonly Adapters.IThemePaletteSource? _themePalettes;

    /// <summary>
    /// All-saved-accounts source for <c>GetAccounts</c> (contract 0.9.0). Optional for the same
    /// reason as <see cref="_themePalettes"/> above — 30 construction sites, 29 of which do not
    /// care — and guarded the same way: <c>SavedAccountsWiringTests</c> asserts the production
    /// registration supplies it. Null makes GetAccounts fail with FailedPrecondition rather than
    /// answer "no accounts", because an empty roster and an unwired provider are different claims.
    /// </summary>
    private readonly ISavedAccountsProvider? _savedAccounts;

    public PluginHostService(
        IInstalledPluginsLookup registry,
        string hostVersion,
        string supportedContractVersion,
        IPluginHostStateProvider hostState,
        IRunningAccountsProvider runningAccounts,
        IPluginEventBus eventBus,
        IPluginLaunchInvoker launcher,
        PluginUITranslator uiTranslator,
        IActivitySnapshotProvider activityProvider,
        IAccountActivityMarker activityMarker,
        IPluginAccountStopper accountStopper,
        Adapters.IThemePaletteSource? themePalettes = null,
        ISavedAccountsProvider? savedAccounts = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _hostVersion = hostVersion ?? throw new ArgumentNullException(nameof(hostVersion));
        _supportedContractVersion = supportedContractVersion ?? throw new ArgumentNullException(nameof(supportedContractVersion));
        _hostState = hostState ?? throw new ArgumentNullException(nameof(hostState));
        _runningAccounts = runningAccounts ?? throw new ArgumentNullException(nameof(runningAccounts));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _uiTranslator = uiTranslator ?? throw new ArgumentNullException(nameof(uiTranslator));
        _activityProvider = activityProvider ?? throw new ArgumentNullException(nameof(activityProvider));
        _activityMarker = activityMarker ?? throw new ArgumentNullException(nameof(activityMarker));
        _accountStopper = accountStopper ?? throw new ArgumentNullException(nameof(accountStopper));
        _themePalettes = themePalettes;
        _savedAccounts = savedAccounts;
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
                PlaceId = snapshot.PlaceId,
                PlaceName = snapshot.PlaceName,
            });
        }
        return Task.FromResult(list);
    }

    public override Task<AccountActivityList> GetAccountActivity(Empty request, ServerCallContext context)
    {
        var list = new AccountActivityList();
        foreach (var a in _activityProvider.Snapshot())
        {
            list.Items.Add(new AccountActivity
            {
                AccountId = a.AccountId,
                LastActivityUnixMs = a.LastActivityUnixMs,
                SecondsSinceActivity = a.SecondsSinceActivity,
            });
        }
        return Task.FromResult(list);
    }

    public override Task<SavedAccountsList> GetAccounts(Empty request, ServerCallContext context)
    {
        if (_savedAccounts is null)
        {
            // Unwired provider, not an empty roster — same distinction GetTheme draws for its
            // optional source. Reachable only from a test host; SavedAccountsWiringTests pins
            // the production registration.
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                "Saved accounts are not available on this host."));
        }

        var list = new SavedAccountsList();
        foreach (var a in _savedAccounts.Snapshot())
        {
            list.Accounts.Add(new SavedAccount
            {
                AccountId = a.AccountId,
                RobloxUserId = a.RobloxUserId,
                DisplayName = a.DisplayName,
                IsMain = a.IsMain,
            });
        }
        return Task.FromResult(list);
    }

    // =====================================================================
    // MarkAccountActive (activity-crediting-fix plan, task 3).
    //
    // Fire-and-forget stamp: a consented plugin tells the host it kept an
    // account's window active (e.g. a keep-alive tap it synthesized). The
    // handler is a pass-through to IAccountActivityMarker — no input
    // sensing, no reasoning about what the plugin did, just a dictionary
    // stamp. Capability gate (host.commands.mark-account-active) is
    // enforced upstream by CapabilityInterceptor via RpcMethodCapabilityMap.
    // =====================================================================

    public override Task<Empty> MarkAccountActive(MarkAccountActiveRequest request, ServerCallContext context)
    {
        _activityMarker.Mark(request.AccountId);
        return Task.FromResult(new Empty());
    }

    // =====================================================================
    // StopAccounts (agent-ops surface, NuGet 0.6.0).
    //
    // Per-account close of the clients RoRoRo tracks — the recovery half of
    // "the internet dropped, put my accounts back". An empty account_ids means
    // every tracked account; an id we don't track lands in failed_account_ids
    // rather than failing the batch, so a partial recovery still reports what
    // it managed. Untracked processes are unreachable from here by design.
    // Capability gate (host.commands.stop-accounts) is enforced upstream.
    // =====================================================================

    public override Task<StopAccountsResult> StopAccounts(StopAccountsRequest request, ServerCallContext context)
    {
        var targets = request.AccountIds.Count > 0
            ? request.AccountIds.Distinct(StringComparer.Ordinal).ToList()
            : _accountStopper.TrackedAccountIds.ToList();

        var result = new StopAccountsResult();
        foreach (var accountId in targets)
        {
            if (_accountStopper.StopAccount(accountId))
            {
                result.StoppedCount++;
            }
            else
            {
                result.FailedAccountIds.Add(accountId);
            }
        }
        return Task.FromResult(result);
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
                PlaceId = s.PlaceId,
                PlaceName = s.PlaceName,
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
    // Theming (v1.19). Two RPCs because a theme is STATE, not an occurrence: GetTheme answers
    // "what colour are you right now", the stream answers "tell me when that changes". Both are
    // ungated in RpcMethodCapabilityMap, deliberately and with the reasoning recorded there.
    //
    // Both send resolved colours and never a theme id. A plugin holding an id needs somewhere to
    // look it up, and the only "somewhere" was this app's own settings file and themes folder --
    // which is F-091, and which worked for user themes (files) and could never work for built-ins
    // (records in host code).
    // =====================================================================

    public override Task<ThemePalette> GetTheme(Empty request, ServerCallContext context)
    {
        var palette = _themePalettes?.Latest;
        if (palette is null)
        {
            // Either no feed configured (a test that does not care about theming) or no theme
            // applied yet (not reachable in production -- ApplyAtStartup runs long before the pipe
            // binds). FailedPrecondition rather than an empty palette: a plugin can fall back to
            // its own default, and cannot mistake eleven empty strings for a colour scheme.
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                "No theme has been applied yet."));
        }
        return Task.FromResult(ToProto(palette));
    }

    public override async Task SubscribeThemeChanged(
        SubscriptionRequest request,
        IServerStreamWriter<ThemePalette> responseStream,
        ServerCallContext context)
    {
        // Capacity 1, not the 64 the four event streams above use, and DropOldest. Those carry
        // occurrences where every item matters; this carries state where only the latest one does.
        // A plugin that stalls through three theme switches wants the theme that is on screen now,
        // not a replay of the two the user already moved past -- and at depth 1 that is a property
        // of the channel rather than a promise in a comment.
        var channel = Channel.CreateBounded<ROROROblox.Core.Theming.ResolvedPalette>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });

        void Handler(ROROROblox.Core.Theming.ResolvedPalette palette) => channel.Writer.TryWrite(palette);

        _eventBus.ThemeChanged += Handler;
        try
        {
            // Paint on subscribe, before waiting for anything to change. Without this a plugin
            // that only subscribes sits on its fallback colour until the user happens to open the
            // theme picker, which most sessions never do -- so the broken case would be the
            // common one. Sent before the loop so it cannot be dropped by a later change racing it.
            var current = _themePalettes?.Latest;
            if (current is not null)
            {
                await responseStream.WriteAsync(ToProto(current)).ConfigureAwait(false);
            }

            await foreach (var palette in channel.Reader.ReadAllAsync(context.CancellationToken).ConfigureAwait(false))
            {
                await responseStream.WriteAsync(ToProto(palette)).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* clean stream end on caller disconnect */ }
        finally
        {
            _eventBus.ThemeChanged -= Handler;
            channel.Writer.TryComplete();
        }
    }

    private static ThemePalette ToProto(ROROROblox.Core.Theming.ResolvedPalette p) => new()
    {
        Bg = p.Bg,
        Cyan = p.Cyan,
        Magenta = p.Magenta,
        White = p.White,
        MutedText = p.MutedText,
        Divider = p.Divider,
        RowBg = p.RowBg,
        RowExpiredBg = p.RowExpiredBg,
        RowExpiredAccent = p.RowExpiredAccent,
        Navy = p.Navy,
        InteractiveEdge = p.InteractiveEdge,
    };

    // =====================================================================
    // SubscribeMemoryPressure (memory-watchdog plan, task 10).
    //
    // Same shape as the three streams above. One AccountMemorySnapshot per tracked
    // account per crossing -- private_bytes stays a stale last-known-good figure (not
    // zeroed) when read_ok is false, matching MemoryWatchdog's own internal convention
    // (see MemoryWatchdog.FormatAccountPayload's "(stale)" tag); the plugin decides
    // whether a stale reading is still useful, but it can never mistake it for fresh.
    // Capability gate (host.events.memory-pressure) is enforced upstream by
    // CapabilityInterceptor via RpcMethodCapabilityMap.
    // =====================================================================

    public override async Task SubscribeMemoryPressure(
        SubscriptionRequest request,
        IServerStreamWriter<AccountMemorySnapshot> responseStream,
        ServerCallContext context)
    {
        var channel = Channel.CreateBounded<AccountMemorySnapshot>(
            new BoundedChannelOptions(64)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });

        void Handler(ROROROblox.Core.Diagnostics.AccountMemory a) => channel.Writer.TryWrite(new AccountMemorySnapshot
        {
            AccountId = a.AccountId.ToString(),
            PrivateBytes = (ulong)a.PrivateBytes,
            GrowthMbPerHr = a.GrowthBytesPerHour / (1024.0 * 1024.0),
            MinsToCeiling = (uint)a.MinutesToCeiling,
            OverCap = a.OverCap,
            IsTarget = a.IsTarget,
            ReadOk = a.ReadOk,
        });

        _eventBus.MemoryPressure += Handler;
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
            _eventBus.MemoryPressure -= Handler;
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

    public override async Task<LaunchResult> RequestLaunchTarget(LaunchTargetRequest request, ServerCallContext context)
    {
        string? shareUrl = request.TargetCase == LaunchTargetRequest.TargetOneofCase.ShareUrl ? request.ShareUrl : null;
        long? followUserId = request.TargetCase == LaunchTargetRequest.TargetOneofCase.FollowUserId ? request.FollowUserId : null;
        var (ok, reason, pid) = await _launcher.RequestLaunchTargetAsync(request.AccountId, shareUrl, followUserId).ConfigureAwait(false);
        return new LaunchResult
        {
            Ok = ok,
            FailureReason = reason ?? string.Empty,
            ProcessId = pid,
        };
    }

    public override async Task<CurrentServer> GetCurrentServer(Empty request, ServerCallContext context)
    {
        var info = await _launcher.GetCurrentServerAsync().ConfigureAwait(false);
        if (info is null) return new CurrentServer { Present = false };
        return new CurrentServer
        {
            Present = true,
            ShareUrl = info.ShareUrl,
            PlaceName = info.PlaceName,
            PlaceId = info.PlaceId,
            LastLaunchedAtUnixMs = info.LastLaunchedAtUnixMs,
        };
    }

    // =====================================================================
    // UI surface (item 14 / plan task 16).
    //
    // AddTrayMenuItem / AddRowBadge / AddStatusPanel forward to the
    // PluginUITranslator, which in turn calls into IPluginUIHost (the WPF-side
    // host wired in App.xaml.cs item 15). Capability gates are enforced by
    // CapabilityInterceptor; bodies assume the call has already passed consent.
    //
    // Per-connection plugin-id binding is read from the request metadata header
    // "x-plugin-id". v1 ships this as the convention until per-call interceptor
    // state plumbing lands (v1.5+). The end-to-end test in PluginTestHarness
    // exercises this header path; the in-process unit tests for the translator
    // exercise the ownership / dispatch logic directly.
    // =====================================================================

    public override Task<UIHandle> AddTrayMenuItem(MenuItemSpec request, ServerCallContext context)
    {
        var pluginId = ResolveCurrentPluginId(context);
        return Task.FromResult(_uiTranslator.AddTrayMenuItem(pluginId, request));
    }

    public override Task<UIHandle> AddRowBadge(RowBadgeSpec request, ServerCallContext context)
    {
        var pluginId = ResolveCurrentPluginId(context);
        return Task.FromResult(_uiTranslator.AddRowBadge(pluginId, request));
    }

    public override Task<UIHandle> AddStatusPanel(StatusPanelSpec request, ServerCallContext context)
    {
        var pluginId = ResolveCurrentPluginId(context);
        return Task.FromResult(_uiTranslator.AddStatusPanel(pluginId, request));
    }

    public override Task<Empty> UpdateUI(UIUpdate request, ServerCallContext context)
    {
        // Ownership is the ONLY gate on UpdateUI (the capability map leaves it
        // ungated), so an unknown or foreign handle must refuse here. Same status
        // for both cases: callers must not be able to probe which handle ids exist.
        // Dispatching the spec-typed update through to IPluginUIHost is still
        // future work — an owned handle gets an acknowledged no-op.
        var pluginId = ResolveCurrentPluginId(context);
        var handleId = request.Handle?.Id ?? string.Empty;
        if (!_uiTranslator.OwnsHandle(pluginId, handleId))
        {
            throw HandleNotOwned(pluginId, handleId);
        }
        return Task.FromResult(new Empty());
    }

    public override Task<Empty> RemoveUI(UIHandle request, ServerCallContext context)
    {
        var pluginId = ResolveCurrentPluginId(context);
        if (!_uiTranslator.RemoveUI(pluginId, request))
        {
            throw HandleNotOwned(pluginId, request.Id);
        }
        return Task.FromResult(new Empty());
    }

    private static RpcException HandleNotOwned(string pluginId, string handleId) =>
        new(new Status(StatusCode.PermissionDenied,
            $"UI handle '{handleId}' is not owned by plugin '{pluginId}'."));

    private static string ResolveCurrentPluginId(ServerCallContext context)
    {
        // v1 contract: the plugin process puts its id in the call's request metadata
        // header "x-plugin-id". The handshake-rejection path enforces that only
        // installed plugins can connect, so a forged header from outside the
        // per-user named pipe is not a useful attack vector. Tighter binding
        // (per-connection interceptor state) lands in v1.5+.
        return context.RequestHeaders.GetValue("x-plugin-id") ?? string.Empty;
    }
}
