# ROROROblox — Technical Spec (pointer stub)

Spec-first Cart cycles. Active cycle's canonical spec lives upstream:

→ [docs/superpowers/specs/2026-05-09-rororo-plugin-system-design.md](superpowers/specs/2026-05-09-rororo-plugin-system-design.md)

Cycle history (each cycle's canonical spec is its own durable artifact):

- v1.1 core (multi-instance + accounts + distribution): [`2026-05-03-rororoblox-design.md`](superpowers/specs/2026-05-03-rororoblox-design.md)
- v1.2 per-account FPS limiter: [`2026-05-07-per-account-fps-limiter-design.md`](superpowers/specs/2026-05-07-per-account-fps-limiter-design.md)
- v1.3.x default-game widget + local rename overlay: [`2026-05-07-default-game-widget-and-rename-design.md`](superpowers/specs/2026-05-07-default-game-widget-and-rename-design.md) (shipped 2026-05-08 via PR #3)
- v1.3.x save-pasted-links: [`2026-05-08-save-pasted-links-design.md`](superpowers/specs/2026-05-08-save-pasted-links-design.md) (shipped 2026-05-08 via PR #5)
- v1.3.x detect-Roblox-already-running + cookie-container fix (cycle 4): [`2026-05-08-roblox-already-running-detect-design.md`](superpowers/specs/2026-05-08-roblox-already-running-detect-design.md) (shipped 2026-05-08 via PR #6)
- v1.3.x persist `RobloxUserId` (cycle 5): [`2026-05-08-persist-roblox-user-id-design.md`](superpowers/specs/2026-05-08-persist-roblox-user-id-design.md) (shipped via friends-list-field-drift)
- v1.3.4 cookie-capture per-capture user-data dir: shipped 2026-05-09 (commit 981068a)
- **v1.4 plugin system (current cycle):** [`2026-05-09-rororo-plugin-system-design.md`](superpowers/specs/2026-05-09-rororo-plugin-system-design.md)

## Section index (for checklist references — current cycle)

The plugin system spec is narrative-shaped (not §-numbered). Heading map for checklist references:

- **Why this exists** — background, Store policy 10.2.2 forcing function, posture decision
- **Locked decisions** — 10-row table covering scope, architecture, surface, trust, install, lifecycle, IPC contract
- **Architecture** — three first-class components (RoRoRo host, plugin EXE, shared `ROROROblox.PluginContract`), named-pipe transport, MSIX size impact
- **Components** — `PluginContract` project shape, `Plugins/` module breakdown (`PluginHostService`, `PluginRegistry`, `ConsentStore`, etc.), plugin-EXE shape, manifest format
- **Data flow** — install (user paste URL) / startup (autostart toggle) / event (push) / UI interaction (callback) / launch trigger (plugin-initiated)
- **Error handling** — 8-row matrix: crash, disconnect, version mismatch, capability denial, consent revoke, missing EXE, schema mismatch, plugin self-update fail
- **Testing** — unit (xUnit, mirrors `AccountStoreTests` patterns) + integration (`ROROROblox.PluginTestHarness` over real named pipe) + manual E2E (clean Win11 VM smoke per release playbook §8)
- **Open questions for next sprint** — versioning policy, sample plugin shape, plugin distribution UX polish, telemetry, cross-machine sync
- **Cross-machine inputs** — auto-keys bundle at `docs/port-reference/auto-keys/v1/` is the canonical first consumer's reference; lands as plugin in `rororo-autokeys-plugin` sibling repo follow-up sprint

## What's deliberately not in this cycle

- **The auto-keys plugin itself.** v1.4 ships the plugin SYSTEM only. Auto-keys is its own follow-up sprint in a sibling repo, fed by the auto-keys bundle + Mac-side vibe-taker writeup.
- **Writes to favorites / accounts / session-history stores.** Plugins observe + add UI + trigger launches; they cannot mutate RoRoRo state. Future v2 conversation if demand surfaces.
- **In-app curated plugin gallery.** v1.4 ships URL-paste install only; gallery is a UX polish for a later cycle.
- **Telemetry on installed plugins.** Privacy-policy implications; deferred until there's an explicit reason.
- **Cross-machine sync of installed-plugin lists.** Out of scope; future when account-sync lands.

When build reality drifts from the canonical spec — banner-correct at the top of the canonical spec doc per pattern v from Vibe Thesis (per `CLAUDE.md` "Don't rewrite the canonical spec on drift" rule).
