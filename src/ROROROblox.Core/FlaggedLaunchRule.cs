namespace ROROROblox.Core;

/// <summary>What a launch of a <c>JoinViaFriend</c> account does. Spec rule 1: a flagged account
/// never joins directly on its own; it follows the main when the main is joinable.</summary>
public enum FlaggedLaunchOutcome { Direct, Follow, MainNotJoinable, MainNotInThatServer }

public sealed record FlaggedLaunchDecision(FlaggedLaunchOutcome Outcome, LaunchTarget Target);

public static class FlaggedLaunchRule
{
    /// <summary>
    /// Decide how a launch of one account goes. Pure: every input is passed in.
    /// </summary>
    /// <param name="mainLastTarget">The main's last launch target. Presence can't tell a private
    /// server from a public one in the same place, so a <see cref="LaunchTarget.PrivateServer"/>
    /// target is followed only when the main was itself launched into that same private server
    /// (same place and code). Null, or anything else, means "not known to be in it".</param>
    public static FlaggedLaunchDecision Decide(
        bool joinViaFriend, bool isMain, LaunchTarget resolved,
        long? mainUserId, UserPresence? mainPresence, LaunchTarget? mainLastTarget = null)
    {
        if (!joinViaFriend || isMain || resolved is LaunchTarget.FollowFriend)
            return new(FlaggedLaunchOutcome.Direct, resolved);

        // Same joinable test as MainViewModel.EvaluateFollow: InGame AND a visible place.
        if (mainUserId is not { } mainId || mainPresence is not { PresenceType: UserPresenceType.InGame, PlaceId: > 0 } p)
            return new(FlaggedLaunchOutcome.MainNotJoinable, resolved);

        var follow = new LaunchTarget.FollowFriend(mainId);
        return resolved switch
        {
            LaunchTarget.GameJob gj when gj.PlaceId == p.PlaceId && gj.JobId == p.GameJobId
                => new(FlaggedLaunchOutcome.Follow, follow),
            LaunchTarget.GameJob => new(FlaggedLaunchOutcome.MainNotInThatServer, resolved),
            LaunchTarget.PrivateServer ps when ps.PlaceId == p.PlaceId
                    && mainLastTarget is LaunchTarget.PrivateServer mps
                    && mps.PlaceId == ps.PlaceId
                    && string.Equals(mps.Code, ps.Code, StringComparison.Ordinal)
                => new(FlaggedLaunchOutcome.Follow, follow),
            LaunchTarget.PrivateServer => new(FlaggedLaunchOutcome.MainNotInThatServer, resolved),
            _ => new(FlaggedLaunchOutcome.Follow, follow),
        };
    }
}
