# PR draft — feat/default-game-widget-and-rename → main

> Scratch file. Copy into GitHub's PR-create form, then this can be deleted.
> Source URL: https://github.com/estevanhernandez-stack-ed/ROROROblox/pull/new/feat/default-game-widget-and-rename

---

## Suggested title

```
feat(app): v1.3.x cycle — default-game widget + local rename overlay + library sheet
```

(70 chars exactly. Under GitHub's display cutoff.)

---

## Body

```markdown
## Summary

Closes the v1.3.x cycle. Two coupled features ship together because they answer the same complaint: *the game names are long, and the default game is buried in Settings.*

- **Default-game widget** — quick-switch dropdown in `MainWindow` Header Row 2. Reads `IFavoriteGameStore`. Click → list of saved games → pick → that's the new default for Launch As.
- **Local rename overlay** — right-click any saved game, saved private server, or account row → `Rename…` → small popup edits a per-record `LocalName: string?`. Roblox-side names stay untouched. Empty input on save normalizes to null (effective reset).
- **Library sheet** — saved-private-servers section + visible Rename buttons.

Strategic frame: Mac-banner parity, Windows-tailored. The Mac banner is full-width because Mac is single-URL; Windows already has the multi-game library + per-row picker, so the widget is properly secondary chrome.

Release notes already shipped in `71d320f`.

## What ships

**Core (4 commits):**
- `LocalName: string?` field added to `FavoriteGame`, `SavedPrivateServer`, `Account`. Forward/backward-compatible JSON. DPAPI envelope roundtrips clean — verified `01 00 00 00 D0 8C 9D DF` header on `accounts.dat` after property add.
- `IFavoriteGameStore.UpdateLocalNameAsync` + `DefaultChanged` event + re-add `LocalName` preservation across all three stores.
- `IPrivateServerStore.UpdateLocalNameAsync` + same re-add guarantee.
- `IAccountStore.UpdateLocalNameAsync`.
- `RenameTarget` DTO + `RenameTargetKind` enum (lives in Core; see deviation #2 below).

**App (5 commits):**
- `RenameWindow` shared modal. Single small `Window` reused across Game / PrivateServer / Account rename flows.
- `DefaultGameWidget` user control in `MainWindow` Header Row 2.
- `MainViewModel` rename + default-game commands; `AccountSummary.LocalName`.
- Right-click → `Rename…` / `Reset name` context menu on 4 of 5 trigger surfaces.
- `LocalName ?? Name/DisplayName` render coverage on 8 surfaces.

**Library sheet (final commit):**
- Saved-private-servers section in the Library sheet.
- Visible Rename buttons across the saved-server rows.

## Banner-correct deviations from spec

Spec at [`docs/superpowers/specs/2026-05-07-default-game-widget-and-rename-design.md`](docs/superpowers/specs/2026-05-07-default-game-widget-and-rename-design.md) was banner-corrected on 2026-05-08 with four surgical deviations. None change user-visible behavior — they reflect the gap between the spec letter and what actually shipped, per pattern v from Vibe Thesis (don't rewrite top-to-bottom; banner-correct):

1. **`ui:FluentWindow` → plain `Window`.** Existing v1.1 modals (`WebView2NotInstalledWindow`, `RobloxNotInstalledWindow`, `DpapiCorruptWindow`) use plain `Window` with `Background="{DynamicResource BgBrush}"`, not FluentWindow. `RenameWindow` matched the actual shipping pattern instead of the spec letter; chrome consistency preserved (which was the underlying point of the §3 spec line).
2. **`RenameTarget` placement: App → Core.** Pure data with zero UI dependencies; Tests project doesn't reference App, so testability requires Core. `RenameResult` (popup outcome) moved for the same reason.
3. **4 of 5 trigger surfaces wired.** Account row, per-row Game ComboBox dropdown, default-game widget dropdown row, Squad Launch sheet saved-server row — wired. Games settings sheet (`SettingsWindow.xaml` rows) deferred; same set of saved games is renameable via the per-row ComboBox + widget dropdown surfaces, so no user flow is stranded.
4. **8 of 12 render surfaces actually changed; 1 vacuous; 2 deferred.** Tray-menu `Start [Account]` surface anticipated a future tray feature that isn't wired (no per-account tray list). Roblox window title intentionally stays as `DisplayName` because `RunningRobloxScanner` matches by exact title pattern — switching to `RenderName` requires a coordinated matcher update, separate item. Games settings sheet rows + Session History entries deferred (snapshot vs live-lookup distinction defensible either way; spec said live-lookup, snapshot shipped).

## Test plan

**Unit (`dotnet test`):**
- [ ] `RenameDispatchTests` — Game / PrivateServer / Account rename dispatch + LocalName preservation on re-add.
- [ ] `LocalNameSchemaTests` — JSON roundtrip with `LocalName` present and absent.
- [ ] `FavoriteGameStoreTests` — replace-preserves-LocalName guarantee.
- [ ] `PrivateServerStoreTests` — same guarantee.

**Manual smoke (clean Win11 box):**
- [ ] Right-click any saved game → `Rename…` → enter custom name → confirm it shows in widget + per-row ComboBox + Squad Launch sheet.
- [ ] Right-click any saved private server in Squad Launch → `Rename…` → confirm in Library sheet.
- [ ] Right-click any account row → `Rename…` → confirm across row primary label, MAIN-pill, follow-strip chips, compact-mode row.
- [ ] Empty rename input + save → resets to Roblox-side default.
- [ ] Default-game widget → click → dropdown opens → pick game → DEFAULT badge moves.
- [ ] Empty-state widget shows "no saved games yet" CTA when favorites store is empty.
- [ ] Re-add an already-renamed item via the existing add path → custom name preserved.
- [ ] DPAPI roundtrip: open `accounts.dat` after a rename + restart → header bytes `01 00 00 00 D0 8C 9D DF`, decrypt path works.

## Out of scope (deliberate)

- **Mac parity** — separate cycle once Windows ships.
- **Bulk rename / find-and-replace** — one item at a time.
- **Sync to Roblox** — strictly local. Popup's mono-micro `ROBLOX NAME — <original>` reminds the user.
- **New "Saved private servers" settings sheet** — Squad Launch + Library sheet stay the surfaces.
- **Tray-menu account rename surface** — no per-account list to attach to in v1.3.x.
- **Auto-shorten heuristics** — renames are explicit user choices; UI ellipses on overflow.
- **Games settings sheet rename rows** — deferred per banner-correct #3; can land in a follow-up PR using the same shape as Squad Launch's `OnRenameSavedServerAsync` / `OnResetSavedServerNameAsync`.

## References

- Spec: [`docs/superpowers/specs/2026-05-07-default-game-widget-and-rename-design.md`](docs/superpowers/specs/2026-05-07-default-game-widget-and-rename-design.md) (banner-corrected 2026-05-08)
- Process notes: [`process-notes.md`](process-notes.md)
- Build plan: [`docs/checklist.md`](docs/checklist.md)
- Release notes commit: `71d320f`

🤖 Generated with [Claude Code](https://claude.com/claude-code)
```
