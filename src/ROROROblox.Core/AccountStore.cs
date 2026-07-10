using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ROROROblox.Core.Transport;

namespace ROROROblox.Core;

/// <summary>
/// DPAPI-enveloped JSON store of accounts. Whole-blob encryption for v1.1 — simpler than per-cookie;
/// v1.2 upgrades alongside per-account WebView2 profiles (spec §5.4 decisions).
/// Atomic write via temp-file + <see cref="File.Move(string, string, bool)"/>. Thread-safe via a
/// <see cref="SemaphoreSlim"/> gate. Decrypt failures become <see cref="AccountStoreCorruptException"/>.
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
    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);
    // In-memory, per-account monotonic counter bumped every time an account's cookie is replaced
    // (reauth). Not persisted — it only needs to be stable within a single app run so a presence
    // poll that started before a reauth can tell its stale 401 apart from a genuine expiry
    // (the reauth re-flag race, 2026-07-03). Concurrent because the poll loop reads it off the
    // threadpool while the store gate serializes the bump.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, int> _cookieGeneration = new();
    private bool _disposed;

    public AccountStore() : this(DefaultPath()) { }

    public AccountStore(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path must not be empty.", nameof(filePath));
        }
        _filePath = filePath;
    }

    public static string DefaultPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "ROROROblox", "accounts.dat");
    }

    public async Task<IReadOnlyList<Account>> ListAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            return blob.Accounts
                .Select(a => new Account(a.Id, a.DisplayName, a.AvatarUrl, a.CreatedAt, a.LastLaunchedAt, a.IsMain, a.SortOrder, a.IsSelected, a.CaptionColorHex, a.FpsCap, a.LocalName, a.RobloxUserId, a.Tags ?? [], a.BrowserTrackerId, a.JoinViaFriend, a.StreamerName, a.StreamerAvatarId))
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
            return new Account(stored.Id, stored.DisplayName, stored.AvatarUrl, stored.CreatedAt, stored.LastLaunchedAt, stored.IsMain, stored.SortOrder, stored.IsSelected, stored.CaptionColorHex, stored.FpsCap, stored.LocalName, stored.RobloxUserId, stored.Tags ?? [], stored.BrowserTrackerId, JoinViaFriend: false, StreamerName: stored.StreamerName, StreamerAvatarId: stored.StreamerAvatarId);
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

            await SaveAsync(blob).ConfigureAwait(false);
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
                    JoinViaFriend: stored.JoinViaFriend));
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
                    JoinViaFriend: record.JoinViaFriend));
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

    private async Task<StoredAccountsBlob> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new StoredAccountsBlob(Version: 1, Accounts: []);
        }

        byte[] encrypted;
        try
        {
            encrypted = await File.ReadAllBytesAsync(_filePath).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new AccountStoreCorruptException($"Could not read {_filePath}.", ex);
        }

        if (encrypted.Length == 0)
        {
            return new StoredAccountsBlob(Version: 1, Accounts: []);
        }

        byte[] plaintext;
        try
        {
            plaintext = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException ex)
        {
            throw new AccountStoreCorruptException(
                "DPAPI decrypt failed; accounts.dat is unreadable on this PC. " +
                "Likely cause: Windows restored from backup that didn't preserve the DPAPI master key, " +
                "SID change, or the file was copied from another machine.",
                ex);
        }

        try
        {
            var blob = JsonSerializer.Deserialize<StoredAccountsBlob>(plaintext, SerializerOptions);
            return blob ?? new StoredAccountsBlob(Version: 1, Accounts: []);
        }
        catch (JsonException ex)
        {
            throw new AccountStoreCorruptException("accounts.dat decrypted but its JSON shape is invalid.", ex);
        }
    }

    private async Task SaveAsync(StoredAccountsBlob blob)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(blob, SerializerOptions);
        var encrypted = ProtectedData.Protect(json, optionalEntropy: null, DataProtectionScope.CurrentUser);

        var tempPath = _filePath + ".tmp";
        await File.WriteAllBytesAsync(tempPath, encrypted).ConfigureAwait(false);
        File.Move(tempPath, _filePath, overwrite: true);
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
        string? StreamerAvatarId = null);

    internal sealed record StoredAccountsBlob(
        int Version,
        List<StoredAccount> Accounts);
}
