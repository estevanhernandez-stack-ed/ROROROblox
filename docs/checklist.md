# RORORO — v1.6.0 Account Transport + Bundle Build Checklist

**Cycle:** v1.6.0 (account transport + saved-PS-in-dropdown + tag UI + Follow restore + security pass)
**Cycle type:** Spec-first cycle (pattern mm). Canonical spec: [`docs/superpowers/specs/2026-05-21-rororo-account-transport-and-bundle-design.md`](superpowers/specs/2026-05-21-rororo-account-transport-and-bundle-design.md). [`spec.md`](spec.md) is a pointer-stub.

## Build Preferences

- **Build mode:** Autonomous
- **Comprehension checks:** N/A (autonomous)
- **Git:** Commit after each item. Conventional commits. Branch `v1.6.0-account-transport` (cut from `main`; spec committed).
- **Verification:** Yes — checkpoints. **C1 after item 5** (transport export→import runnable end to end). **C2 after item 8** (private servers + tag UI + Follow). Manual smoke is the v1 trade; no E2E against real roblox.com.
- **TDD:** strict for Core + ViewModel logic (items 2, 3, 6, 7 test-first). UI/DI wiring (items 5, 8) + investigation (item 1) + audit (item 9) are verify-by-running.

## Effort

Bigger than v1.5.0 — crypto + five surfaces. **Total ≈ 8-12 hours.** Heaviest: item 2 (transport crypto, security-sensitive) and item 5 (transport UI). Riskiest unknown: item 1 (why Follow broke — gated first so we learn early).

---

## Checklist

- [x] **1. Follow root-cause diagnostic (GATE — read-only)** → DONE. Finding: Follow is NOT masked in committed code (stale memory/spec). Real issue is functional — Friends-modal follow path lacks the land-at-home guard `FollowAltAsync` has; needs a live two-account smoke to confirm `RequestFollowUser`. Item 8 reshaped to "live-smoke confirm + port the guard (~10-20 lines)". In-cycle. See `docs/investigations/2026-05-21-follow-restore-diagnostic.md`.
  Spec ref: `spec.md > 5. Fix + restore the Follow feature`
  What to build: Nothing yet — investigate. Read `FollowAltAsync`, `OpenFriendFollowCommand`, `FriendFollowWindow`, `LaunchTarget.FollowFriend`, and the presence/join-by-user path. Determine WHY the feature was masked (`Visibility=Collapsed`) — what breaks against current Roblox behavior. Pull any clues from `docs/` + git history + the masked-feature memory. Produce a findings note: root cause, whether the fix is small (lands in item 7 this cycle) or deep (split to its own cycle, descope item 7).
  Acceptance: a written root-cause finding + a scoped go/no-go on item 7. No code changes.
  Verify: findings reported; item 7 scope confirmed before reaching it. Commit: `docs: Follow restore root-cause diagnostic`.

- [x] **2. `AccountTransportService` crypto core (Core, TDD)**
  Spec ref: `spec.md > 1. Account transport > Crypto / Bundle format`
  What to build: `src/ROROROblox.Core/Transport/IAccountTransport.cs` + `AccountTransportService.cs` + `AccountExportRecord` (display name, userId, cookie, tags, fpsCap, captionColorHex, localName, isMain, sortOrder/selected). `Export(records, passphrase) → byte[]` and `Import(byte[], passphrase) → records`. PBKDF2-HMAC-SHA256 @ 600,000 iters, random 16-byte salt; AES-256-GCM, random 12-byte nonce, 16-byte tag. Versioned binary header (magic + formatVersion + iterations + salt + nonce + ciphertext+tag). No DPAPI, no UI — pure crypto + serialization. Never log the passphrase/key/cookie; clear key material after use where the BCL allows.
  Acceptance: round-trip (export→import = same records); wrong passphrase throws (no partial data); tampered ciphertext/tag throws; unknown formatVersion rejected; two exports of identical data differ (random salt/nonce). New tests pass; existing 420 stay green.
  Verify: `dotnet test ROROROblox.slnx --filter "AccountTransport*"`. Commit: `feat(transport): AccountTransportService — PBKDF2 + AES-GCM bundle`.

- [x] **3. `AccountStore` bulk export read + merge import (Core, TDD)**
  Spec ref: `spec.md > 1. Account transport > Import flow / Components`
  What to build: `IAccountStore` gains a bulk export (decrypt the chosen accounts' cookies + settings into `AccountExportRecord`s) and a merge import (`ImportMergeAsync(records)`: for each record whose `RobloxUserId` is not already present locally, add it — re-encrypt its cookie into the local DPAPI `accounts.dat`, persist its settings; skip duplicates; return imported/skipped counts). Reuses the existing DPAPI path.
  Acceptance: export read returns full records for selected ids; merge adds non-dupes, skips existing (by userId), reports counts; round-trips through `AccountTransportService`; old `accounts.dat` unaffected. Tests pass.
  Verify: `dotnet test ROROROblox.slnx --filter "AccountStore*"`. Commit: `feat(transport): AccountStore bulk export + merge-by-userId import`.

- [ ] **4. Transport security review of items 2-3 (TDD-backed hardening)**
  Spec ref: `spec.md > 2. Security pass`
  What to build: Tighten the crypto path before it gets a UI. Confirm KDF iterations are a named constant at the OWASP floor; nonce is unique per export (test); AEAD tag is verified before any plaintext is used; no passphrase/key/cookie reaches logs or exception messages (grep + the `dpapi-cookie-blast-radius` agent against `Transport/` + the new AccountStore paths); key/passphrase buffers cleared after use. Add any missing negative tests.
  Acceptance: dpapi-cookie-blast-radius reports clean on the transport path; no secret in logs/exceptions; nonce-uniqueness + tag-verify tests present.
  Verify: agent report + `dotnet test ROROROblox.slnx --filter "AccountTransport*|AccountStore*"`. Commit: `test(transport): crypto hardening + cookie-leak audit on transport path`.

- [ ] **5. Transport UI — export/import dialogs + passphrase strength meter**
  Spec ref: `spec.md > 1. Account transport > Export flow / Import flow`
  What to build: Export dialog (account checklist defaulting to all; passphrase field with **enforced floor ≥12 + strength meter + confirm**; export disabled until it clears; save-file picker; plain warning on success). Import dialog (file picker; passphrase; calls decrypt → `ImportMergeAsync`; reports "Imported N, skipped M"; clear fail-closed message on wrong passphrase / damaged file). Wire to `AccountTransportService` + `AccountStore`. Brand-styled per 626 tokens. Entry points in Settings (or a dedicated dialog).
  Acceptance: export a bundle, import it on a clean profile → accounts appear; wrong passphrase fails cleanly; merge skips dupes; weak passphrase blocks export.
  Verify: `dotnet build ROROROblox.slnx`; run, export + re-import. **Checkpoint C1.** Commit: `feat(transport): export/import dialogs + passphrase strength gate`.

- [ ] **6. Saved private servers in the per-account dropdown**
  Spec ref: `spec.md > 3. Saved private servers in the per-account dropdown`
  What to build: Populate the per-row dropdown (`AvailableGames`, `MainWindow.xaml:565`) with saved private servers from `IPrivateServerStore` alongside games. Give the dropdown a common item abstraction (or a `FavoriteGame`-shaped wrapper carrying the PS code); selecting a private server sets the row's launch target to `LaunchTarget.PrivateServer`. Render with the server's `RenderName`. No new management UI — rename/remove already exist for `SavedPrivateServer`.
  Acceptance: saved private servers show in the dropdown, named; selecting one launches that account into that private server; games still work; Launch multiple can send different alts to different targets. Tests on the launch-target mapping where feasible.
  Verify: `dotnet test ROROROblox.slnx`; run, pick a saved PS on a row, launch. Commit: `feat(launch): saved private servers selectable in the per-account dropdown`.

- [ ] **7. Tag UI — collapsed "+" chip + reorder-safe filter**
  Spec ref: `spec.md > 4. Tag UI — add-affordance redesign + filter`
  What to build: **7a** — replace the always-open add-tag bar with a collapsed **"+" chip** (tag-shaped pill, just a plus) where chips live; click engages a small inline input; Enter commits + collapses; blur/escape collapses unchanged. Compact mode stays read-only (no "+"). **7b** — a filter box that narrows the account list by tag (or name) via a non-persisted per-row `IsFilteredOut` visibility flag (leave `Accounts` order intact) and **disable drag-reorder while a filter is active**; clearing restores reorder.
  Acceptance: no empty bar — rows show chips + a quiet "+"; clicking "+" adds a tag and re-collapses; filter hides non-matching rows without reordering; reorder disabled while filtered, restored on clear. VM-logic tests for the filter predicate.
  Verify: `dotnet test ROROROblox.slnx --filter "AccountSummary*|TagFilter*"`; run, add a tag via the "+", filter the list. Commit: `feat(tags): collapsed + chip add-affordance + reorder-safe tag filter`.

- [ ] **8. Fix + restore the Follow feature** *(scope gated by item 1)*
  Spec ref: `spec.md > 5. Fix + restore the Follow feature`
  What to build: Per item 1's finding — fix the root cause of the Follow break, then unmask the UI (`Visibility` flips on the friend-follow surface). If item 1 found the cause is deep, this item is descoped to "deferred to its own cycle" and the checklist is updated (per the When-Something-Breaks protocol).
  Acceptance: friend-follow visible + functional (follow an alt into a friend's game), OR a documented descope decision if item 1 gated it out.
  Verify: `dotnet test ROROROblox.slnx`; run, follow a friend. **Checkpoint C2.** Commit: `feat(follow): fix + restore friend-follow` (or `docs: defer Follow restore — root cause deep`).

- [ ] **9. Documentation & Security Verification**
  Spec ref: `spec.md > 2. Security pass` + `spec.md > Testing` + `CLAUDE.md > What NOT to do`
  What to build: Full app-wide `dpapi-cookie-blast-radius` audit (whole app, this is the security-pass cycle). Update the disclosure surfaces for the deliberate-export reality: a note for the next reviewer letter ("cookies leave only on user-initiated, passphrase-encrypted export") + a privacy-policy line. Sync `docs/` (spec status, checklist, spec.md pointer; banner-correct the canonical spec if build drifted). Secrets scan + local-path grep (no `c:\Users\` in committable code). `dotnet list ROROROblox.slnx package --vulnerable`. Confirm `.gitignore` still covers `accounts.dat`, `*.rororo-accounts` export bundles (add if missing — must never be committed), `*.pfx`. Branch ready for PR to `main`. (Version bump + Store/Velopack release is the builder-driven release flow, separate.)
  Acceptance: cookie audit clean app-wide; export bundles gitignored; no secrets/local-paths in staged files; deps clean or documented; docs current. Branch PR-ready.
  Verify: agent report clean; pre-commit hooks pass; `dotnet build ROROROblox.slnx` clean. Commit: `docs: v1.6.0 security pass + docs sync + disclosure updates`.
