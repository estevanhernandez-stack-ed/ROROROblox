namespace ROROROblox.App.ViewModels;

/// <summary>
/// Result of splitting an eligible batch-launch account list by
/// <see cref="AccountSummary.JoinViaFriend"/> — trust-aware squad launch (v1.9.0).
/// <see cref="Direct"/> accounts launch straight away; <see cref="Flagged"/> accounts wait for an
/// anchor (<see cref="AnchorGate"/>) and join via friend-follow instead. Both lists preserve the
/// input's relative order.
/// </summary>
internal sealed record SquadPlan(
    IReadOnlyList<AccountSummary> Direct,
    IReadOnlyList<AccountSummary> Flagged);

/// <summary>
/// Pure split of an eligible batch-launch list into its direct and flagged sub-batches. Lifted out
/// so the split is unit-testable without the heavy view model or a live launch. The VM (Task 5)
/// dispatches <see cref="SquadPlan.Direct"/> first, waits for an <see cref="AnchorGate"/> anchor,
/// then follow-dispatches <see cref="SquadPlan.Flagged"/>.
/// </summary>
internal static class SquadLaunchPlan
{
    /// <summary>
    /// Split eligible accounts: non-flagged keep their order (direct batch); flagged accounts come
    /// after (follow batch). Pure — two order-preserving passes over <paramref name="eligible"/>.
    /// </summary>
    public static SquadPlan Build(IReadOnlyList<AccountSummary> eligible)
    {
        var direct = eligible.Where(s => !s.JoinViaFriend).ToList();
        var flagged = eligible.Where(s => s.JoinViaFriend).ToList();
        return new SquadPlan(direct, flagged);
    }
}
