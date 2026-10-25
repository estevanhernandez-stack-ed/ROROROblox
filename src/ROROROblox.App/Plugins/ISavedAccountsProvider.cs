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

    /// <summary>
    /// False until the saved accounts have been read at startup (#243). Before that,
    /// <see cref="Snapshot"/> is empty because nothing is loaded yet, not because there are no
    /// accounts, and answering a plugin with it reads as "no accounts". Defaults to true so a
    /// provider with nothing to load, and the test fakes, need not implement it.
    /// </summary>
    bool IsLoaded => true;

    /// <summary>Completes when <see cref="IsLoaded"/> becomes true. Already complete by default.</summary>
    Task WhenLoadedAsync(CancellationToken ct) => Task.CompletedTask;
}

public sealed record SavedAccountSnapshot(
    string AccountId,
    long RobloxUserId,
    string DisplayName,
    bool IsMain);
