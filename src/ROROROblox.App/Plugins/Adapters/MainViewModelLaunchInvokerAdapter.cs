using System.Linq;
using Microsoft.Extensions.Logging;
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
/// <para>
/// <b>Fix round 2.</b> <see cref="RequestLaunchAsync"/>'s Follow/Direct dispatch marshals through
/// <see cref="MainViewModel.UiDispatcher"/> (blocking), not <c>Application.Current?.Dispatcher</c>
/// directly (non-blocking when marshaling across threads). See the inline note on that method.
/// </para>
/// </summary>
internal sealed class MainViewModelLaunchInvokerAdapter : IPluginLaunchInvoker
{
    private readonly MainViewModel _vm;
    private readonly ILogger<MainViewModelLaunchInvokerAdapter>? _log;

    public MainViewModelLaunchInvokerAdapter(MainViewModel vm, ILogger<MainViewModelLaunchInvokerAdapter>? log = null)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        _log = log;
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

        // Fix round 2: marshal through MainViewModel's own injected IUiDispatcher, not
        // Application.Current?.Dispatcher directly. The two are NOT equivalent under full-suite
        // load: Application.Current can be non-null and owned by another (STA) thread here (other
        // tests construct a real App), so dispatcher.CheckAccess() is false and the old code fell
        // into dispatcher.InvokeAsync(...) — which QUEUES the delegate and returns immediately,
        // fire-and-forget, with no wait for it to even start. That let this method return
        // (true, ...) before LaunchAccountAsync had run its first line (summary.IsLaunching = true),
        // so a caller awaiting this call could observe IsLaunching still false. _vm.UiDispatcher
        // (WpfUiDispatcher in production) calls the BLOCKING Dispatcher.Invoke, which only returns
        // once the marshaled action has actually run — and runs directly, inline, when there is no
        // dispatcher at all (headless tests), so this is correct in every mode. By the time Invoke
        // returns, DispatchLaunch has already started LaunchAccountAsync synchronously up to its
        // first await, so IsLaunching is guaranteed true before this method hands back its result.
        _vm.UiDispatcher.Invoke(() => DispatchLaunch(summary, launchTarget));
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

        // Same marshal as RequestLaunchAsync: the view model's own IUiDispatcher, a blocking Invoke
        // that runs inline when there is no dispatcher or we are already on it. It starts the launch
        // on the UI thread (up to its first await) and hands the task back, which is then awaited
        // here, so this call still returns only once the launch has run.
        Task launch = Task.CompletedTask;
        _vm.UiDispatcher.Invoke(() => launch = _vm.LaunchAccountForPluginAsync(summary, target));
        await launch.ConfigureAwait(false);

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
    /// Fire-and-forget WITH RESPECT TO COMPLETION: the caller gets <c>(true, null, 0, null)</c>
    /// once this is dispatched, and the real PID arrives later via <c>SubscribeAccountLaunched</c>.
    /// It is not fire-and-forget with respect to STARTING — called via <c>_vm.UiDispatcher.Invoke</c>
    /// (a blocking marshal), so by the time that call returns, <c>LaunchAccountForPluginAsync</c>
    /// has already run synchronously up to its first await, which is exactly where
    /// <c>summary.IsLaunching</c> is set. Used for every <see cref="RequestLaunchAsync"/> outcome
    /// (Direct and Follow alike) — always through
    /// <see cref="MainViewModel.LaunchAccountForPluginAsync"/>, never
    /// <see cref="MainViewModel.LaunchAccountCommand"/>, so <see cref="MainViewModel.FlaggedLaunchPrompt"/>
    /// is structurally unreachable from this adapter.
    /// <para>
    /// The launch task itself is still discarded (its completion is genuinely fire-and-forget —
    /// the plugin contract returns pid 0 up front), but a fault on it is not left unobserved: the
    /// continuation below logs it, following the same shape as <c>App.StartPluginHostListener</c>'s
    /// <c>_pluginHostListening.ContinueWith(...)</c>.
    /// </para>
    /// </summary>
    private void DispatchLaunch(AccountSummary summary, LaunchTarget target)
    {
        var launch = _vm.LaunchAccountForPluginAsync(summary, target);
        _ = launch.ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                _log?.LogWarning(t.Exception, "Plugin-initiated launch for account {AccountId} faulted.", summary.Id);
            }
        }, TaskScheduler.Default);
    }
}
