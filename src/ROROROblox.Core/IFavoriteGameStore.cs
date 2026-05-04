namespace ROROROblox.Core;

/// <summary>
/// Persistent list of saved Roblox games. Exactly one is marked default at any time
/// (the launch target when no explicit place URL is passed). Adding the first favorite
/// auto-sets it default; removing the current default auto-promotes the next.
/// </summary>
public interface IFavoriteGameStore
{
    Task<IReadOnlyList<FavoriteGame>> ListAsync();

    /// <summary>The favorite currently marked default, or null if none saved.</summary>
    Task<FavoriteGame?> GetDefaultAsync();

    /// <summary>
    /// Add (or replace, if <paramref name="placeId"/> already exists). First favorite
    /// becomes default automatically; subsequent adds keep the existing default.
    /// </summary>
    Task<FavoriteGame> AddAsync(long placeId, long universeId, string name, string thumbnailUrl);

    Task RemoveAsync(long placeId);

    /// <summary>
    /// Make the favorite with this <paramref name="placeId"/> the default. Throws
    /// <see cref="KeyNotFoundException"/> if no such favorite exists.
    /// </summary>
    Task SetDefaultAsync(long placeId);
}
