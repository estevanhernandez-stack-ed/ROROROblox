namespace ROROROblox.App.Plugins;

/// <summary>
/// Read-only snapshot of currently running Roblox accounts as exposed to plugins via
/// GetRunningAccounts. Implemented by the App layer over the live AccountManager /
/// process map so PluginHostService stays decoupled from WPF lifetime concerns.
/// </summary>
public interface IRunningAccountsProvider
{
    /// <summary>Point-in-time snapshot. Callers should treat the result as immutable.</summary>
    IReadOnlyList<RunningAccountSnapshot> Snapshot();
}

/// <summary>
/// Plain-data record passed across the PluginHostService boundary. Maps 1:1 onto the
/// proto-generated RunningAccount message — kept as a separate type so the App layer
/// has no compile-time dependency on a specific proto runtime version.
/// </summary>
public sealed record RunningAccountSnapshot(
    string AccountId,
    long RobloxUserId,
    string DisplayName,
    int ProcessId);
