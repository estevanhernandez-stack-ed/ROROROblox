namespace ROROROblox.Core;

/// <summary>
/// One-shot dispatch helper that routes a <see cref="RenameTarget"/> to the right store's
/// <c>UpdateLocalNameAsync</c>. Lives in Core (not the App ViewModel) so the dispatch logic
/// is unit-testable without WPF. v1.3.x. Spec §6.2 + §6.3.
/// </summary>
public static class RenameDispatch
{
    /// <summary>
    /// Apply <paramref name="newLocalName"/> to the entity identified by <paramref name="target"/>.
    /// Switches on <see cref="RenameTarget.Kind"/>. Each store's <c>UpdateLocalNameAsync</c>
    /// normalizes empty/whitespace to null and throws <see cref="KeyNotFoundException"/> on
    /// missing IDs — those exceptions surface here unchanged. v1.3.x.
    /// </summary>
    public static Task ApplyAsync(
        IFavoriteGameStore favoriteGames,
        IPrivateServerStore privateServers,
        IAccountStore accounts,
        RenameTarget target,
        string? newLocalName)
    {
        ArgumentNullException.ThrowIfNull(favoriteGames);
        ArgumentNullException.ThrowIfNull(privateServers);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(target);

        return target.Kind switch
        {
            RenameTargetKind.Game => favoriteGames.UpdateLocalNameAsync((long)target.Id, newLocalName),
            RenameTargetKind.PrivateServer => privateServers.UpdateLocalNameAsync((Guid)target.Id, newLocalName),
            RenameTargetKind.Account => accounts.UpdateLocalNameAsync((Guid)target.Id, newLocalName),
            _ => throw new ArgumentOutOfRangeException(nameof(target),
                $"Unknown RenameTargetKind: {target.Kind}"),
        };
    }
}
