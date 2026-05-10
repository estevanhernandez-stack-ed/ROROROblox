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
        // ToList() copies under whatever marshalling the caller is on. Accounts is owned
        // by the UI thread; this method is hit from gRPC threadpool reads, so we're tolerant
        // of a transient inconsistency (an account flipping IsRunning mid-iteration). The
        // contract treats the result as a point-in-time snapshot, not a live view.
        var accounts = _vm.Accounts;
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
