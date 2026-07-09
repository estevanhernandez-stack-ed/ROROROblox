namespace ROROROblox.Core;

/// <summary>
/// Persistent list of saved private (VIP) servers. Cross-account — see
/// <see cref="SavedPrivateServer"/> remarks. The Squad Launch surface picks one and
/// dispatches every eligible account into it.
/// </summary>
public interface IPrivateServerStore
{
    Task<IReadOnlyList<SavedPrivateServer>> ListAsync();

    Task<SavedPrivateServer?> GetAsync(Guid id);

    /// <summary>
    /// Add or replace by (placeId, code) pair. If a server with the same pair already exists,
    /// its <see cref="SavedPrivateServer.Id"/> + <see cref="SavedPrivateServer.AddedAt"/> are
    /// preserved and the rest of the fields are updated. <paramref name="codeKind"/> tells the
    /// launcher which Roblox query slot the code goes in — pasting a share URL produces
    /// <see cref="PrivateServerCodeKind.LinkCode"/>; pasting a browser-resolved launcher URI
    /// produces <see cref="PrivateServerCodeKind.AccessCode"/>.
    /// </summary>
    Task<SavedPrivateServer> AddAsync(
        long placeId,
        string code,
        PrivateServerCodeKind codeKind,
        string name,
        string placeName,
        string thumbnailUrl);

    Task RemoveAsync(Guid id);

    /// <summary>Stamp <see cref="SavedPrivateServer.LastLaunchedAt"/> = now (used for sort order).</summary>
    Task TouchLastLaunchedAsync(Guid id);

    /// <summary>
    /// Set the per-user local nickname override. <paramref name="localName"/> is normalized:
    /// null / empty / whitespace all collapse to <c>null</c> (effective reset). The Roblox-side
    /// <see cref="SavedPrivateServer.Name"/> is never touched. Throws
    /// <see cref="KeyNotFoundException"/> if no server has the given <paramref name="serverId"/>.
    /// v1.3.x.
    /// </summary>
    Task UpdateLocalNameAsync(Guid serverId, string? localName);

    /// <summary>
    /// Mark this server the default (clears the flag on every other server — at most one
    /// default). Throws <see cref="KeyNotFoundException"/> if no server has this id. No-op
    /// (no write, no event) when it's already the default.
    /// </summary>
    Task SetDefaultAsync(Guid id);

    /// <summary>
    /// Clear the default flag on every server, returning to the zero-default state. No-op
    /// (no write, no event) when nothing is default. Zero-default is legal: Squad Launch
    /// falls back to manual pick.
    /// </summary>
    Task ClearDefaultAsync();

    /// <summary>
    /// Fired after <see cref="SetDefaultAsync"/> / <see cref="ClearDefaultAsync"/> (or a
    /// default-removing <see cref="RemoveAsync"/>) mutates state and persists. Fired outside
    /// the store gate so subscribers can re-enter the store without deadlocking. Mirrors
    /// <see cref="IFavoriteGameStore.DefaultChanged"/>.
    /// </summary>
    event EventHandler? DefaultChanged;
}
