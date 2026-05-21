namespace ROROROblox.Core.Transport;

/// <summary>
/// One account's full setup as it travels inside an exported bundle. SECURITY-SENSITIVE:
/// <see cref="Cookie"/> is the plaintext <c>.ROBLOSECURITY</c> value — full account access.
/// The caller (AccountStore) supplies it decrypted on export and re-encrypts it into local
/// DPAPI on import. This record only exists in memory during a transport operation and is
/// never logged or persisted by the transport layer itself. Spec §1 "Bundle format" / "Components".
/// </summary>
/// <param name="DisplayName">Roblox-side display name (informational; the source of truth is re-resolved on import if needed).</param>
/// <param name="RobloxUserId">Roblox user id — the merge key on import (dedupe by userId).</param>
/// <param name="Cookie">Plaintext <c>.ROBLOSECURITY</c>. Treat as a secret. Never log.</param>
/// <param name="Tags">Free-text per-account tags (PS99, RCU…).</param>
/// <param name="FpsCap">Per-account FPS cap, or null to leave Roblox config untouched on launch.</param>
/// <param name="CaptionColorHex">Per-account title-bar tint (<c>#rrggbb</c>), or null to auto-derive.</param>
/// <param name="LocalName">Per-user local nickname override, or null.</param>
/// <param name="IsMain">Whether this was the user's "main" account.</param>
/// <param name="SortOrder">Top-to-bottom row order.</param>
/// <param name="IsSelected">Whether the account is included in batch launches.</param>
public sealed record AccountExportRecord(
    string DisplayName,
    long RobloxUserId,
    string Cookie,
    IReadOnlyList<string> Tags,
    int? FpsCap,
    string? CaptionColorHex,
    string? LocalName,
    bool IsMain,
    int SortOrder,
    bool IsSelected);
