namespace ROROROblox.Core;

/// <summary>
/// Entity-agnostic shape for the rename overlay. <c>MainViewModel</c>'s
/// <c>RenameItemCommand</c> takes one of these and switches on <see cref="Kind"/> to
/// dispatch to the right store. v1.3.x. Spec §5.3.
/// </summary>
/// <remarks>
/// Lives in Core (not App) so the test project can construct + assert against it without
/// pulling in WPF dependencies. Pure data — zero UI couplings. (Banner-correct against
/// spec §5.3, which originally placed it in App; the move costs nothing and gains test
/// reachability.)
/// </remarks>
/// <param name="Kind">Discriminator for the dispatch switch.</param>
/// <param name="Id">
/// <see cref="long"/> for <see cref="RenameTargetKind.Game"/> (placeId);
/// <see cref="System.Guid"/> for <see cref="RenameTargetKind.PrivateServer"/>
/// and <see cref="RenameTargetKind.Account"/>. Boxed at this layer; unboxed by the
/// switch at dispatch time.
/// </param>
/// <param name="OriginalName">
/// The Roblox-side value — <c>Name</c> for game/server, <c>DisplayName</c> for account.
/// Shown to the user as a mono-micro reference line so the rename popup never hides
/// the underlying name.
/// </param>
/// <param name="CurrentLocalName">
/// The current local override, or <see langword="null"/> if none. Drives the
/// pre-fill on <c>RenameWindow</c> and the visibility of the "Reset to original" link.
/// </param>
public sealed record RenameTarget(
    RenameTargetKind Kind,
    object Id,
    string OriginalName,
    string? CurrentLocalName);

/// <summary>
/// Three entity kinds the rename overlay supports. v1.3.x.
/// </summary>
public enum RenameTargetKind
{
    Game,
    PrivateServer,
    Account,
}
