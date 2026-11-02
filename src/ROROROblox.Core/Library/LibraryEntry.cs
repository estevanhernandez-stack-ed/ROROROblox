namespace ROROROblox.Core;

/// <summary>Which of the three sources a <see cref="LibraryEntry"/> came from. Declaration order is display order.</summary>
public enum LibraryEntryKind
{
    /// <summary>A saved game (<c>favorites.json</c>).</summary>
    Favourite,

    /// <summary>A saved private server (<c>private-servers.json</c>).</summary>
    PrivateServer,

    /// <summary>A place played from RoRoRo by any account that is not saved (the rollup's games).</summary>
    Recent,
}

/// <summary>
/// One row of the Library: the join of favourites, saved servers and Recent. Data only, no prose:
/// the view decides how to word an absent <see cref="LastPlayedUtc"/> or <see cref="IconUrl"/>.
/// </summary>
/// <param name="Kind">Source of the entry.</param>
/// <param name="PlaceId">The Roblox place.</param>
/// <param name="PrivateServerId">The saved server's id, for <see cref="LibraryEntryKind.PrivateServer"/> only.</param>
/// <param name="Name">The Roblox-side name (or the last known name for Recent; the place id as text when nothing is known).</param>
/// <param name="LocalName">The user's rename, if any.</param>
/// <param name="IconUrl">From the store's thumbnail for saved entries, else the icon cache's disk state; null on a miss. Never fetched by the join.</param>
/// <param name="IsDefault">The entry the Launch button uses.</param>
/// <param name="LastPlayedUtc">When RoRoRo last launched it, any account. Null if never recorded.</param>
/// <param name="TotalPlayed">All-time uptime for a game; for a private server, the sum over the surviving history rows. Null if none.</param>
/// <param name="Target">What launching this entry launches.</param>
public sealed record LibraryEntry(
    LibraryEntryKind Kind,
    long PlaceId,
    Guid? PrivateServerId,
    string Name,
    string? LocalName,
    string? IconUrl,
    bool IsDefault,
    DateTimeOffset? LastPlayedUtc,
    TimeSpan? TotalPlayed,
    LaunchTarget Target)
{
    /// <summary>The name to show: the rename when there is one.</summary>
    public string RenderName => string.IsNullOrEmpty(LocalName) ? Name : LocalName;
}
