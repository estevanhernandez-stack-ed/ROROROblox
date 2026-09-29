namespace ROROROblox.Core;

/// <summary>What a launch of a <c>JoinViaFriend</c> account does. Spec rule 1: a flagged account
/// never joins directly on its own; it follows the main when the main is joinable.</summary>
public enum FlaggedLaunchOutcome { Direct, Follow, MainNotJoinable, MainNotInThatServer }

public sealed record FlaggedLaunchDecision(FlaggedLaunchOutcome Outcome, LaunchTarget Target);

public static class FlaggedLaunchRule
{
    public static FlaggedLaunchDecision Decide(
        bool joinViaFriend, bool isMain, LaunchTarget resolved,
        long? mainUserId, UserPresence? mainPresence)
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
                => new(FlaggedLaunchOutcome.Follow, follow),
            LaunchTarget.PrivateServer => new(FlaggedLaunchOutcome.MainNotInThatServer, resolved),
            _ => new(FlaggedLaunchOutcome.Follow, follow),
        };
    }
}
