namespace ROROROblox.App.ViewModels;

/// <summary>
/// Pure gating logic for the v1.9.0 trust-aware squad launch's anchor wait. Once
/// <see cref="SquadLaunchPlan.Build"/>'s direct batch is dispatched, we wait for an anchor — a
/// landed direct-batch account with a known Roblox userId (<c>FollowFriend</c> needs a target id)
/// — before follow-dispatching the flagged batch. Lifted out of <see cref="MainViewModel"/> so the
/// predicates are unit-testable without the heavy view model or a live process / network. The VM
/// owns the actual timing/await loop (verify-by-running), same shape as <see cref="PreWarmGate"/>.
/// </summary>
internal static class AnchorGate
{
    /// <summary>
    /// The hard upper bound on the anchor wait. On hitting the cap the VM proceeds best-effort
    /// (never hangs forever waiting for an anchor that never lands).
    /// </summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(90);

    /// <summary>
    /// An account can anchor a friend-follow when it is in the game AND its Roblox userId is known
    /// (<c>FollowFriend</c> needs a target id) — a landed account with an unresolved userId can't
    /// anchor.
    /// </summary>
    public static bool CanAnchor(bool inGame, long? robloxUserId) => inGame && robloxUserId is not null;

    /// <summary>
    /// Pick the first anchor-capable account in the direct batch, or <see langword="null"/> when none
    /// qualify yet.
    /// </summary>
    public static AccountSummary? PickAnchor(IReadOnlyList<AccountSummary> directBatch) =>
        directBatch.FirstOrDefault(s => CanAnchor(s.InGame, s.RobloxUserId));

    /// <summary>
    /// True once the anchor wait is done: the picked anchor has landed in game. Mirrors
    /// <see cref="PreWarmGate.PreWarmWaitComplete"/>'s shape for the single-signal careful-mode wait.
    /// </summary>
    public static bool WaitComplete(bool inGame) => inGame;

    /// <summary>
    /// True once <paramref name="utcNow"/> is at or past <paramref name="deadline"/> — the VM should
    /// stop waiting for an anchor and proceed best-effort. Inclusive at the exact deadline, matching
    /// <c>RobloxProcessTracker</c>'s <c>now &gt;= deadline</c> convention.
    /// </summary>
    public static bool WaitExpired(DateTime utcNow, DateTime deadline) => utcNow >= deadline;
}
