# RoRoRo v1.6.0 — Account transport + bundle (private-server library, tag filter, Follow restore) + security pass

> **Status:** Design in progress 2026-05-21. Brainstorm decisions captured; pending spec review.
> **Cycle:** v1.6.0 (feature minor bump on v1.5.0). Anchor feature is account transport; security-sensitive, so it gets the deepest design.
> **Bundle:** account transport · saved private servers in the dropdown · tag UI (add-affordance + filter) · Follow restore · cross-cutting security pass.

## Why this exists

Two threads converged after v1.5.0 shipped:

1. **Clan ask (WitheredZack, K0ii Discord):** "is there a way to import all accounts into another rororo?" Today the answer is no — `accounts.dat` is DPAPI-encrypted with `DataProtectionScope.CurrentUser` (`AccountStore.cs:478,509`), so it decrypts only for the same Windows user on the same machine. Copying the file to another PC throws ("accounts.dat is unreadable on this PC", `AccountStore.cs:483`). Transport requires **re-keying**, not copying.
2. **Builder call:** bundle the remaining account-management asks into one release, and run a real **security pass** this cycle — appropriate because the anchor feature deliberately moves credentials off the machine for the first time.

The five pieces ship together as v1.6.0.

## 1. Account transport (anchor) — passphrase-protected export/import bundle

### Threat model (this is why the security pass exists)

The export bundle contains `.ROBLOSECURITY` cookies — full account access. The realistic threats:

- **Leaked / over-shared file** (posted in Discord, left on a shared drive). Mitigation: the file is useless without the passphrase; the passphrase is never stored in or alongside the file.
- **Weak passphrase → offline brute-force** of a captured file. Mitigation: a deliberately slow KDF + an enforced passphrase floor (below).
- **Tampered file.** Mitigation: AEAD (AES-GCM) makes decryption fail closed on any modification.

The Pet Sim 99 audience is non-technical, so the UX must make the safe path the default and say the risk plainly — no jargon, no apology.

### Crypto (locked, dependency-free)

- **KDF:** PBKDF2-HMAC-SHA256, **600,000 iterations** (OWASP 2023 floor for PBKDF2-SHA256), random 16-byte salt per bundle, deriving a 256-bit key. `Rfc2898DeriveBytes` (BCL).
- **AEAD:** **AES-256-GCM**, random 12-byte nonce per bundle, 16-byte tag. `System.Security.Cryptography.AesGcm` (BCL).
- No external crypto dependency. Both primitives are in `System.Security.Cryptography`.
- The passphrase and derived key live only in memory for the operation; never logged, never written. Clear key material after use where the BCL allows.

### Bundle format (`.rororo-accounts`, versioned)

A small binary container:
```
magic ("RRRACCT\0") | formatVersion (1) | kdfIterations (int) | salt (16B) | nonce (12B) | ciphertext+tag
```
The ciphertext is AES-GCM over a JSON payload: a list of account records, each carrying display name, Roblox userId, the cookie, and the full per-account setup (tags, fpsCap, captionColorHex, localName, isMain, sortOrder/selected). Versioned header so the format can evolve without breaking old bundles.

### Export flow

1. User opens **Export accounts** (Settings or a dedicated dialog).
2. Pick accounts — default to all (or all selected); a checklist.
3. Set passphrase: **enforced floor (≥12 chars, not obviously weak) + strength meter + confirm field.** Export stays disabled until it clears the bar.
4. RoRoRo decrypts each chosen account's cookie from local DPAPI, assembles the JSON payload, PBKDF2-derives the key, AES-GCM encrypts, writes the `.rororo-accounts` file to a user-chosen path.
5. Plain warning on success: "This file is your account logins. Anyone with the file **and** the passphrase can sign in as you. Keep the passphrase safe; don't post the file publicly."

### Import flow (merge — non-destructive)

1. User opens **Import accounts**, picks a `.rororo-accounts` file, enters the passphrase.
2. Derive key, AES-GCM decrypt. Wrong passphrase or tampered file → fail closed with a clear message ("Couldn't open this file — wrong passphrase, or the file is damaged."). No partial import.
3. **Merge by Roblox userId:** for each imported account whose `RobloxUserId` is not already present locally, add it — re-encrypt its cookie into this machine's DPAPI `accounts.dat` and persist its settings. Accounts already present are skipped (existing local copy kept untouched).
4. Report: "Imported N accounts. Skipped M already on this PC."

### Components

- **`IAccountTransport` / `AccountTransportService` (Core):** `Export(IReadOnlyList<AccountExportRecord>, passphrase) → byte[]` and `Import(byte[], passphrase) → IReadOnlyList<AccountExportRecord>`. Pure, unit-testable (round-trip, wrong-passphrase-throws, tamper-throws, version-header). No UI, no DPAPI inside — the caller supplies decrypted records on export and re-encrypts on import, so the transport service only does the crypto + serialization.
- **`AccountStore` additions:** a bulk export read (decrypt selected accounts' cookies + settings) and a bulk import write (add non-duplicate accounts). Reuses the existing DPAPI path.
- **UI:** Export/Import entry points + passphrase dialogs with the strength meter, brand-styled. Plain-language warnings.

### Disclosure change (carry into the next Store reviewer letter)

v1.5.0's letter says cookies "never leave the machine." Transport changes that to: cookies never leave **unintentionally** — export is a deliberate, user-initiated, passphrase-encrypted action, and the encrypted bundle goes only where the user puts it (no cloud, no 626 Labs, no third party). The privacy policy gets a matching line.

## 2. Security pass (cross-cutting)

- **Transport crypto review:** KDF iteration count current with guidance, nonce uniqueness per bundle, AEAD tag verified before use, no passphrase/key/cookie in logs or exceptions, key material cleared after use.
- **Full cookie/DPAPI blast-radius audit** across the whole app (the `dpapi-cookie-blast-radius` agent), not just the transport path — since this is the security-pass cycle.
- **Dependency audit** (`dotnet list ROROROblox.slnx package --vulnerable`); address criticals.
- **Disclosure updates:** reviewer letter + privacy policy reflect the deliberate-export reality.

## 3. Saved private servers in the per-account dropdown

**Scope correction (builder, 2026-05-21):** this is NOT a library overhaul. Saved private servers already exist (`IPrivateServerStore`) and are already renamable (the rename feature shipped in v1.3.x exists precisely so users can tell saved servers apart). The only gap: they don't appear in the per-account dropdown.

Today the row dropdown (`MainWindow.xaml:565`, `SelectedItem={Binding SelectedGame}`) binds to `AvailableGames`, which `ReloadGamesAsync` (`MainViewModel.cs:400-410`) fills from the favorites store + the JoinByLink sentinel only — saved private servers from `IPrivateServerStore` are never added. The fix: include the saved private servers in the dropdown's item list so a row can be set to launch into one (rendered with the server's `RenderName`; selecting it makes the row's launch target a `LaunchTarget.PrivateServer`, which already exists).

Implementation note (worked at build time): the dropdown's items are `FavoriteGame`. To list private servers there, either give the dropdown a common item abstraction (a "launch choice" wrapping `FavoriteGame` | `SavedPrivateServer`) or represent saved servers as a `FavoriteGame`-shaped entry carrying the private-server code. Either way it's a dropdown-population + selection-mapping change, not a new store or a new management surface. No new rename/remove UI — that already exists for `SavedPrivateServer`.

## 4. Tag UI — add-affordance redesign + filter

Two tag-UI items, both touching the v1.5.0 row tag chrome.

**4a. Add-affordance redesign (builder ask, 2026-05-21).** The v1.5.0 add-tag UI is an always-visible empty text bar on every row — it reads as clutter. Replace it with a **collapsed "+" chip**: a small tag-shaped pill (a circle/pill with just a plus) that sits where chips live. Clicking it *engages* the input — reveals a small text box to type the tag; on Enter (or commit) it adds the chip and collapses back to the "+" pill; on blur/escape with no entry it collapses unchanged. So the row shows existing chips + a quiet "+", never an empty open bar. Compact mode keeps chips read-only (no "+").

**4b. Tag filter** — a filter box that narrows the account list by tag (or name), the piece deferred from v1.5.0. The deferral reason holds: a `CollectionViewSource` filter desyncs the drag-to-reorder index math (`OnAccountRowDrop` maps drop position to the source `Accounts` collection). Reorder-safe approach: filter by toggling per-row visibility on a non-persisted `IsFilteredOut` flag (the underlying `Accounts` order is untouched), and **disable drag-reorder while a filter is active** (filtering and reordering at the same time is a non-case). Clearing the filter restores full reorder.

## 5. Fix + restore the Follow feature

> **CORRECTION (2026-05-21):** The feature was NEVER masked in committed source — see [the item-1 diagnostic](../../investigations/2026-05-21-follow-restore-diagnostic.md). Both follow surfaces are visible and wired. The real defect was functional: the Friends-modal path fired the launch with no presence/place guard, so a friend not in a joinable game (or privacy-hidden) silently landed the user at the Roblox home page. The actual item-8 work was porting the land-at-home guard (`EvaluateFollow`) into the Friends-modal path and sharing it with `FollowAltAsync` so the two can't drift — not a 3-XAML-edit unmask. The original framing below is preserved for /reflect-time context.

The friend-follow feature is masked (UI `Visibility=Collapsed`, code intact: `FollowAltAsync`, `OpenFriendFollowCommand`, `FriendFollowWindow`, `LaunchTarget.FollowFriend`) — it was hidden because it was broken, not merely unfinished. **This item is investigate-then-fix-then-unmask**, not a 3-XAML-edit unmask. Build sequence: (a) root-cause why it was masked (presence/join-by-userid path against current Roblox behavior), (b) fix it, (c) unmask. The diagnostic gate comes first; if the root cause turns out deep, it may split into its own follow-up rather than block the cycle.

## Testing

Unit + reconciliation; no E2E against real roblox.com (CLAUDE.md rule).

- **`AccountTransportService`:** round-trip (export→import yields the same records); wrong passphrase throws (no partial data); tampered ciphertext/tag throws; unknown format version rejected; salt/nonce are random per export (two exports of the same data differ).
- **Import merge:** dedupe by userId (existing kept, new added, report counts); empty/garbage file rejected cleanly.
- **Tag filter:** matching rows shown, non-matching hidden, `Accounts` order unchanged; reorder disabled while filtered.
- **Private-server library:** a saved private server selectable as a row target; Launch multiple sends different alts to different targets.
- **Follow:** tests gated on the root-cause finding.

## Out of scope

- **Cloud account sync.** Transport is offline, user-controlled files only. No backend, no 626 Labs storage. (A cloud option is a separate future conversation with real privacy/cost weight.)
- **Per-cookie encryption rework** (vs whole-blob DPAPI). Transport re-keys at the bundle level; the at-rest model stays whole-blob DPAPI. Per-cookie is a deeper change, not needed for transport.
- **Anti-multilaunch installer smoothing** (the Roblox bootstrapper popup) — considered, cut from this bundle; mostly Roblox-side, realistic scope is detect+guide, revisit later.

## Risks / open questions

- **Passphrase loss = bundle loss.** By design (no recovery — a recovery path would mean we hold a key, defeating the point). The export warning must say this plainly.
- **Non-technical users sharing the file.** Mitigated by the warning + enforced passphrase + slow KDF, but social risk remains. Acceptable given the alternative (no transport at all) and the encryption floor.
- **Follow root cause unknown** until the diagnostic pass — it may be a Roblox-side change that's non-trivial. Sequenced first so we learn early.
- **KDF cost on low-end machines:** 600k PBKDF2 iterations is ~hundreds of ms — fine for a one-shot export/import, not a hot path.

## Decisions to log (626 Labs Dashboard)

- Account transport via passphrase-encrypted offline bundle (PBKDF2-600k + AES-256-GCM); merge-by-userId import; full per-account setup travels. Re-key, not file-copy — DPAPI CurrentUser is the reason.
- "Cookies never leave the machine" narrative updated to "never leave unintentionally" — deliberate user-initiated encrypted export. Reviewer-letter + privacy-policy disclosure change.
- Tag filter implemented via per-row visibility + reorder-disabled-while-filtered (not CollectionViewSource) to preserve drag-reorder.
