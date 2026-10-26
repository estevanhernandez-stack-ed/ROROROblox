namespace ROROROblox.App.Plugins;

/// <summary>
/// Seam between the plugin host's <c>RequestLaunch</c> RPC and the App-layer
/// launch pipeline (cookie capture → auth-ticket → <c>roblox-player:</c> URI).
/// The host RPC accepts a launch request and returns the resulting PID; the
/// invoker hides the underlying <c>IRobloxLauncher</c> + account-store wiring
/// so the gRPC surface stays test-isolated.
/// </summary>
public interface IPluginLaunchInvoker
{
    /// <summary>
    /// Launch the saved account identified by <paramref name="accountId"/>.
    /// Returns success + the launched PID, or failure with a reason string.
    /// Implementations must not throw on user-recoverable errors (account
    /// missing, mutex held, cookie expired) — return <c>(false, reason, 0, reasonCode)</c>
    /// so the calling plugin can surface the failure cleanly. <c>reasonCode</c> is a stable,
    /// machine-readable string (see <see cref="PluginLaunchReasonCodes"/>) for failures a plugin
    /// might want to branch on, and null otherwise — a plugin can't answer the flagged-launch
    /// dialog, so a refusal must be diagnosable without parsing prose.
    /// </summary>
    Task<(bool ok, string? failureReason, int processId, string? reasonCode)> RequestLaunchAsync(string accountId);

    /// <summary>
    /// Launch <paramref name="accountId"/> into a target. Exactly one of
    /// <paramref name="shareUrl"/> / <paramref name="followUserId"/> / <paramref name="gameInstance"/>
    /// is set by the caller; shareUrl is resolved by the host's share-URL resolver, followUserId
    /// becomes a follow-friend launch, gameInstance joins exactly that server or is refused with
    /// <see cref="PluginLaunchReasonCodes.InvalidTarget"/> (never degraded to "any server", #247).
    /// Same return contract as <see cref="RequestLaunchAsync"/>.
    /// </summary>
    Task<(bool ok, string? failureReason, int processId, string? reasonCode)> RequestLaunchTargetAsync(
        string accountId, string? shareUrl, long? followUserId, PluginGameInstance? gameInstance = null);

    /// <summary>Most-recently-launched saved private server, or null if none.</summary>
    Task<CurrentServerInfo?> GetCurrentServerAsync();
}

/// <summary>
/// The raw halves of a <c>GameInstance</c> as a plugin sent them, unvalidated. Present means "a
/// game instance was requested", so a half pair (place 0, empty job id) still reaches the adapter
/// and is refused there instead of reading as "no target". Validation lives in one place:
/// <c>MainViewModelLaunchInvokerAdapter.ValidateLaunchTargetArgs</c>.
/// </summary>
public sealed record PluginGameInstance(long PlaceId, string JobId);

/// <summary>Host-internal DTO for the GetCurrentServer RPC (mapped to proto in the service).</summary>
public sealed record CurrentServerInfo(
    string ShareUrl, string PlaceName, long PlaceId, long LastLaunchedAtUnixMs);
