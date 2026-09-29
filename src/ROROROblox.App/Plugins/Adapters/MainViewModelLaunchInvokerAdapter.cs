using System.Linq;
using System.Windows;
using ROROROblox.App.Plugins;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.App.Plugins.Adapters;

/// <summary>
/// Bridges <see cref="IPluginLaunchInvoker.RequestLaunchAsync"/> onto
/// <see cref="MainViewModel.LaunchAccountForPluginAsync"/>. Every outcome — Direct and Follow
/// alike — routes through that seam, never <see cref="MainViewModel.LaunchAccountCommand"/>:
/// the command's own <c>LaunchAccountAsync</c> call re-decides the flagged-launch outcome from
/// whatever state is current when it actually runs (in <c>Ask</c> mode), so a Direct decision
/// made here could still open <see cref="MainViewModel.FlaggedLaunchPrompt"/> later if
/// <c>JoinViaFriend</c> or the main's presence changed in between. Routing Direct through the
/// same Refuse-mode seam as Follow closes that window structurally (see the review-round-1 note
/// inline in <see cref="RequestLaunchAsync"/>). v1.4 contract:
/// <list type="bullet">
///   <item><c>(true, null, 0, null)</c> when the launch was dispatched. PID is 0 because
///         <c>LaunchAccountAsync</c> is fire-and-forget from this seam's POV — the
///         tracker raises <see cref="ROROROblox.Core.Diagnostics.IRobloxProcessTracker.ProcessAttached"/>
///         later with the real PID, which the bus forwards as <c>AccountLaunched</c>.</item>
///   <item><c>(false, "reason", 0, null)</c> for user-recoverable failures (account not found,
///         not eligible to launch right now).</item>
///   <item><c>(false, "reason", 0, reasonCode)</c> for a refused <c>JoinViaFriend</c> launch or
///         follow target (see <see cref="PluginLaunchReasonCodes"/>) — a plugin can't answer the
///         flagged-launch dialog, so <see cref="MainViewModel.FlaggedLaunchPrompt"/> is never
///         reached from this adapter; a flagged account follows the main when joinable, or the
///         launch refuses with a stable code the plugin can branch on.</item>
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

    public Task<(bool ok, string? failureReason, int processId, string? reasonCode)> RequestLaunchAsync(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return Task.FromResult<(bool, string?, int, string?)>((false, "accountId is required.", 0, null));
        }
        if (!Guid.TryParse(accountId, out var id))
        {
            return Task.FromResult<(bool, string?, int, string?)>((false, $"accountId '{accountId}' is not a valid GUID.", 0, null));
        }

        // AccountsSnapshot: gRPC threadpool thread — never enumerate the UI-owned collection here.
        var summary = _vm.AccountsSnapshot.FirstOrDefault(a => a.Id == id);
        if (summary is null)
        {
            return Task.FromResult<(bool, string?, int, string?)>((false, $"No saved account with id {id}.", 0, null));
        }
        if (summary.SessionExpired)
        {
            return Task.FromResult<(bool, string?, int, string?)>((false, "Account session is expired; re-add the account first.", 0, null));
        }
        if (summary.IsLaunching)
        {
            return Task.FromResult<(bool, string?, int, string?)>((false, "Account is already launching.", 0, null));
        }
        if (summary.IsRunning)
        {
            return Task.FromResult<(bool, string?, int, string?)>((false, "Account is already running.", 0, null));
        }

        // Spec rule 1, plugin surface: a plugin can't answer the flagged-launch dialog, so it never
        // reaches FlaggedLaunchPrompt. A flagged account either follows the main (Follow) or the
        // launch is refused with a machine-readable reason code — never a silent direct join.
        var resolvedTarget = MainViewModel.ResolveLaunchTarget(summary.SelectedGame, null);
        var decision = _vm.DecideFlaggedLaunch(summary, resolvedTarget);
        if (decision.Outcome is FlaggedLaunchOutcome.MainNotJoinable or FlaggedLaunchOutcome.MainNotInThatServer)
        {
            return Task.FromResult<(bool, string?, int, string?)>(
                (false, "This account joins through your main, and your main isn't in a joinable game.", 0, PluginLaunchReasonCodes.FollowTargetNotJoinable));
        }

        // Review round 1: Direct used to route through LaunchAccountCommand -> LaunchAccountAsync
        // in Ask mode, which RE-DECIDES the flagged-launch outcome from whatever state is current
        // when it actually runs — not the state the decision above was made from. If JoinViaFriend
        // flipped, or the main's presence changed, between this decision and that one, a Direct
        // decision made HERE could still open FlaggedLaunchPrompt once the command's own
        // re-decision came out MainNotJoinable/MainNotInThatServer. Routing Direct through the
        // SAME LaunchAccountForPluginAsync seam as Follow closes that window structurally:
        // whatever LaunchAccountAsync re-decides, it still runs in Refuse mode (see
        // MainViewModel.LaunchAccountForPluginAsync), so it can only follow or silently decline —
        // it can never reach FlaggedLaunchPrompt. LaunchAccountCommand's CanExecute carried no
        // meaning here to preserve: it's constructed with no canExecute predicate
        // (MainViewModel.cs, LaunchAccountCommand assignment), so RelayCommand.CanExecute is
        // unconditionally true — nothing is lost by no longer routing through it.
        var launchTarget = decision.Outcome == FlaggedLaunchOutcome.Follow ? decision.Target : resolvedTarget;

        // Marshal to the WPF dispatcher — the plugin-host launch seam mutates ObservableCollection
        // state on the UI thread. Application.Current is null in headless tests; fall back to
        // direct dispatch in that case.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            DispatchLaunch(summary, launchTarget);
        }
        else
        {
            dispatcher.InvokeAsync(() => DispatchLaunch(summary, launchTarget));
        }
        return Task.FromResult<(bool, string?, int, string?)>((true, null, 0, null));
    }

    internal static (bool ok, string? reason) ValidateLaunchTargetArgs(string accountId, string? shareUrl, long? followUserId)
    {
        if (string.IsNullOrWhiteSpace(accountId)) return (false, "accountId is required.");
        if (!Guid.TryParse(accountId, out _)) return (false, $"accountId '{accountId}' is not a valid GUID.");
        if (string.IsNullOrWhiteSpace(shareUrl) && followUserId is null) return (false, "A launch target (share_url or follow_user_id) is required.");
        return (true, null);
    }

    public async Task<(bool ok, string? failureReason, int processId, string? reasonCode)> RequestLaunchTargetAsync(
        string accountId, string? shareUrl, long? followUserId)
    {
        var (argsOk, argsReason) = ValidateLaunchTargetArgs(accountId, shareUrl, followUserId);
        if (!argsOk) return (false, argsReason, 0, null);

        var id = Guid.Parse(accountId);
        // AccountsSnapshot: gRPC threadpool thread — never enumerate the UI-owned collection here.
        var summary = _vm.AccountsSnapshot.FirstOrDefault(a => a.Id == id);
        if (summary is null) return (false, $"No saved account with id {id}.", 0, null);
        if (summary.SessionExpired) return (false, "Account session is expired; re-add the account first.", 0, null);
        if (summary.IsLaunching) return (false, "Account is already launching.", 0, null);
        if (summary.IsRunning) return (false, "Account is already running.", 0, null);

        LaunchTarget target;
        if (followUserId is { } uid)
        {
            // If the follow target IS a saved account, guard it the same way the Friends-modal
            // follow path does (EvaluateFollow) — following an account that isn't in a joinable
            // game bounces to the Roblox home page instead of silently failing. A user id that
            // isn't a saved account passes through unchanged: its presence can't be read here.
            var followedRow = _vm.AccountsSnapshot.FirstOrDefault(a => a.RobloxUserId == uid);
            if (followedRow is not null)
            {
                var followDecision = MainViewModel.EvaluateFollow(MainViewModel.ProjectPresence(followedRow), followedRow.RenderName);
                if (!followDecision.CanFollow)
                {
                    return (false, "That account isn't in a joinable game.", 0, PluginLaunchReasonCodes.FollowTargetNotJoinable);
                }
            }
            target = new LaunchTarget.FollowFriend(uid);
        }
        else
        {
            var resolved = await _vm.ResolveShareUrlAsync(shareUrl!).ConfigureAwait(false);
            if (resolved is null) return (false, "Couldn't read that as a Roblox server link.", 0, null);

            // Spec rule 1, plugin surface: a share-url launch still has to respect the launching
            // account's JoinViaFriend flag. No FlaggedLaunchPrompt reachable from here either.
            var decision = _vm.DecideFlaggedLaunch(summary, resolved);
            if (decision.Outcome is FlaggedLaunchOutcome.MainNotJoinable or FlaggedLaunchOutcome.MainNotInThatServer)
            {
                return (false, "This account joins through your main, and your main isn't in a joinable game.", 0, PluginLaunchReasonCodes.FollowTargetNotJoinable);
            }
            target = decision.Outcome == FlaggedLaunchOutcome.Follow ? decision.Target : resolved;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            await _vm.LaunchAccountForPluginAsync(summary, target).ConfigureAwait(false);
        else
            await dispatcher.InvokeAsync(() => _vm.LaunchAccountForPluginAsync(summary, target)).Task.Unwrap().ConfigureAwait(false);

        return (true, null, 0, null); // PID arrives via SubscribeAccountLaunched
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

    /// <summary>
    /// Fire-and-forget: the caller gets <c>(true, null, 0, null)</c> once this is dispatched, and
    /// the real PID arrives later via <c>SubscribeAccountLaunched</c>. Used for every
    /// <see cref="RequestLaunchAsync"/> outcome (Direct and Follow alike) — always through
    /// <see cref="MainViewModel.LaunchAccountForPluginAsync"/>, never
    /// <see cref="MainViewModel.LaunchAccountCommand"/>, so <see cref="MainViewModel.FlaggedLaunchPrompt"/>
    /// is structurally unreachable from this adapter.
    /// </summary>
    private void DispatchLaunch(AccountSummary summary, LaunchTarget target)
    {
        _ = _vm.LaunchAccountForPluginAsync(summary, target);
    }
}
