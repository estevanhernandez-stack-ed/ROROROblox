namespace ROROROblox.Core;

/// <summary>
/// Persistent, DPAPI-encrypted store of saved Roblox accounts. Spec §5.4 + §6.1.
/// Cookies live only inside the encrypted blob; the public <see cref="Account"/> record never
/// carries them. <see cref="RetrieveCookieAsync"/> is the only path back to plaintext.
/// </summary>
public interface IAccountStore
{
    Task<IReadOnlyList<Account>> ListAsync();
    Task<Account> AddAsync(string displayName, string avatarUrl, string cookie);
    Task RemoveAsync(Guid id);
    Task<string> RetrieveCookieAsync(Guid id);
    Task UpdateCookieAsync(Guid id, string newCookie);
    Task TouchLastLaunchedAsync(Guid id);

    /// <summary>
    /// Mark a single account as the user's "main." Exactly one main at a time — setting a new
    /// one clears the prior. Pass <see cref="Guid.Empty"/> or a non-existent id to unset all.
    /// The main flag drives compact-mode CTAs ("Start [MainName]"), tray double-click target,
    /// and downstream main-tinted tray icon work.
    /// </summary>
    Task SetMainAsync(Guid id);

    /// <summary>
    /// Persist a manual ordering of accounts. The list passed in is the new top-to-bottom order;
    /// each account's <see cref="Account.SortOrder"/> gets re-numbered to its index. Accounts not
    /// in the list keep their existing SortOrder (no shuffle), but in normal use the caller passes
    /// every account. Drives the row order on the main window.
    /// </summary>
    Task UpdateSortOrderAsync(IReadOnlyList<Guid> idsInOrder);

    /// <summary>
    /// Persist whether an account is included in batch launches (Launch multiple / Private
    /// server). The per-row dot toggle calls this whenever the user clicks. Persisted so an
    /// unticked alt stays unticked across app restarts — no Save button, the click is the save.
    /// </summary>
    Task SetSelectedAsync(Guid id, bool isSelected);

    /// <summary>
    /// Persist a per-account Roblox window title-bar tint. Pass <c>#rrggbb</c> or <c>null</c>
    /// to clear (auto-derive). The window decorator re-applies via DwmSetWindowAttribute on
    /// the next tick so the color change shows up live in any currently-running Roblox window.
    /// </summary>
    Task SetCaptionColorAsync(Guid id, string? hex);
}
