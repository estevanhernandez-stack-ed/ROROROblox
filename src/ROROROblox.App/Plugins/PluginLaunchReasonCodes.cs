namespace ROROROblox.App.Plugins;

/// <summary>
/// Machine-readable refusal codes carried on <c>LaunchResult.reason_code</c>. A plugin can't answer
/// the flagged-launch dialog (nobody to ask), so a refused launch tells it WHY via a stable string
/// instead of forcing it to parse <c>failure_reason</c> prose.
/// </summary>
public static class PluginLaunchReasonCodes
{
    /// <summary>
    /// The account is <c>JoinViaFriend</c>-flagged and the follow target it would join through
    /// isn't currently joinable (main not in a game, main not in that server, or a saved
    /// follow-user-id target that isn't in a joinable game).
    /// </summary>
    public const string FollowTargetNotJoinable = "follow-target-not-joinable";
}
