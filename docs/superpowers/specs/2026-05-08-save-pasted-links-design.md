# Save Pasted Join-by-Link Targets — Design Spec

**Version:** v1.3.x feature add (post default-game-widget + rename)
**Date:** 2026-05-08
**Status:** Approved for implementation planning
**Branch (spec):** `docs/spec-save-pasted-links` (cut alongside spec commit)
**Branch (implementation):** `feat/save-pasted-links` (cut from `main` after spec lands)
**Repo:** https://github.com/estevanhernandez-stack-ed/ROROROblox

## 1. Overview

The per-account row Game dropdown on `MainWindow` ends in a `(Paste a link...)` sentinel that opens `JoinByLinkWindow`. The user pastes a Roblox URL — public game, private server share link, or `roblox.com/share?code=...` — and the dialog returns a `LaunchTarget` for `MainViewModel` to dispatch.

**Today, the paste is one-shot.** The launch happens, but the URL never reaches `FavoriteGameStore` (public games) or `PrivateServerStore` (private servers). To save the same link, the user has to leave the row dropdown, open the **Games window** (`SettingsWindow.xaml` — saves + sets default), and re-paste the URL there. Two surfaces, same paste, different behavior.

**Feedback received:** users want the row-dropdown paste to optionally save, so the link they just used to launch lands in their library and shows up next time without retyping.

The fix is small: an opt-in **"Save to my library"** checkbox on `JoinByLinkWindow`, default unchecked. When checked, after the user clicks Launch and a target is resolved, `MainViewModel` calls the same `AddAsync` the Games window already uses (public games) or that `SquadLaunchWindow` already uses (private servers) — same store, same shape, same downstream consumers.

The technical core is three files and one new bool flag.

## 2. Goals and non-goals

**Goals (v1.3.x):**
- Add an opt-in `Save to my library` checkbox to `JoinByLinkWindow`. Default OFF every time the dialog opens.
- On Launch with checkbox checked: save the resolved `LaunchTarget` to the appropriate existing store (`FavoriteGameStore` for `Place`; `PrivateServerStore` for `PrivateServer`).
- Saved-from-paste items appear in **every existing surface** the corresponding store powers — per-account Game dropdown, default-game widget, Games window's Saved-games list, Library sheet's saved-private-servers section.
- Saving never blocks the launch. If the save throws, log a warning and dispatch the launch anyway.
- Share-token URLs (`roblox.com/share?code=...`) save correctly after server-side resolution to either `Place` or `PrivateServer`.
- Re-saving a URL that's already in the store is a no-op for the user — `LocalName` is preserved by both stores' existing replace-on-duplicate paths (cycle #2 item 2 guarantee).

**Non-goals (v1.3.x):**
- **Persisting the checkbox state across dialog opens.** Default OFF every open; users opt in per paste. Avoids the "I checked it once and now everything pollutes the library" failure mode.
- **"Save without launching" path.** Launch is mandatory — the dialog's reason to exist is launch. Save is the optional side effect.
- **"Save and set as default" combined affordance.** Set-default lives exclusively in the Games window. Keeps `JoinByLinkWindow` focused on save-or-don't and avoids bifurcating the canonical default-setting surface.
- **Toast / snackbar feedback on save.** The list updating in the row dropdown / widget / Library is the feedback. No additional UI noise.
- **Rename-on-save prompt.** Saved-from-paste items default to the Roblox-side name (public games) or `PlaceName` (private servers, mirroring `SquadLaunchWindow.xaml.cs:294`). User can rename later via the existing context menu added in cycle #2.
- **Mac parity.** The Mac sibling repo (`rororo-mac`) has a different favorites surface — that port is a separate cycle.

## 3. Stack

No new dependencies. Reuses what's already in the app:

- `JoinByLinkWindow` — existing modal in `src/ROROROblox.App/JoinByLink/`.
- `IFavoriteGameStore` — `src/ROROROblox.Core/IFavoriteGameStore.cs`. `AddAsync(placeId, universeId, name, thumbnail)`.
- `IPrivateServerStore` — `src/ROROROblox.Core/IPrivateServerStore.cs`. `AddAsync(placeId, code, kind, name, placeName, thumbnail)`.
- `LaunchTarget` discriminated union — `src/ROROROblox.Core/LaunchTarget.cs`. Cases `Place` and `PrivateServer` are the save-eligible ones.
- `IRobloxApi.GetGameMetadataByPlaceIdAsync(placeId)` — already used by `SettingsWindow.xaml.cs:151` and `SessionHistoryWindow.xaml.cs:280` to fetch `GameMetadata { PlaceId, UniverseId, Name, IconUrl }` from a place id. Returns `Task<GameMetadata?>` — null on lookup failure (not an exception). No cookie required.

## 4. Architecture and change surface

Three files. One new bool flag plumbed end-to-end.

### 4.1 `JoinByLink/JoinByLinkWindow.xaml`

Add a `CheckBox` row immediately above the action buttons. Default unchecked. Brand-tokens consistent with the rest of the dialog.

```xml
<CheckBox Grid.Row="3" x:Name="SaveCheckBox"
          Content="Save to my library"
          Foreground="{DynamicResource WhiteBrush}"
          Margin="0,12,0,0"
          ToolTip="Add this game or private server to your saved library so it shows up next time without retyping." />
```

Grid row indices on the existing `Grid` shift by one to make room — the existing `StatusText` (currently `Grid.Row="3"`) becomes `Grid.Row="4"`, and the action-button `StackPanel` (currently `Grid.Row="4"`) becomes `Grid.Row="5"`. Add a sixth row definition (`Auto`) at the top of `Grid.RowDefinitions`.

### 4.2 `JoinByLink/JoinByLinkWindow.xaml.cs`

Expose the checkbox state alongside the existing `SelectedTarget`:

```csharp
public bool SaveToLibrary => SaveCheckBox.IsChecked == true;
```

Set after dialog returns; consumed by `MainViewModel`. No other code-behind changes — the checkbox is independent of URL parsing and target resolution.

### 4.3 `ViewModels/MainViewModel.cs`

In the existing `OpenJoinByLinkAsync` flow, after `dialog.ShowDialog()` returns `true` and `dialog.SelectedTarget` is non-null, but **before** dispatching the launch:

```csharp
if (dialog.SaveToLibrary)
{
    try
    {
        await SavePastedTargetAsync(dialog.SelectedTarget);
    }
    catch (Exception ex)
    {
        // Save failure must NEVER block the launch. Mirrors avatar-fetch soft-fail
        // pattern in MainViewModel:613.
        _log.LogWarning(ex, "Failed to save pasted target {Target}; launching anyway.", dialog.SelectedTarget);
    }
}
// existing launch dispatch unchanged
```

`SavePastedTargetAsync` is a new private method on `MainViewModel`:

```csharp
private async Task SavePastedTargetAsync(LaunchTarget target)
{
    switch (target)
    {
        case LaunchTarget.Place place:
            var meta = await _api.GetGameMetadataByPlaceIdAsync(place.PlaceId);
            if (meta is null)
            {
                _log.LogWarning("Skipping save for pasted Place {PlaceId}: metadata lookup returned null.", place.PlaceId);
                return;
            }
            await _favoriteGameStore.AddAsync(meta.PlaceId, meta.UniverseId, meta.Name, meta.IconUrl);
            await ReloadGamesAsync();
            break;

        case LaunchTarget.PrivateServer ps:
            var psMeta = await _api.GetGameMetadataByPlaceIdAsync(ps.PlaceId);
            if (psMeta is null)
            {
                _log.LogWarning("Skipping save for pasted PrivateServer at place {PlaceId}: metadata lookup returned null.", ps.PlaceId);
                return;
            }
            // Mirrors SquadLaunchWindow.xaml.cs:294 — Name defaults to PlaceName; user
            // can rename later via the existing context menu (cycle #2).
            await _privateServerStore.AddAsync(ps.PlaceId, ps.Code, ps.Kind, psMeta.Name, psMeta.Name, psMeta.IconUrl);
            break;

        default:
            // Other LaunchTarget cases (e.g., FollowFriend if/when the masked feature
            // un-masks) are not save-eligible from this surface.
            break;
    }
}
```

Note `_api` is already a `MainViewModel` field — same instance the launch path uses. Optimization opportunity: if the launch dispatch resolves metadata in a local variable, thread it into `SavePastedTargetAsync` to avoid a second round-trip. Skipping that optimization in the spec because it depends on the exact launch-path shape; the implementation plan can decide.

After save:
- Public game → call `ReloadGamesAsync()` (existing private method) so the new game appears in `AvailableGames` (per-account dropdowns) + `WidgetGames` (default-game widget) immediately.
- Private server → no Main-window-side reload needed. The Library sheet, when opened, lists from `IPrivateServerStore.ListAsync()` directly; saved item appears next open.

## 5. Save semantics + naming

### 5.1 Public games (`LaunchTarget.Place`)

- **Name source:** Roblox-side place name from `IRobloxApi.GetPlaceMetadataAsync` — same source `SessionHistoryWindow.xaml.cs:280` and `SettingsWindow.xaml.cs:158` already use.
- **Universe ID:** required by `FavoriteGameStore.AddAsync` (4-arg). Comes from the same metadata fetch.
- **Thumbnail:** `IconUrl` from metadata.
- **`IsDefault` flag:** **never set on save.** Default-setting stays exclusively in the Games window. A first-paste user with no saved games yet will end up with one game saved, default unset — the Games window's "mark as default" remains the canonical surface.
- **`LocalName`:** never set on save. User applies via the existing Rename context menu after save.

### 5.2 Private servers (`LaunchTarget.PrivateServer`)

- **Name + PlaceName:** both default to the Roblox-side place name (e.g., `"Pet Simulator 99"`). Direct mirror of `SquadLaunchWindow.xaml.cs:294`.
- **Code + CodeKind:** taken from the resolved `LaunchTarget.PrivateServer` — already carries both.
- **Thumbnail:** from `IRobloxApi.GetPlaceMetadataAsync(ps.PlaceId)`.
- **`LocalName`:** never set on save. Rename via context menu after.
- **`AddedAt`:** stamped to `DateTimeOffset.UtcNow` by the store.
- **`LastLaunchedAt`:** **not** stamped here — that's the launch path's responsibility. If the launch dispatcher already calls `TouchLastLaunchedAsync` for resolved private-server targets it's been launched into, that path covers us; if not, the saved record's `LastLaunchedAt` will be `null` until the next launch into that server. Either is acceptable — sort-by-recent still works because save itself stamps `AddedAt`.

### 5.3 Share-token URLs (`roblox.com/share?code=...`)

Resolved by `_resolveShareUrl` to either `Place` or `PrivateServer` at click time. Save uses the resolved kind — share tokens are never persisted as a third type. Same path as the resolved-then-launched flow today; save-checked just adds the `AddAsync` call before launch.

## 6. Re-add idempotence

Both stores already preserve `LocalName` on duplicate-key replace (`FavoriteGameStore.AddAsync` keys by `placeId`; `PrivateServerStore.AddAsync` keys by `(placeId, code)`). This was item 2 of cycle #2 (`docs/checklist.md:25`). Effects:

- Pasting the same public URL twice with checkbox checked → store sees existing entry, replaces it, preserves `LocalName`. User sees no list growth.
- Pasting the same private server URL twice with checkbox checked → same behavior.
- Pasting a URL the user previously saved AND renamed → second save preserves the custom name, refreshes other fields (thumbnail URL might have changed Roblox-side).

This means the save-on-paste behavior is "idempotent and non-destructive" by inheritance from the stores' existing replace contract. No new tests needed for the preservation guarantee — the existing cycle-2 regression tests cover it.

## 7. Error handling

The launch is the user's primary intent. The save is the optional sweetener. **Save failures must never block, prompt, or interrupt the launch.**

| Failure | Behavior |
|---|---|
| `IRobloxApi.GetGameMetadataByPlaceIdAsync` returns null (place not found / network failure / malformed response) | Log warning. Skip save. Launch continues using the already-resolved `LaunchTarget`. |
| `IRobloxApi.GetGameMetadataByPlaceIdAsync` throws (unexpected — interface contract is null-returning) | Caught by the outer `try` in `OpenJoinByLinkAsync`. Log warning. Skip save. Launch continues. |
| `_favoriteGameStore.AddAsync` throws (disk full, permission) | Log warning. Skip save. Launch continues. |
| `_privateServerStore.AddAsync` throws | Same. |
| `ReloadGamesAsync()` throws after a successful save | Log warning. The store has the new entry; the in-memory collections will catch up on next reload. Launch continues. |

No modal, no toast, no inline error in `JoinByLinkWindow` (the dialog is already closed by the time the save runs). The warning shows up in the log file for diagnostics. This mirrors the soft-fail pattern in `MainViewModel.cs:613` where avatar-fetch failure doesn't stop account add.

## 8. Testing

### 8.1 Unit tests (xUnit, `ROROROblox.Tests`)

New file: `src/ROROROblox.Tests/JoinByLinkSaveTests.cs`. Targets the `MainViewModel` save-on-paste branch via the same in-memory store pattern existing tests use (`RenameDispatchTests.cs:43-46` shows the shape).

Cases:

1. **`Place + SaveToLibrary=true`** → `_favoriteGameStore.AddAsync` called once with `(placeId, universeId, name, thumbnail)` matching the metadata fetch result. `_privateServerStore.AddAsync` not called.
2. **`PrivateServer + SaveToLibrary=true`** → `_privateServerStore.AddAsync` called once with `(placeId, code, kind, placeName, placeName, thumbnail)`. `_favoriteGameStore.AddAsync` not called.
3. **`Place + SaveToLibrary=false`** → neither store's `AddAsync` called.
4. **`PrivateServer + SaveToLibrary=false`** → neither store's `AddAsync` called.
5. **Save fault-injection:** `_favoriteGameStore.AddAsync` throws → launch dispatch still occurs, warning logged. Same case for `_privateServerStore.AddAsync`.
6. **Metadata returns null:** `_api.GetGameMetadataByPlaceIdAsync` returns `null` → save skipped (no `AddAsync` called), warning logged, launch dispatch still occurs.
7. **Metadata throws (defensive):** `_api.GetGameMetadataByPlaceIdAsync` throws despite the null-returning contract → caught, warning logged, save skipped, launch dispatch still occurs.

### 8.2 Manual smoke (per spec §8 of canonical design spec)

On a clean Win11 dev box:

1. Open RORORO with at least one signed-in account.
2. Click the per-account Game dropdown → `(Paste a link...)`.
3. Paste `https://www.roblox.com/games/920587237` (or any public place URL). Check `Save to my library`. Click Launch.
4. **Verify:** Roblox launches into Pet Simulator 99 (or the pasted game). Open the per-account Game dropdown again — the pasted game is now in the list. Open the default-game widget — the game is in the dropdown. Open the Games window — the game is in `Saved games`. Default flag remains unchanged from before the save.
5. Repeat with checkbox UNCHECKED and a different public URL — game launches but is **not** added to any list.
6. Repeat with a private server share URL (`roblox.com/games/<id>/<name>?privateServerLinkCode=<code>`) and checkbox checked. Open the Library sheet → saved server appears under saved-private-servers. Repeat with checkbox unchecked → server launches but does not appear.
7. Re-paste an already-saved URL with checkbox checked → no list duplication; if the entry has a `LocalName`, it is preserved.
8. Toggle airplane mode on after pasting a valid URL but before clicking Launch. Click Launch with checkbox checked. Save fails silently (metadata fetch throws); launch is **attempted** (will fail downstream because no network, but the save-fail does not surface). Log shows one warning.

### 8.3 Regression coverage

- Existing `RenameDispatchTests` already cover `LocalName` preservation on re-add. No changes there.
- Existing `FavoriteGameStoreTests` cover the public-game replace path. No changes there.
- Existing `PrivateServerStoreTests` cover the private-server replace path. No changes there.

## 9. Branch + commit plan

**Spec branch:** `docs/spec-save-pasted-links` — single commit landing this spec file. Merged to `main` before implementation begins.

**Implementation branch:** `feat/save-pasted-links` — cut from `main` after spec merge. Suggested commits:

1. `feat(app): JoinByLinkWindow — Save to my library checkbox` — XAML + code-behind, no MainViewModel wiring yet. Dialog returns the new `SaveToLibrary` flag but nothing reads it.
2. `feat(app): MainViewModel — wire save-on-paste for Place + PrivateServer` — implements `SavePastedTargetAsync`, calls AddAsync on the right store, calls `ReloadGamesAsync()` for public games. Fault-tolerant try/catch wrapping.
3. `test(app): JoinByLinkSaveTests — 6 cases covering save branches + fault injection` — adds the new test file, all six cases passing.
4. `docs: README + spec banner-correct (if needed)` — small, only if implementation drifts from spec sections.

PR opens against `main`. CI runs `dotnet test`. Review checklist: visual smoke on the dialog (checkbox aligns with brand tokens, no layout regressions), verify post-launch list updates work for both target kinds.

## 10. Open questions / future

- **`LastLaunchedAt` on private-server save.** As noted in §5.2, we don't stamp `LastLaunchedAt` on save itself — only on launch. If the existing launch path doesn't already touch `LastLaunchedAt` for resolved private-server targets, the user-visible effect is "saved-from-paste private server appears at the bottom of sort-by-recent until next explicit launch into it." Acceptable for v1.3.x; flag if user feedback says otherwise.
- **Auto-set-as-default for first-ever save.** Out of scope for this cycle. If feedback says "first paste should mark default when none exists," that's a one-line conditional in `SavePastedTargetAsync` for a future cycle.
- **Saved-from-paste rename prompt.** Some users may want to nickname at save time (e.g., a private server saved as "Friday Night Squad" rather than the place name). Currently they must rename after via context menu — one extra click. If feedback supports an inline rename field on save, add it as a v1.4 conversation.
- **Cross-surface consistency check.** The Games window's paste-to-save (`SettingsWindow.xaml.cs:158`) currently always saves with no opt-out. Is there value in giving that surface the same checkbox? Probably not — the Games window's purpose **is** to manage saved games, so always-save is correct there. JoinByLink's purpose is launch, so opt-in save is correct here. The asymmetry is intentional.

## 11. Decisions to log on completion

After implementation merges, log to the 626 Labs Dashboard via `mcp__626Labs__manage_decisions log`:

- **Architectural choice:** "Save-on-paste lives in MainViewModel.SavePastedTargetAsync, not in JoinByLinkWindow itself. Reason: dialog stays a dumb intent-collector; persistence side-effects belong to the VM that already owns store references and reload coordination." Tag with bound RORORO project ID.
- **UX choice:** "Save default OFF, no cross-session persistence. Reason: avoids 'I forgot I checked it' library pollution, matches user-stated default."
- **Asymmetry:** "Games window saves always; JoinByLink saves opt-in. Two surfaces, two purposes."
