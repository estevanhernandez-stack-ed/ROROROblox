namespace ROROROblox.App.ViewModels;

/// <summary>
/// What the flagged-launch dialog is asked: one or more <c>JoinViaFriend</c> accounts that can't
/// follow the main right now (spec rule 1), the main's name, and the other saved accounts that are
/// in a game the flagged ones could follow instead.
/// </summary>
internal sealed record FlaggedLaunchAsk(IReadOnlyList<string> AccountNames, string? MainName,
    IReadOnlyList<(string Name, long UserId)> JoinableOthers);

/// <summary>The answer to a <see cref="FlaggedLaunchAsk"/>. A closed dialog is <see cref="Cancel"/>.</summary>
internal abstract record FlaggedLaunchChoice
{
    /// <summary>Follow this saved account into its server instead of the main.</summary>
    internal sealed record FollowAccount(long UserId) : FlaggedLaunchChoice;

    /// <summary>"It's fixed": clear the account's flag and join the resolved target directly.</summary>
    internal sealed record JoinDirectly : FlaggedLaunchChoice;

    /// <summary>Launch nothing.</summary>
    internal sealed record Cancel : FlaggedLaunchChoice;
}

/// <summary>
/// What a launch path does when a flagged account can't follow the main. <see cref="Ask"/> puts
/// the question to the person; <see cref="Refuse"/> launches nothing and asks nobody, for paths
/// with no one to ask (plugins, auto-rejoin).
/// </summary>
internal enum FlaggedLaunchMode { Ask, Refuse }
