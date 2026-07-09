namespace ROROROblox.Core;

/// <summary>
/// Persistent list of saved Roblox games. Zero or one is marked default
/// (the launch target when no explicit place URL is passed). Adding the first favorite
/// auto-sets it default; removing the current default leaves no default; launches open Roblox home until you set one.
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

    /// <summary>
    /// Clear the default flag on every game, returning to the zero-default state. No-op (no write,
    /// no event) when nothing is default. Zero-default is legal: Launch As opens Roblox home.
    /// </summary>
    Task ClearDefaultAsync();

    /// <summary>
    /// Set the per-user local nickname override. <paramref name="localName"/> is normalized:
    /// null / empty / whitespace all collapse to <c>null</c> (effective reset). The Roblox-side
    /// <see cref="FavoriteGame.Name"/> is never touched. Throws <see cref="KeyNotFoundException"/>
    /// if no favorite has the given <paramref name="placeId"/>. v1.3.x.
    /// </summary>
    Task UpdateLocalNameAsync(long placeId, string? localName);

    /// <summary>
    /// Fired after <see cref="SetDefaultAsync"/> mutates state and persists. Lets the
    /// default-game widget react without a manual re-fetch. Subscribers should expect to be
    /// invoked on whatever thread <see cref="SetDefaultAsync"/> ran on. v1.3.x.
    /// </summary>
    event EventHandler? DefaultChanged;
}
