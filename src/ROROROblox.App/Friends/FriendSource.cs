namespace ROROROblox.App.Friends;

/// <summary>
/// One account the Friends picker can list friends FROM: its saved-account id, resolved Roblox
/// user id, display name, and whether it is the user's designated main. The picker's LAUNCH
/// identity (the row it was opened on) is tracked separately by the caller — a source is only
/// "whose friends you're browsing."
/// </summary>
internal sealed record FriendSource(Guid AccountId, long RobloxUserId, string DisplayName, bool IsMain);

/// <summary>
/// Pure decision for which friends-list sources the picker offers and which is shown by default.
/// </summary>
internal static class FriendSourcePlan
{
    /// <summary>
    /// Build the ordered source list for a picker opened on <paramref name="openedRow"/>. When
    /// <paramref name="main"/> is present and a DIFFERENT account than the opened row, main is placed
    /// first (index 0 = the default source) so main's friends show by default, with the opened row as
    /// the toggle alternate. When main is null or IS the opened row, the picker is single-source.
    /// </summary>
    public static (IReadOnlyList<FriendSource> Sources, int DefaultIndex) Build(
        FriendSource openedRow, FriendSource? main)
    {
        if (main is null || main.AccountId == openedRow.AccountId)
        {
            return ([openedRow], 0);
        }
        return ([main, openedRow], 0);
    }
}
