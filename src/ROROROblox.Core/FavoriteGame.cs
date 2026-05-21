namespace ROROROblox.Core;

/// <summary>
/// One Roblox game saved by the user. <see cref="IsDefault"/> marks the one Launch As uses
/// when no explicit place URL is passed. Persisted as JSON in
/// <c>%LOCALAPPDATA%\ROROROblox\favorites.json</c>; not secret, no DPAPI envelope.
/// </summary>
/// <remarks>
/// <see cref="LocalName"/> is a per-user nickname override that wins over <see cref="Name"/>
/// at every render surface. Roblox-side name refreshes never touch <see cref="LocalName"/>;
/// re-adding the same <see cref="PlaceId"/> preserves it. v1.3.x.
/// </remarks>
public sealed record FavoriteGame(
    long PlaceId,
    long UniverseId,
    string Name,
    string ThumbnailUrl,
    bool IsDefault,
    DateTimeOffset AddedAt,
    string? LocalName = null,
    string? PrivateServerCode = null,
    PrivateServerCodeKind? PrivateServerCodeKind = null,
    Guid? PrivateServerId = null)
{
    /// <summary>
    /// What the UI should show wherever it used to show <see cref="Name"/>. v1.3.x.
    /// PriorityBinding doesn't fall through on null (it treats null as a successful value), so
    /// XAML binds <c>Path=RenderName</c> instead of two-binding gymnastics.
    /// </summary>
    public string RenderName => string.IsNullOrEmpty(LocalName) ? Name : LocalName;

    /// <summary>
    /// True when this entry stands in for a <see cref="SavedPrivateServer"/> in the per-account
    /// dropdown rather than a plain favorite game. v1.6.0. A normal game and the JoinByLink
    /// sentinel both leave <see cref="PrivateServerCode"/> null. Selecting a PS entry makes the
    /// row launch into <see cref="LaunchTarget.PrivateServer"/>; the launch precedence in
    /// <c>MainViewModel.ResolveLaunchTarget</c> branches on this before the plain Place case.
    /// </summary>
    public bool IsPrivateServer => !string.IsNullOrEmpty(PrivateServerCode);

    /// <summary>
    /// Dropdown label for the per-account picker. Appends a quiet "(private server)" suffix to
    /// PS entries so they're distinguishable from games at a glance, without polluting
    /// <see cref="RenderName"/> (which the rename feature reads + writes). v1.6.0.
    /// </summary>
    public string DropdownLabel => IsPrivateServer ? $"{RenderName} (private server)" : RenderName;
}
