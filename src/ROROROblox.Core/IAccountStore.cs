using ROROROblox.Core.Transport;

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

    /// <summary>
    /// Persist a per-account FPS cap. Pass <c>null</c> to clear (the next launch will not
    /// touch <c>ClientAppSettings.json</c>'s FPS flag). Pass an integer in [10, 9999] to set.
    /// Drives the per-account dropdown on the main window. Spec §5.4 + §6.1.
    /// </summary>
    Task SetFpsCapAsync(Guid id, int? fps);

    /// <summary>
    /// Set the per-user local nickname override. <paramref name="localName"/> is normalized:
    /// null / empty / whitespace all collapse to <c>null</c> (effective reset). The Roblox-side
    /// <see cref="Account.DisplayName"/> is never touched. Throws
    /// <see cref="KeyNotFoundException"/> if no account has the given <paramref name="accountId"/>.
    /// v1.3.x.
    /// </summary>
    Task UpdateLocalNameAsync(Guid accountId, string? localName);

    /// <summary>
    /// Persist the resolved Roblox <paramref name="userId"/> for the saved account identified
    /// by <paramref name="accountId"/>. Idempotent — no-op (no disk write, no DPAPI roundtrip)
    /// if the account already has the same <paramref name="userId"/> persisted. Throws
    /// <see cref="KeyNotFoundException"/> if no account has the given id. Cycle 5 (2026-05-08).
    /// </summary>
    /// <remarks>
    /// Granular write so the cycle-5 backfill orchestrator doesn't have to round-trip the full
    /// account list on every account it resolves. Three opportunistic-persist call sites in
    /// <c>MainViewModel</c> also call this whenever the in-memory <c>AccountSummary.RobloxUserId</c>
    /// is set. Soft-failure handling lives at the call site — see spec §6.
    /// </remarks>
    Task UpdateRobloxUserIdAsync(Guid accountId, long userId);

    /// <summary>
    /// Persist the stable per-account <paramref name="browserTrackerId"/> (v1.8.1 trust
    /// hygiene — followups 2026-06-30 §6). A real Roblox client keeps one browserTrackerId per
    /// account; generating a fresh random one per launch reads as a brand-new, unfamiliar
    /// client on every launch. Generated once at first launch (MainViewModel), then reused for
    /// the account's lifetime. Idempotent — no-op (no disk write, no DPAPI roundtrip) if the
    /// account already has the same value. Throws <see cref="KeyNotFoundException"/> if no
    /// account has the given id. NOT exported by account transport — the btid is a
    /// client-instance identity, so a destination PC generates its own.
    /// </summary>
    Task UpdateBrowserTrackerIdAsync(Guid accountId, long browserTrackerId);

    /// <summary>
    /// Persist free-text per-account tags (PS99, RCU, PLAZA…). Granular write mirroring
    /// <see cref="SetCaptionColorAsync"/> — no Save button, the edit is the save. The list is
    /// normalized before persisting: each tag is trimmed, empty/whitespace entries are dropped,
    /// duplicates collapse case-insensitively (first-seen casing wins), tag length is capped at
    /// 24 chars and total count at 8. Pass an empty list to clear all tags. Throws
    /// <see cref="KeyNotFoundException"/> if no account has the given <paramref name="id"/>.
    /// v1.5.0 — spec §"Components > 4".
    /// </summary>
    Task SetTagsAsync(Guid id, IReadOnlyList<string> tags);

    /// <summary>
    /// Bulk export read for account transport (v1.6.0 — spec §1). For each requested id that has a
    /// non-null <see cref="Account.RobloxUserId"/>, build a full <see cref="AccountExportRecord"/>:
    /// decrypt the cookie via the existing DPAPI path and copy every per-account field (display
    /// name, avatar, tags, fps cap, caption color, local name, main flag, sort order, selected).
    /// Requested ids whose userId is null land in <see cref="AccountExportResult.SkippedNoUserId"/>
    /// instead — the merge key on import requires a real userId, so they cannot be exported. Ids
    /// that don't match any local account are silently ignored. SECURITY-SENSITIVE: the returned
    /// records carry plaintext cookies; the caller (transport service) encrypts them immediately
    /// and they are never logged.
    /// </summary>
    Task<AccountExportResult> ExportAccountsAsync(IEnumerable<Guid> ids);

    /// <summary>
    /// Merge import for account transport (v1.6.0 — spec §1). Non-destructive: merge by Roblox
    /// userId. Each record whose userId is NOT already present among local accounts is added (new
    /// Guid, CreatedAt=now, all fields incl. RobloxUserId, cookie DPAPI-encrypted). Records whose
    /// userId already exists locally are skipped — the existing local account is kept untouched.
    /// Dedupe is by userId only (display names aren't unique). All imported accounts are written in
    /// a single store read-modify-write cycle (one DPAPI roundtrip), not one per account.
    /// SECURITY-SENSITIVE: the supplied records carry plaintext cookies, re-encrypted on write and
    /// never logged.
    /// </summary>
    Task<ImportMergeResult> ImportMergeAsync(IReadOnlyList<AccountExportRecord> records);
}
