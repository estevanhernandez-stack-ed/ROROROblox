namespace ROROROblox.Core.Feeds;

public enum RobloxStatusRow
{
    Website,
    Games,
    Avatar,
    GameJoin,
}

/// <summary>One watched (group id, row id) pair and the row it belongs to on screen.</summary>
public readonly record struct WatchedCell(RobloxStatusRow Row, string ComponentId, string ContainerId);

public static class RobloxStatusWatch
{
    private const string Player = "5f31ce9f6f386704befc1f81";
    private const string User = "5f31ce906e9c9104c05543d1";
    private const string Creator = "5f31cebe1bc3b004bb012c2e";

    /// <summary>
    /// Roblox's page reuses one row under several groups, so a status belongs to the pair. Ids read from
    /// the live answer on 2026-11-26.
    /// </summary>
    public static IReadOnlyList<WatchedCell> Roblox { get; } =
    [
        new(RobloxStatusRow.Website, Player, "5f31cdef5f2c7c04bbdc4fbf"),
        new(RobloxStatusRow.Website, User, "5f31cdef5f2c7c04bbdc4fbf"),
        new(RobloxStatusRow.Games, Player, "5f31ce3530ca5f04bcd3ee2c"),
        new(RobloxStatusRow.Avatar, Player, "60047c5629a6230534c2300a"),
        new(RobloxStatusRow.GameJoin, Creator, "5f31ce5767272804b51e8046"),
    ];
}
