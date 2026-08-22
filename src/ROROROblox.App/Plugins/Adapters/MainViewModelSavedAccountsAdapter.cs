using ROROROblox.App.ViewModels;

namespace ROROROblox.App.Plugins.Adapters;

/// <summary>
/// Bridges the app's account list (ALL saved accounts, running or not) to the plugin host's
/// <see cref="ISavedAccountsProvider"/>. Reads the lock-free
/// <see cref="MainViewModel.AccountsSnapshot"/> mirror — same threading reasoning as
/// <see cref="MainViewModelRunningAccountsAdapter"/>, whose doc records why the UI-owned
/// collection must never be enumerated from a gRPC thread — with no running filter, and carries
/// <c>IsMain</c> so a connector can resolve "the main" and launch not-yet-running alts.
/// <para>
/// <c>DisplayName</c> maps <see cref="AccountSummary.RenderName"/>, deliberately matching the
/// running-accounts adapter: while streamer mode is active, plugins see the same masked names the
/// app shows. A connector resolving names during a stream resolves the masked ones — that is the
/// privacy feature working, not a bug.
/// </para>
/// </summary>
internal sealed class MainViewModelSavedAccountsAdapter : ISavedAccountsProvider
{
    private readonly MainViewModel _vm;

    public MainViewModelSavedAccountsAdapter(MainViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
    }

    public IReadOnlyList<SavedAccountSnapshot> Snapshot()
    {
        var accounts = _vm.AccountsSnapshot;
        var saved = new List<SavedAccountSnapshot>(accounts.Count);
        foreach (var a in accounts)
        {
            saved.Add(new SavedAccountSnapshot(
                AccountId: a.Id.ToString(),
                RobloxUserId: a.RobloxUserId ?? 0,
                DisplayName: a.RenderName,
                IsMain: a.IsMain));
        }
        return saved;
    }
}
