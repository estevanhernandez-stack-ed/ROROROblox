using ROROROblox.App.ViewModels;

namespace ROROROblox.App.Plugins.Adapters;

/// <summary>
/// Snapshots <see cref="MainViewModel.Accounts"/> filtered to currently-running rows
/// for plugin consumption. <see cref="AccountSummary.RunningPid"/> is the live PID for
/// an attached <c>RobloxPlayerBeta.exe</c> (set by RobloxProcessTracker); when the PID
/// hasn't been recorded yet (race window between tracker.ProcessAttached and the
/// AccountSummary update), we fall back to 0 — plugins should treat 0 as "PID unknown".
/// </summary>
internal sealed class MainViewModelRunningAccountsAdapter : IRunningAccountsProvider
{
    private readonly MainViewModel _vm;

    public MainViewModelRunningAccountsAdapter(MainViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
    }

    public IReadOnlyList<RunningAccountSnapshot> Snapshot()
    {
        // AccountsSnapshot is the VM's lock-free point-in-time mirror — this method is hit
        // from gRPC threadpool reads, and enumerating the UI-owned Accounts collection here
        // would race a concurrent UI-thread Add/Remove into "Collection was modified",
        // failing the plugin's RPC. Per-row property reads can still tear by a frame; the
        // contract treats the result as a point-in-time snapshot, not a live view.
        var accounts = _vm.AccountsSnapshot;
        var running = new List<RunningAccountSnapshot>(accounts.Count);
        foreach (var a in accounts)
        {
            if (!a.IsRunning) continue;
            running.Add(new RunningAccountSnapshot(
                AccountId: a.Id.ToString(),
                RobloxUserId: a.RobloxUserId ?? 0,
                DisplayName: a.RenderName,
                ProcessId: a.RunningPid ?? 0));
        }
        return running;
    }
}
