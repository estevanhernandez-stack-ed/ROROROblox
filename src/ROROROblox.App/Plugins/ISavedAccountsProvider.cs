namespace ROROROblox.App.Plugins;

/// <summary>
/// Supplies a point-in-time snapshot of ALL saved accounts (running or not), including which one
/// is the main. Mirrors <see cref="IRunningAccountsProvider"/> but does not filter to running and
/// carries <c>IsMain</c>. Backs the <c>GetAccounts</c> RPC (contract 0.9.0) — the piece that lets
/// a connector resolve "Pokey" and "the main" for accounts that are not running yet, which
/// <c>GetRunningAccounts</c> by definition cannot list.
/// </summary>
public interface ISavedAccountsProvider
{
    /// <summary>Point-in-time snapshot. Callers should treat the result as immutable.</summary>
    IReadOnlyList<SavedAccountSnapshot> Snapshot();
}

public sealed record SavedAccountSnapshot(
    string AccountId,
    long RobloxUserId,
    string DisplayName,
    bool IsMain);
