namespace ROROROblox.Core;

/// <summary>
/// One Roblox private (VIP) server bookmarked by the user. Distinct from
/// <see cref="LaunchTarget.PrivateServer"/> — that's the launch intent;
/// <see cref="SavedPrivateServer"/> is the persisted record (with name, place name, thumbnail,
/// timestamps).
/// </summary>
/// <remarks>
/// <para>
/// Stored cross-account, NOT per-account. Roblox private servers are scoped to a place +
/// link code; any signed-in user with the link can join. Per-account "stick to this server"
/// behavior is a preference layer, not an ownership boundary.
/// </para>
/// <para>
/// Persisted as plaintext JSON in <c>%LOCALAPPDATA%\ROROROblox\private-servers.json</c>.
/// A private server link is a soft credential — anyone with it can join — but it isn't a
/// password and doesn't unlock the account. Same trust class as a bookmark; same precedent
/// Bloxstrap and earlier multi-account tools use.
/// </para>
/// </remarks>
public sealed record SavedPrivateServer(
    Guid Id,
    long PlaceId,
    string Code,
    PrivateServerCodeKind CodeKind,
    string Name,
    string PlaceName,
    string ThumbnailUrl,
    DateTimeOffset AddedAt,
    DateTimeOffset? LastLaunchedAt,
    string? LocalName = null)
{
    /// <summary>
    /// Back-compat shim for older storage blobs that persisted only an <c>accessCode</c> field
    /// before the kind discriminator landed. The previous app build emitted both
    /// <c>accessCode=</c> and <c>linkCode=</c> in the launcher URI, but only share-URL paste
    /// flow (which is what users actually used) wrote rows here — meaning legacy values are
    /// almost always link codes. Default to <see cref="PrivateServerCodeKind.LinkCode"/> when
    /// loading old records.
    /// </summary>
    public const PrivateServerCodeKind DefaultLegacyKind = PrivateServerCodeKind.LinkCode;

    /// <summary>
    /// What the UI should show wherever it used to show <see cref="Name"/>. v1.3.x.
    /// </summary>
    public string RenderName => string.IsNullOrEmpty(LocalName) ? Name : LocalName;
}
