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

    /// <summary>
    /// RoRoRo is still loading saved accounts at startup and did not finish within the wait (#243).
    /// Retryable: the account is probably there, the list just is not read yet. Also carried as the
    /// prefix of <c>GetAccounts</c>' <c>Unavailable</c> status message, which has no reason field.
    /// </summary>
    public const string AccountsLoading = "accounts-loading";

    /// <summary>
    /// The launch target was malformed and was refused before any launch: a <c>GameInstance</c>
    /// with a place id that is not positive, or a job id that is empty or not a GUID (Roblox
    /// server-list ids are GUIDs, and the value lands in a URL). A half pair is never degraded to
    /// "any server": that is silent matchmaking, which #247 forbids.
    /// </summary>
    public const string InvalidTarget = "invalid-target";
}
