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
}
