using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.Core.Transport;

namespace ROROROblox.Core;

/// <summary>
/// DPAPI-enveloped JSON store of accounts. Whole-blob encryption for v1.1 — simpler than per-cookie;
/// v1.2 upgrades alongside per-account WebView2 profiles (spec §5.4 decisions).
/// Durable write: temp file flushed to disk, then <see cref="File.Replace(string, string, string)"/> (or
/// <see cref="File.Move(string, string, bool)"/> when there is no good main file to keep), with the previous
/// good file kept as <c>accounts.dat.bak</c>. A missing, empty or unreadable main file falls back to the backup.
/// Thread-safe via a <see cref="SemaphoreSlim"/> gate. With no usable backup, decrypt failures become
/// <see cref="AccountStoreCorruptException"/>.
/// </summary>
public sealed class AccountStore : IAccountStore, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly string _filePath;
    private readonly ILogger<AccountStore> _log;
    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);
    // In-memory, per-account monotonic counter bumped every time an account's cookie is replaced
    // (reauth). Not persisted — it only needs to be stable within a single app run so a presence
    // poll that started before a reauth can tell its stale 401 apart from a genuine expiry
    // (the reauth re-flag race, 2026-07-03). Concurrent because the poll loop reads it off the
    // threadpool while the store gate serializes the bump.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, int> _cookieGeneration = new();
    private bool _disposed;

    public AccountStore() : this(DefaultPath()) { }

    public AccountStore(string filePath, ILogger<AccountStore>? log = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path must not be empty.", nameof(filePath));
        }
        _filePath = filePath;
        _log = log ?? NullLogger<AccountStore>.Instance;
    }

    public static string DefaultPath()
    {
        return AppDataRoot.Combine("accounts.dat");
    }

    public async Task<IReadOnlyList<Account>> ListAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            return blob.Accounts
                .Select(a => new Account(a.Id, a.DisplayName, a.AvatarUrl, a.CreatedAt, a.LastLaunchedAt, a.IsMain, a.SortOrder, a.IsSelected, a.CaptionColorHex, a.FpsCap, a.LocalName, a.RobloxUserId, a.Tags ?? [], a.BrowserTrackerId, a.JoinViaFriend, a.StreamerName, a.StreamerAvatarId, a.AutoRejoin, a.Facts, a.Note, a.PanelExpanded))
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Account> AddAsync(string displayName, string avatarUrl, string cookie)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name must not be empty.", nameof(displayName));
        }
        if (string.IsNullOrEmpty(cookie))
        {
            throw new ArgumentException("Cookie must not be empty.", nameof(cookie));
        }
        CookieValidation.ThrowIfControlCharacters(cookie, nameof(cookie));

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            // First account added gets auto-promoted to main. The user can re-pick later.
            var promoteAsMain = blob.Accounts.Count == 0;
            // Place new accounts after every existing one in manual-sort order so a fresh add
            // doesn't jump above the user's curated arrangement.
            var nextSortOrder = blob.Accounts.Count == 0
                ? 0
                : blob.Accounts.Max(a => a.SortOrder) + 1;
            var stored = new StoredAccount(
                Id: Guid.NewGuid(),
                DisplayName: displayName,
                AvatarUrl: avatarUrl ?? string.Empty,
                Cookie: cookie,
                CreatedAt: DateTimeOffset.UtcNow,
                LastLaunchedAt: null,
                IsMain: promoteAsMain,
                SortOrder: nextSortOrder,
                JoinViaFriend: false); // a fresh account is never flagged
            blob.Accounts.Add(stored);
            await SaveAsync(blob).ConfigureAwait(false);
            return new Account(stored.Id, stored.DisplayName, stored.AvatarUrl, stored.CreatedAt, stored.LastLaunchedAt, stored.IsMain, stored.SortOrder, stored.IsSelected, stored.CaptionColorHex, stored.FpsCap, stored.LocalName, stored.RobloxUserId, stored.Tags ?? [], stored.BrowserTrackerId, JoinViaFriend: false, StreamerName: stored.StreamerName, StreamerAvatarId: stored.StreamerAvatarId, AutoRejoin: false, Facts: stored.Facts, Note: stored.Note, PanelExpanded: stored.PanelExpanded);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetCaptionColorAsync(Guid id, string? hex)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var idx = blob.Accounts.FindIndex(a => a.Id == id);
            if (idx < 0)
            {
                return;
            }
            // Normalize empty/whitespace → null. Don't validate hex shape here; the consumer
            // (window decorator) tolerates malformed values by falling back to auto-palette.
            var normalized = string.IsNullOrWhiteSpace(hex) ? null : hex.Trim();
            if (string.Equals(blob.Accounts[idx].CaptionColorHex, normalized, StringComparison.Ordinal))
            {
                return; // no-op write avoidance
            }
            blob.Accounts[idx] = blob.Accounts[idx] with { CaptionColorHex = normalized };
            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetFpsCapAsync(Guid id, int? fps)
    {
        // Caller is responsible for clamping; we still defensively clamp to [10, 9999] so
        // disk never holds an invalid value.
        var clamped = fps is null ? (int?)null : FpsPresets.ClampCustom(fps.Value);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var idx = blob.Accounts.FindIndex(a => a.Id == id);
            if (idx < 0)
            {
                return;
            }
            if (blob.Accounts[idx].FpsCap == clamped)
            {
                return; // no-op write avoidance
            }
            blob.Accounts[idx] = blob.Accounts[idx] with { FpsCap = clamped };
            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetSelectedAsync(Guid id, bool isSelected)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var idx = blob.Accounts.FindIndex(a => a.Id == id);
            if (idx < 0)
            {
                return;
            }
            if (blob.Accounts[idx].IsSelected == isSelected)
            {
                return; // no-op write avoidance — saves a DPAPI roundtrip on chatty toggles.
            }
            blob.Accounts[idx] = blob.Accounts[idx] with { IsSelected = isSelected };
            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetJoinViaFriendAsync(Guid id, bool joinViaFriend)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var idx = blob.Accounts.FindIndex(a => a.Id == id);
            if (idx < 0)
            {
                return;
            }
            if (blob.Accounts[idx].JoinViaFriend == joinViaFriend)
            {
                return; // no-op write avoidance — saves a DPAPI roundtrip on chatty toggles.
            }
            blob.Accounts[idx] = blob.Accounts[idx] with { JoinViaFriend = joinViaFriend };
            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetAutoRejoinAsync(Guid id, bool autoRejoin)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var idx = blob.Accounts.FindIndex(a => a.Id == id);
            if (idx < 0)
            {
                return;
            }
            if (blob.Accounts[idx].AutoRejoin == autoRejoin)
            {
                return; // no-op write avoidance — saves a DPAPI roundtrip on chatty toggles.
            }
            blob.Accounts[idx] = blob.Accounts[idx] with { AutoRejoin = autoRejoin };
            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// How far behind the incoming stamp the stored <see cref="AccountFacts.FetchedAtUtc"/> may fall
    /// before an otherwise unchanged refresh is written anyway. The service stamps every successful
    /// refresh, so comparing whole records never skipped and rewrote the encrypted vault about every
    /// ten minutes per account. The skip is bounded so the persisted stamp (what "Updated N ago" reads
    /// after a restart) never lags the truth by more than this.
    /// </summary>
    internal static readonly TimeSpan FactsStampWriteInterval = TimeSpan.FromHours(6);

    private static bool FactsUnchanged(AccountFacts? stored, AccountFacts incoming)
    {
        if (stored is null
            || stored.Robux != incoming.Robux
            || stored.Membership != incoming.Membership
            || stored.FriendsOnline != incoming.FriendsOnline
            || stored.LastError != incoming.LastError)
        {
            return false;
        }
        if (incoming.FetchedAtUtc is not { } newer)
        {
            return true; // nothing newer to record (a failed refresh keeps the old stamp).
        }
        // Stored stamp null with an incoming one: there is no gap to measure, so record it.
        return stored.FetchedAtUtc is { } older && newer - older < FactsStampWriteInterval;
    }

    public async Task SetFactsAsync(Guid id, AccountFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var idx = blob.Accounts.FindIndex(a => a.Id == id);
            if (idx < 0)
            {
                return;
            }
            if (FactsUnchanged(blob.Accounts[idx].Facts, facts))
            {
                return; // no-op write avoidance: a refresh that changed nothing costs no DPAPI roundtrip.
            }
            blob.Accounts[idx] = blob.Accounts[idx] with { Facts = facts };
            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateNoteAsync(Guid id, string? note)
    {
        var normalized = NormalizeNote(note);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var idx = blob.Accounts.FindIndex(a => a.Id == id);
            if (idx < 0)
            {
                return;
            }
            if (string.Equals(blob.Accounts[idx].Note, normalized, StringComparison.Ordinal))
            {
                return; // no-op write avoidance.
            }
            blob.Accounts[idx] = blob.Accounts[idx] with { Note = normalized };
            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetPanelExpandedAsync(Guid id, bool expanded)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var idx = blob.Accounts.FindIndex(a => a.Id == id);
            if (idx < 0)
            {
                return;
            }
            if (blob.Accounts[idx].PanelExpanded == expanded)
            {
                return; // no-op write avoidance. Null (never chosen) is not equal to either value, so the first choice persists.
            }
            blob.Accounts[idx] = blob.Accounts[idx] with { PanelExpanded = expanded };
            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string? NormalizeNote(string? note) => Account.NormalizeNote(note);

    public async Task UpdateSortOrderAsync(IReadOnlyList<Guid> idsInOrder)
    {
        ArgumentNullException.ThrowIfNull(idsInOrder);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var anyChanged = false;
            for (var newOrder = 0; newOrder < idsInOrder.Count; newOrder++)
            {
                var id = idsInOrder[newOrder];
                var idx = blob.Accounts.FindIndex(a => a.Id == id);
                if (idx < 0) continue;
                if (blob.Accounts[idx].SortOrder != newOrder)
                {
                    blob.Accounts[idx] = blob.Accounts[idx] with { SortOrder = newOrder };
                    anyChanged = true;
                }
            }
            if (anyChanged)
            {
                await SaveAsync(blob).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetMainAsync(Guid id)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var anyChanged = false;
            for (var i = 0; i < blob.Accounts.Count; i++)
            {
                var shouldBeMain = blob.Accounts[i].Id == id;
                if (blob.Accounts[i].IsMain != shouldBeMain)
                {
                    blob.Accounts[i] = blob.Accounts[i] with { IsMain = shouldBeMain };
                    anyChanged = true;
                }
            }
            if (anyChanged)
            {
                await SaveAsync(blob).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(Guid id)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var removed = blob.Accounts.FirstOrDefault(a => a.Id == id);
            blob.Accounts.RemoveAll(a => a.Id == id);

            // If we just removed the main and others remain, auto-promote the most-recently-launched
            // (or, falling back, the most recently added) so compact mode never lands in the
            // "no main" empty state right after a removal.
            if (removed?.IsMain == true && blob.Accounts.Count > 0)
            {
                var promoteIndex = blob.Accounts
                    .Select((a, i) => (a, i))
                    .OrderByDescending(t => t.a.LastLaunchedAt ?? t.a.CreatedAt)
                    .First().i;
                blob.Accounts[promoteIndex] = blob.Accounts[promoteIndex] with { IsMain = true };
            }

            // The backup holds the removed account's cookie until it is rewritten to match the new main.
            await SaveAsync(blob, scrubBackup: true).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> RetrieveCookieAsync(Guid id)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var match = blob.Accounts.FirstOrDefault(a => a.Id == id)
                ?? throw new KeyNotFoundException($"Account {id} not found.");
            return match.Cookie;
        }
        finally
        {
            _gate.Release();
        }
    }

    public int GetCookieGeneration(Guid id) =>
        _cookieGeneration.TryGetValue(id, out var v) ? v : 0;

    public async Task UpdateCookieAsync(Guid id, string newCookie)
    {
        if (string.IsNullOrEmpty(newCookie))
        {
            throw new ArgumentException("Cookie must not be empty.", nameof(newCookie));
        }
        CookieValidation.ThrowIfControlCharacters(newCookie, nameof(newCookie));

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var index = blob.Accounts.FindIndex(a => a.Id == id);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Account {id} not found.");
            }
            blob.Accounts[index] = blob.Accounts[index] with { Cookie = newCookie };
            await SaveAsync(blob).ConfigureAwait(false);
            // Bump AFTER the save commits so a reader that observes the new generation is
            // guaranteed the new cookie is already on disk (reauth re-flag race guard).
            _cookieGeneration.AddOrUpdate(id, 1, (_, v) => v + 1);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateLocalNameAsync(Guid accountId, string? localName)
    {
        var normalized = string.IsNullOrWhiteSpace(localName) ? null : localName.Trim();

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var index = blob.Accounts.FindIndex(a => a.Id == accountId);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Account {accountId} not found.");
            }
            if (string.Equals(blob.Accounts[index].LocalName, normalized, StringComparison.Ordinal))
            {
                return; // no-op write avoidance — saves a DPAPI roundtrip on chatty rename UIs.
            }
            blob.Accounts[index] = blob.Accounts[index] with { LocalName = normalized };
            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateStreamerIdentityAsync(Guid accountId, string fakeName, string fakeAvatarId)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var index = blob.Accounts.FindIndex(a => a.Id == accountId);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Account {accountId} not found.");
            }
            var cur = blob.Accounts[index];
            if (cur.StreamerName == fakeName && cur.StreamerAvatarId == fakeAvatarId)
            {
                return; // no-op write avoidance — saves a DPAPI roundtrip on chatty identity refreshes.
            }
            blob.Accounts[index] = cur with { StreamerName = fakeName, StreamerAvatarId = fakeAvatarId };
            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateRobloxUserIdAsync(Guid accountId, long userId)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var index = blob.Accounts.FindIndex(a => a.Id == accountId);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Account {accountId} not found.");
            }
            if (blob.Accounts[index].RobloxUserId == userId)
            {
                return; // idempotent — saves a DPAPI roundtrip on chatty backfill orchestrator passes.
            }
            blob.Accounts[index] = blob.Accounts[index] with { RobloxUserId = userId };
            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateBrowserTrackerIdAsync(Guid accountId, long browserTrackerId)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var index = blob.Accounts.FindIndex(a => a.Id == accountId);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Account {accountId} not found.");
            }
            if (blob.Accounts[index].BrowserTrackerId == browserTrackerId)
            {
                return; // idempotent — mirrors UpdateRobloxUserIdAsync, saves the DPAPI roundtrip.
            }
            blob.Accounts[index] = blob.Accounts[index] with { BrowserTrackerId = browserTrackerId };
            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetTagsAsync(Guid id, IReadOnlyList<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        // Defensively normalize on the write path so disk never holds dupes, blanks, over-length,
        // or more than the cap — even if a caller bypasses the ViewModel's incremental normalization.
        var normalized = NormalizeTags(tags);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var index = blob.Accounts.FindIndex(a => a.Id == id);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Account {id} not found.");
            }
            // No-op write avoidance — skip the DPAPI roundtrip when nothing changed.
            var current = blob.Accounts[index].Tags ?? [];
            if (current.SequenceEqual(normalized, StringComparer.Ordinal))
            {
                return;
            }
            blob.Accounts[index] = blob.Accounts[index] with { Tags = normalized };
            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Tag count cap — keeps the account row from overflowing. Spec §"Components > 4" (~8).
    /// </summary>
    public const int MaxTags = 8;

    /// <summary>
    /// Tag length cap — keeps a single chip from blowing out the row width. Spec §"Components > 4" (~24).
    /// </summary>
    public const int MaxTagLength = 24;

    /// <summary>
    /// Trim each tag, drop empty/whitespace, truncate to <see cref="MaxTagLength"/>, dedupe
    /// case-insensitively (first-seen casing wins), and cap at <see cref="MaxTags"/>. Mirrors the
    /// ViewModel's incremental AddTag rules so the two never diverge.
    /// </summary>
    private static List<string> NormalizeTags(IReadOnlyList<string> tags)
    {
        var result = new List<string>(Math.Min(tags.Count, MaxTags));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in tags)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }
            var trimmed = raw.Trim();
            if (trimmed.Length > MaxTagLength)
            {
                trimmed = trimmed[..MaxTagLength];
            }
            if (!seen.Add(trimmed))
            {
                continue; // case-insensitive dupe
            }
            result.Add(trimmed);
            if (result.Count >= MaxTags)
            {
                break;
            }
        }
        return result;
    }

    public async Task TouchLastLaunchedAsync(Guid id)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var index = blob.Accounts.FindIndex(a => a.Id == id);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Account {id} not found.");
            }
            blob.Accounts[index] = blob.Accounts[index] with { LastLaunchedAt = DateTimeOffset.UtcNow };
            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AccountExportResult> ExportAccountsAsync(IEnumerable<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        // Snapshot the requested ids in order, de-duped, so a caller passing the same id twice
        // doesn't yield duplicate records.
        var requested = new List<Guid>();
        var seenIds = new HashSet<Guid>();
        foreach (var id in ids)
        {
            if (seenIds.Add(id))
            {
                requested.Add(id);
            }
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var byId = blob.Accounts.ToDictionary(a => a.Id);

            var records = new List<AccountExportRecord>(requested.Count);
            var skipped = new List<Guid>();

            foreach (var id in requested)
            {
                if (!byId.TryGetValue(id, out var stored))
                {
                    continue; // unknown id — silently ignored, not a "no userId" skip
                }
                if (stored.RobloxUserId is not long userId)
                {
                    skipped.Add(id); // merge key requires a real userId
                    continue;
                }

                // Cookie is already in plaintext here — LoadAsync DPAPI-decrypted the whole blob.
                records.Add(new AccountExportRecord(
                    DisplayName: stored.DisplayName,
                    AvatarUrl: stored.AvatarUrl,
                    RobloxUserId: userId,
                    Cookie: stored.Cookie,
                    Tags: stored.Tags ?? [],
                    FpsCap: stored.FpsCap,
                    CaptionColorHex: stored.CaptionColorHex,
                    LocalName: stored.LocalName,
                    IsMain: stored.IsMain,
                    SortOrder: stored.SortOrder,
                    IsSelected: stored.IsSelected,
                    JoinViaFriend: stored.JoinViaFriend,
                    StreamerName: stored.StreamerName,
                    StreamerAvatarId: stored.StreamerAvatarId,
                    Note: stored.Note)); // Facts and PanelExpanded never travel: a cache, and a per-machine view state.
            }

            return new AccountExportResult(records, skipped);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ImportMergeResult> ImportMergeAsync(IReadOnlyList<AccountExportRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);

            // Merge key set — every userId already present locally. A record whose userId is in
            // here is skipped; otherwise it's added (and its userId joins the set so an in-bundle
            // duplicate only imports once).
            var presentUserIds = new HashSet<long>(
                blob.Accounts.Where(a => a.RobloxUserId is not null).Select(a => a.RobloxUserId!.Value));

            var imported = 0;
            var skipped = 0;
            var now = DateTimeOffset.UtcNow;

            foreach (var record in records)
            {
                // A cookie with a control character can never be attached to a request (the header
                // write throws and echoes it), so the record is skipped, not stored. Checked before the
                // userId joins the merge set so it cannot shadow a valid record for the same user.
                if (CookieValidation.HasControlCharacters(record.Cookie))
                {
                    skipped++;
                    continue;
                }
                if (!presentUserIds.Add(record.RobloxUserId))
                {
                    skipped++; // userId already present locally or earlier in this same batch
                    continue;
                }

                // SortOrder travels with the record (spec §1 — full per-account setup travels). A
                // collision with an existing local order is harmless: ListAsync doesn't order by it,
                // the UI does, and one drag-reorder resolves any tie.
                blob.Accounts.Add(new StoredAccount(
                    Id: Guid.NewGuid(),
                    DisplayName: record.DisplayName,
                    AvatarUrl: record.AvatarUrl ?? string.Empty,
                    Cookie: record.Cookie,
                    CreatedAt: now,
                    LastLaunchedAt: null,
                    IsMain: record.IsMain,
                    SortOrder: record.SortOrder,
                    IsSelected: record.IsSelected,
                    CaptionColorHex: record.CaptionColorHex,
                    FpsCap: record.FpsCap,
                    LocalName: record.LocalName,
                    RobloxUserId: record.RobloxUserId,
                    Tags: record.Tags is { Count: > 0 } ? record.Tags : null,
                    JoinViaFriend: record.JoinViaFriend,
                    StreamerName: record.StreamerName,
                    StreamerAvatarId: record.StreamerAvatarId,
                    Note: NormalizeNote(record.Note)));
                imported++;
            }

            // One atomic write for the whole batch — not one DPAPI roundtrip per imported account.
            if (imported > 0)
            {
                await SaveAsync(blob).ConfigureAwait(false);
            }

            return new ImportMergeResult(imported, skipped);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<StartFreshResult> StartFreshAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Look again before setting anything aside. The corrupt dialog also appears for a read that
            // failed on I/O (a scanner or a sync tool holding the file for a moment), which says nothing
            // about the vault. If the store reads now, by its ordinary rules, backup included, there is
            // nothing to start fresh from: setting a readable vault aside would hide every saved account
            // behind a rename the user would not know to undo.
            try
            {
                await LoadAsync().ConfigureAwait(false);
                _mainReadGood = false; // a probe, not the read before a save
                _log.LogWarning("Start fresh was asked for, but {VaultFile} reads correctly now; nothing was set aside.", Path.GetFileName(_filePath));
                return new StartFreshResult([], []) { StoreWasReadable = true };
            }
            catch (AccountStoreCorruptException)
            {
                // Still unreadable: this is the case Start fresh is for.
                _mainReadGood = false;
            }

            // The backup goes first and is the one undone if main then refuses: a main left in place beside
            // a set-aside backup cannot bring stale accounts back, the reverse order could.
            var stamp = $"corrupt-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
            var setAside = new List<string>();
            string? backupAside = null;

            if (File.Exists(BackupPath))
            {
                backupAside = UniqueAsideName(BackupPath, stamp);
                if (!TryMove(BackupPath, backupAside))
                {
                    return new StartFreshResult([], [Path.GetFileName(BackupPath)]);
                }
                setAside.Add(Path.GetFileName(backupAside));
            }

            if (File.Exists(_filePath))
            {
                var mainAside = UniqueAsideName(_filePath, stamp);
                if (!TryMove(_filePath, mainAside))
                {
                    var failed = new List<string> { Path.GetFileName(_filePath) };
                    if (backupAside is not null && !TryMove(backupAside, BackupPath))
                    {
                        // Could not undo: say so. The backup stays set aside, which cannot restore anything.
                        failed.Add(Path.GetFileName(BackupPath));
                    }
                    else if (backupAside is not null)
                    {
                        setAside.Clear();
                    }
                    return new StartFreshResult(setAside, failed);
                }
                setAside.Add(Path.GetFileName(mainAside));
            }

            // The store now reads as empty: reset what a read remembers about the old files.
            _mainReadGood = false;
            _recoveryWarningLatched = false;
            _emptyWarningLogged = false;
            _log.LogWarning("Started the vault fresh; set aside {Files}.", string.Join(", ", setAside));
            return new StartFreshResult(setAside, []);
        }
        finally
        {
            _gate.Release();
        }
    }

    // A name beside the file, never over another set-aside copy: a second Start fresh in the same second
    // must not collide with, or replace, the first.
    private static string UniqueAsideName(string path, string stamp)
    {
        var candidate = $"{path}.{stamp}";
        for (var n = 2; File.Exists(candidate); n++)
        {
            candidate = $"{path}.{stamp}-{n}";
        }
        return candidate;
    }

    private static bool TryMove(string from, string to)
    {
        try
        {
            File.Move(from, to, overwrite: false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // The previous good vault. SaveAsync only ever overwrites it with a main file LoadAsync just read
    // successfully, so it is at most one write behind a good main and never a copy of a damaged one.
    private string BackupPath => _filePath + ".bak";

    // True when the LoadAsync that opened the current gate hold read the main file successfully. Every
    // mutating method is Load then Save under _gate, so this is the one hop the fact has to make; SaveAsync
    // consumes it and resets it, which makes a Save with no preceding good Load the conservative case
    // (the backup is left alone). Guarded by _gate like everything else here.
    private bool _mainReadGood;

    // Warning latches, so a condition that persists across loads (the presence poll re-reads the vault
    // about every 25 s) logs once per store instance instead of on every read.
    private bool _recoveryWarningLatched; // set only while healing main from the backup keeps failing
    private bool _emptyWarningLogged;

    private enum VaultReadStatus { Missing, Empty, Good, Damaged, IoError }

    private sealed record VaultRead(VaultReadStatus Status, StoredAccountsBlob? Blob = null, AccountStoreCorruptException? Error = null);

    private async Task<StoredAccountsBlob> LoadAsync()
    {
        _mainReadGood = false;

        var main = await ReadVaultFileAsync(_filePath).ConfigureAwait(false);
        switch (main.Status)
        {
            case VaultReadStatus.Good:
                _mainReadGood = true;
                return main.Blob!;
            case VaultReadStatus.IoError:
                // A read that failed on I/O (a transient lock, say) says nothing about the file's contents.
                // Falling back and then saving would overwrite a good main with an older state, so this stays a throw.
                throw main.Error!;
        }

        // Missing, zero-length or damaged: the backup is the previous good file.
        var backup = await ReadVaultFileAsync(BackupPath).ConfigureAwait(false);
        if (backup.Status == VaultReadStatus.Good)
        {
            var recovered = backup.Blob!;
            if (!_recoveryWarningLatched)
            {
                _log.LogWarning(
                    "{VaultFile} was {Problem}; recovered {Count} account(s) from {BackupFile}.",
                    Path.GetFileName(_filePath),
                    main.Status switch { VaultReadStatus.Missing => "missing", VaultReadStatus.Empty => "empty", _ => "unreadable" },
                    recovered.Accounts.Count,
                    Path.GetFileName(BackupPath));
            }
            // Heal main now so the next load reads it as good (one Warning, and two good copies again).
            // _mainReadGood stays false: a save in this same hold must not promote anything over the backup.
            // If the heal fails, latch the warning so a damaged main does not log on every load.
            _recoveryWarningLatched = !TryHealMainFromBackup();
            return recovered;
        }

        // No usable backup: behave as before the backup existed.
        switch (main.Status)
        {
            case VaultReadStatus.Empty:
                if (!_emptyWarningLogged)
                {
                    _emptyWarningLogged = true; // once per store instance: loads run on every read
                    _log.LogWarning(
                        "{VaultFile} is empty and there is no usable {BackupFile}; reading it as an empty store.",
                        Path.GetFileName(_filePath),
                        Path.GetFileName(BackupPath));
                }
                return new StoredAccountsBlob(Version: 1, Accounts: []);
            case VaultReadStatus.Damaged:
                throw main.Error!;
            default:
                return new StoredAccountsBlob(Version: 1, Accounts: []);
        }
    }

    // Reads and decrypts one vault file. Never logs or throws content: the Damaged and IoError errors carry
    // the fixed messages LoadAsync always threw, with the underlying exception as inner.
    private static async Task<VaultRead> ReadVaultFileAsync(string path)
    {
        if (!File.Exists(path))
        {
            return new VaultRead(VaultReadStatus.Missing);
        }

        byte[] encrypted;
        try
        {
            encrypted = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            return new VaultRead(VaultReadStatus.IoError, Error: new AccountStoreCorruptException($"Could not read {path}.", ex));
        }

        if (encrypted.Length == 0)
        {
            return new VaultRead(VaultReadStatus.Empty);
        }

        byte[] plaintext;
        try
        {
            plaintext = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException ex)
        {
            return new VaultRead(VaultReadStatus.Damaged, Error: new AccountStoreCorruptException(
                "DPAPI decrypt failed; accounts.dat is unreadable on this PC. " +
                "Likely cause: Windows restored from backup that didn't preserve the DPAPI master key, " +
                "SID change, or the file was copied from another machine.",
                ex));
        }

        try
        {
            var blob = JsonSerializer.Deserialize<StoredAccountsBlob>(plaintext, SerializerOptions);
            // A vault that decrypts and parses but has the wrong shape (the literal null, a missing or null
            // accounts list, a null row) is Damaged, never an empty store: an empty list read as Good would
            // let the next save write over every saved account, and would let that empty file replace the
            // backup. Damaged sends it to the backup, then to the corrupt path.
            if (blob is null || !StoreShape.ListOk(blob.Accounts))
            {
                return new VaultRead(VaultReadStatus.Damaged, Error: new AccountStoreCorruptException("accounts.dat decrypted but its JSON shape is invalid."));
            }

            // A row with a missing name, avatar or cookie is a different case and is REPAIRED, not
            // rejected. Rejecting it would turn a vault that opens today into the corrupt dialog for
            // every account in it, to protect against one bad row. Empty strings are safe everywhere a
            // null is not: a row with no cookie reads as signed out and asks to sign in again.
            for (var i = 0; i < blob.Accounts.Count; i++)
            {
                var row = blob.Accounts[i];
                if (row.DisplayName is null || row.Cookie is null || row.AvatarUrl is null)
                {
                    blob.Accounts[i] = row with
                    {
                        DisplayName = row.DisplayName ?? string.Empty,
                        Cookie = row.Cookie ?? string.Empty,
                        AvatarUrl = row.AvatarUrl ?? string.Empty,
                    };
                }
            }
            return new VaultRead(VaultReadStatus.Good, blob);
        }
        catch (JsonException ex)
        {
            return new VaultRead(VaultReadStatus.Damaged, Error: new AccountStoreCorruptException("accounts.dat decrypted but its JSON shape is invalid.", ex));
        }
    }

    private async Task SaveAsync(StoredAccountsBlob blob, bool scrubBackup = false)
    {
        var backupMayBeOverwritten = _mainReadGood;
        _mainReadGood = false;

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(blob, SerializerOptions);
        var encrypted = ProtectedData.Protect(json, optionalEntropy: null, DataProtectionScope.CurrentUser);

        // FileMode.Create truncates a leftover .tmp from an earlier crash. The flush puts the bytes on disk
        // before the swap, so a power cut cannot leave a renamed-in file whose contents never landed.
        var tempPath = _filePath + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
        {
            await stream.WriteAsync(encrypted).ConfigureAwait(false);
            stream.Flush(true);
        }

        if (backupMayBeOverwritten && File.Exists(_filePath))
        {
            // The main file was just read as good, so it is a fair backup. Replace keeps it as .bak.
            try
            {
                File.Replace(tempPath, _filePath, BackupPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A held file (antivirus, a locked backup). The new vault matters more than a fresher
                // backup: refresh the backup if we can, then move the temp file in. If main is gone
                // (Replace renamed it before failing) the move still lands it; if the move throws,
                // the temp file keeps the new state and main or backup still holds the old one.
                _log.LogWarning(ex, "Could not swap {VaultFile} with a backup; saving without refreshing {BackupFile}.",
                    Path.GetFileName(_filePath), Path.GetFileName(BackupPath));
                TryCopyMainToBackup();
                File.Move(tempPath, _filePath, overwrite: true);
            }
        }
        else
        {
            // No main, or a main that was not read successfully: leave the backup alone.
            File.Move(tempPath, _filePath, overwrite: true);
        }

        if (scrubBackup)
        {
            ScrubBackup();
        }
    }

    // Best effort. Byte-copies the good backup over a damaged or missing main (no re-encryption), via a temp
    // name so a torn copy cannot replace anything. Never touches the backup.
    private bool TryHealMainFromBackup()
    {
        var healTemp = _filePath + ".tmp";
        try
        {
            File.Copy(BackupPath, healTemp, overwrite: true);
            File.Move(healTemp, _filePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(healTemp); } catch (Exception) { /* best effort */ }
            return false;
        }
    }

    // Best effort. Copies via a temp name so a torn copy cannot replace a good backup.
    private bool TryCopyMainToBackup()
    {
        var backupTemp = BackupPath + ".tmp";
        try
        {
            File.Copy(_filePath, backupTemp, overwrite: true);
            File.Move(backupTemp, BackupPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(backupTemp); } catch (Exception) { /* best effort */ }
            return false;
        }
    }

    // After a removal the backup must not hold the removed account's cookie. Rewrite it to match the main
    // file; if that fails, delete it; if that fails too, say so (the file name only, never content).
    private void ScrubBackup()
    {
        if (TryCopyMainToBackup())
        {
            return;
        }
        try
        {
            File.Delete(BackupPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Could not rewrite or remove {BackupFile} after an account removal; it may still hold the removed account.",
                Path.GetFileName(BackupPath));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _gate.Dispose();
    }

    // Internal storage shape — the encrypted blob payload. Cookie lives here, never on the public Account.
    // IsMain + SortOrder default safely so older accounts.dat files load cleanly — System.Text.Json
    // fills missing optional fields with their defaults, no migration step needed.
    internal sealed record StoredAccount(
        Guid Id,
        string DisplayName,
        string AvatarUrl,
        string Cookie,
        DateTimeOffset CreatedAt,
        DateTimeOffset? LastLaunchedAt,
        bool IsMain = false,
        int SortOrder = 0,
        bool IsSelected = true,
        string? CaptionColorHex = null,
        int? FpsCap = null,
        string? LocalName = null,
        long? RobloxUserId = null,
        IReadOnlyList<string>? Tags = null,
        long? BrowserTrackerId = null,
        bool JoinViaFriend = false,
        string? StreamerName = null,
        string? StreamerAvatarId = null,
        bool AutoRejoin = false,
        AccountFacts? Facts = null,
        string? Note = null,
        bool? PanelExpanded = null)
    {
        // The synthesized ToString would print every member, Cookie included.
        public sealed override string ToString() => $"StoredAccount {{ Id = {Id}, Cookie = <redacted> }}";
    }

    internal sealed record StoredAccountsBlob(
        int Version,
        List<StoredAccount> Accounts);
}
