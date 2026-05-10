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
}
