# ROROROblox — Technical Spec (pointer stub)

Spec-first Cart cycles. Active cycle's canonical spec lives upstream:

→ [docs/superpowers/specs/2026-05-08-save-pasted-links-design.md](superpowers/specs/2026-05-08-save-pasted-links-design.md)

Cycle history (each cycle's canonical spec is its own durable artifact):

- v1.1 core (multi-instance + accounts + distribution): [`2026-05-03-rororoblox-design.md`](superpowers/specs/2026-05-03-rororoblox-design.md)
- v1.2 per-account FPS limiter: [`2026-05-07-per-account-fps-limiter-design.md`](superpowers/specs/2026-05-07-per-account-fps-limiter-design.md)
- v1.3.x default-game widget + local rename overlay: [`2026-05-07-default-game-widget-and-rename-design.md`](superpowers/specs/2026-05-07-default-game-widget-and-rename-design.md) (shipped 2026-05-08 via PR #3)
- v1.3.x save-pasted-links: this cycle

## Section index (for checklist references — current cycle)

- §1 Overview (one-shot paste flow doesn't save; opt-in checkbox lands saves in the existing stores)
- §2 Goals and non-goals (default-OFF checkbox; same stores as Games window + Squad Launch; deliberate exclusions)
- §3 Stack (no new dependencies — `IRobloxApi.GetGameMetadataByPlaceIdAsync` already exists; both stores already exposed)
- §4 Architecture and change surface
  - §4.1 `JoinByLinkWindow.xaml` — `SaveCheckBox` row + Grid row index shifts
  - §4.2 `JoinByLinkWindow.xaml.cs` — `SaveToLibrary` property
  - §4.3 `MainViewModel` — `SavePastedTargetAsync` switching on `LaunchTarget` kind
- §5 Save semantics + naming
  - §5.1 Public games — `FavoriteGameStore.AddAsync(placeId, universeId, name, thumbnail)` from metadata fetch
  - §5.2 Private servers — `PrivateServerStore.AddAsync` mirroring `SquadLaunchWindow.xaml.cs:294`
  - §5.3 Share-token URLs — resolved-then-saved via the same kind branches
- §6 Re-add idempotence (cycle #2 item 2 guarantee — `LocalName` survives replace)
- §7 Error handling (save never blocks launch; null metadata + AddAsync throw + ReloadGames throw all soft-fail)
- §8 Testing
  - §8.1 Unit — 4 store-call cases × `(Place|PrivateServer) × (SaveToLibrary=true|false)` + 2 fault injection cases + 1 metadata-null case
  - §8.2 Manual smoke (clean Win11 box)
  - §8.3 Regression coverage (existing tests cover `LocalName` preservation)
- §9 Branch + commit plan (4 implementation commits on `feat/save-pasted-links`)
- §10 Open questions / future (`LastLaunchedAt` on private-server save; auto-set-as-default for first save; rename-on-save prompt)
- §11 Decisions to log on completion (3 dashboard entries — architectural choice, UX choice, asymmetry rationale)

## What's deliberately not in this cycle

- Persisting checkbox state across dialog opens (default OFF every open)
- "Save without launching" path (launch is mandatory; save is the toggle)
- "Save and set as default" combined affordance (set-default stays in Games window)
- Toast/snackbar feedback on save (the list-update is the feedback)
- Rename-on-save prompt (defer to existing context-menu rename)
- Mac parity (separate cycle on `rororo-mac`)

When build reality drifts from the canonical spec — banner-correct at the top of the canonical spec doc per pattern v from Vibe Thesis (per `CLAUDE.md` "Don't rewrite the canonical spec on drift" rule).
