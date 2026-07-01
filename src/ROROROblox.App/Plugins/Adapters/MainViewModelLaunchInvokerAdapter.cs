using System.Linq;
using System.Windows;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.App.Plugins.Adapters;

/// <summary>
/// Bridges <see cref="IPluginLaunchInvoker.RequestLaunchAsync"/> onto
/// <see cref="MainViewModel.LaunchAccountCommand"/>. v1.4 contract:
/// <list type="bullet">
///   <item><c>(true, null, 0)</c> when the launch was dispatched. PID is 0 because
///         <c>LaunchAccountAsync</c> is fire-and-forget from the command's POV — the
///         tracker raises <see cref="ROROROblox.Core.Diagnostics.IRobloxProcessTracker.ProcessAttached"/>
///         later with the real PID, which the bus forwards as <c>AccountLaunched</c>.</item>
///   <item><c>(false, "reason", 0)</c> for user-recoverable failures (account not found,
///         not eligible to launch right now).</item>
/// </list>
/// Plugins that want the actual PID subscribe to <c>SubscribeAccountLaunched</c>.
/// </summary>
internal sealed class MainViewModelLaunchInvokerAdapter : IPluginLaunchInvoker
{
    private readonly MainViewModel _vm;

    public MainViewModelLaunchInvokerAdapter(MainViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
    }

    public Task<(bool ok, string? failureReason, int processId)> RequestLaunchAsync(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return Task.FromResult<(bool, string?, int)>((false, "accountId is required.", 0));
        }
        if (!Guid.TryParse(accountId, out var id))
        {
            return Task.FromResult<(bool, string?, int)>((false, $"accountId '{accountId}' is not a valid GUID.", 0));
        }

        // AccountsSnapshot: gRPC threadpool thread — never enumerate the UI-owned collection here.
        var summary = _vm.AccountsSnapshot.FirstOrDefault(a => a.Id == id);
        if (summary is null)
        {
            return Task.FromResult<(bool, string?, int)>((false, $"No saved account with id {id}.", 0));
        }
        if (summary.SessionExpired)
        {
            return Task.FromResult<(bool, string?, int)>((false, "Account session is expired; re-add the account first.", 0));
        }
        if (summary.IsLaunching)
        {
            return Task.FromResult<(bool, string?, int)>((false, "Account is already launching.", 0));
        }
        if (summary.IsRunning)
        {
            return Task.FromResult<(bool, string?, int)>((false, "Account is already running.", 0));
        }

        // Marshal to the WPF dispatcher — LaunchAccountCommand mutates ObservableCollection
        // state on the UI thread. Application.Current is null in headless tests; fall back
        // to direct dispatch in that case.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            DispatchExecute(summary);
        }
        else
        {
            dispatcher.Invoke(() => DispatchExecute(summary));
        }
        return Task.FromResult<(bool, string?, int)>((true, null, 0));
    }

    internal static (bool ok, string? reason) ValidateLaunchTargetArgs(string accountId, string? shareUrl, long? followUserId)
    {
        if (string.IsNullOrWhiteSpace(accountId)) return (false, "accountId is required.");
        if (!Guid.TryParse(accountId, out _)) return (false, $"accountId '{accountId}' is not a valid GUID.");
        if (string.IsNullOrWhiteSpace(shareUrl) && followUserId is null) return (false, "A launch target (share_url or follow_user_id) is required.");
        return (true, null);
    }

    public async Task<(bool ok, string? failureReason, int processId)> RequestLaunchTargetAsync(
        string accountId, string? shareUrl, long? followUserId)
    {
        var (argsOk, argsReason) = ValidateLaunchTargetArgs(accountId, shareUrl, followUserId);
        if (!argsOk) return (false, argsReason, 0);

        var id = Guid.Parse(accountId);
        // AccountsSnapshot: gRPC threadpool thread — never enumerate the UI-owned collection here.
        var summary = _vm.AccountsSnapshot.FirstOrDefault(a => a.Id == id);
        if (summary is null) return (false, $"No saved account with id {id}.", 0);
        if (summary.SessionExpired) return (false, "Account session is expired; re-add the account first.", 0);
        if (summary.IsLaunching) return (false, "Account is already launching.", 0);
        if (summary.IsRunning) return (false, "Account is already running.", 0);

        LaunchTarget? target;
        if (followUserId is { } uid)
        {
            target = new LaunchTarget.FollowFriend(uid);
        }
        else
        {
            target = await _vm.ResolveShareUrlAsync(shareUrl!).ConfigureAwait(false);
            if (target is null) return (false, "Couldn't read that as a Roblox server link.", 0);
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            await _vm.LaunchAccountForPluginAsync(summary, target).ConfigureAwait(false);
        else
            await dispatcher.InvokeAsync(() => _vm.LaunchAccountForPluginAsync(summary, target)).Task.Unwrap().ConfigureAwait(false);

        return (true, null, 0); // PID arrives via SubscribeAccountLaunched
    }

    public async Task<CurrentServerInfo?> GetCurrentServerAsync()
    {
        var servers = await _vm.PrivateServerStoreForPlugin.ListAsync().ConfigureAwait(false);
        var newest = servers.Where(s => s.LastLaunchedAt is not null)
                            .OrderByDescending(s => s.LastLaunchedAt)
                            .FirstOrDefault();
        if (newest is null) return null;
        return new CurrentServerInfo(
            newest.ToShareUrl(),
            string.IsNullOrEmpty(newest.PlaceName) ? newest.RenderName : newest.PlaceName,
            newest.PlaceId,
            newest.LastLaunchedAt!.Value.ToUnixTimeMilliseconds());
    }

    private void DispatchExecute(AccountSummary summary)
    {
        if (_vm.LaunchAccountCommand.CanExecute(summary))
        {
            _vm.LaunchAccountCommand.Execute(summary);
        }
    }
}
