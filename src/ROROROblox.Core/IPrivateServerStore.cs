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
    /// Add or replace by (placeId, accessCode) pair. If a server with the same pair already
    /// exists, its <see cref="SavedPrivateServer.Id"/> + <see cref="SavedPrivateServer.AddedAt"/>
    /// are preserved and the rest of the fields are updated.
    /// </summary>
    Task<SavedPrivateServer> AddAsync(long placeId, string accessCode, string name, string placeName, string thumbnailUrl);

    Task RemoveAsync(Guid id);

    /// <summary>Stamp <see cref="SavedPrivateServer.LastLaunchedAt"/> = now (used for sort order).</summary>
    Task TouchLastLaunchedAsync(Guid id);
}
