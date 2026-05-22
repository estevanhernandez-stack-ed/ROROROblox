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
    /// missing, mutex held, cookie expired) — return <c>(false, reason, 0)</c>
    /// so the calling plugin can surface the failure cleanly.
    /// </summary>
    Task<(bool ok, string? failureReason, int processId)> RequestLaunchAsync(string accountId);

    /// <summary>
    /// Launch <paramref name="accountId"/> into a target. Exactly one of
    /// <paramref name="shareUrl"/> / <paramref name="followUserId"/> is set by the caller;
    /// shareUrl is resolved by the host's share-URL resolver, followUserId becomes a
    /// follow-friend launch. Same return contract as <see cref="RequestLaunchAsync"/>.
    /// </summary>
    Task<(bool ok, string? failureReason, int processId)> RequestLaunchTargetAsync(
        string accountId, string? shareUrl, long? followUserId);

    /// <summary>Most-recently-launched saved private server, or null if none.</summary>
    Task<CurrentServerInfo?> GetCurrentServerAsync();
}

/// <summary>Host-internal DTO for the GetCurrentServer RPC (mapped to proto in the service).</summary>
public sealed record CurrentServerInfo(
    string ShareUrl, string PlaceName, long PlaceId, long LastLaunchedAtUnixMs);
