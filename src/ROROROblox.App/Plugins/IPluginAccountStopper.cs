namespace ROROROblox.App.Plugins;

/// <summary>
/// Stops Roblox clients RoRoRo tracks, per account. The App layer adapts this onto
/// <c>IRobloxProcessTracker</c>, which owns the account↔PID mapping.
///
/// <para>Deliberately NOT backed by <c>IRobloxInstanceStopper</c>: that interface force-closes
/// every RobloxPlayerBeta on the box, "tracked or not", which is right for the startup leftover
/// gate and wrong for a plugin-invoked command. Untracked processes are out of reach here by
/// construction.</para>
/// </summary>
public interface IPluginAccountStopper
{
    /// <summary>Account ids RoRoRo currently tracks a live client for.</summary>
    IReadOnlyList<string> TrackedAccountIds { get; }

    /// <summary>
    /// Graceful close first, hard kill only as fallback. Returns false when the id is
    /// unparseable, the account has no tracked client, or both close and kill failed.
    /// <para>Process exit is asynchronous: a true return means the stop was issued, not that
    /// the client is gone. Callers relaunching the account should poll GetRunningAccounts
    /// until it disappears.</para>
    /// </summary>
    bool StopAccount(string accountId);
}
