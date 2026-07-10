# RoRoRo plugin family — host-side support gap audit

> **Date:** 2026-07-08 · **Scope:** what app-side (host) support RoRoRo should add next, driven by what the three shipped plugins (`rororo-ur-task`, `rororo-ur-afk`, `Ur-OCR`) actually need, want, or work around today.
> **Method:** read host capability vocabulary (`PluginCapability.cs`), the gRPC contract (`plugin_contract.proto`, currently NuGet `ROROROblox.PluginContract` 0.4.0), `PluginHostService.cs`, the host-side adapters, `AUTHOR_GUIDE.md`, and the plugin marketplace design (`docs/superpowers/specs/2026-07-04-plugin-marketplace-design.md`); cross-referenced against each plugin repo's manifest, README, docs/specs, source comments, changelogs, and GitHub issues (all three: zero open issues — the signal lives in design docs and source comments, not the tracker).
> **Read-only audit.** No source was modified in any repo.

---

## 1. Per-plugin findings

### 1.1 RoRoRo Ur Task (`rororo-ur-task`, v0.5.0, contract NuGet 0.4.0)

**What it does:** per-window-aware macro recorder/player for RoRoRo-managed alts — global keyboard/mouse capture, round-robin playback assignment, AFK keep-alive, window-relative mouse coordinates, STACK/GRID arranging, shareable macro bundles, host-theme following, and an action-bridge named pipe so sibling plugins (Ur-OCR) can trigger a macro.

**Declared capabilities:** `system.synthesize-keyboard-input`, `system.synthesize-mouse-input`, `system.watch-global-input`, `host.events.account-launched`, `host.events.account-exited`, `host.ui.row-badge`, `host.ui.tray-menu`. Notably does **not** use `host.commands.request-launch`, `host.commands.launch-target`, `host.queries.current-server`, or `host.queries.account-activity` — it's a keyboard/mouse macro plugin, not a launcher or activity consumer.

**What it needs/wants that the host doesn't provide:**

- **UI capabilities are dead weight.** `host.ui.row-badge` / `host.ui.tray-menu` are declared but README.md:28-29 states plainly they render "in v1.4.1+ when RoRoRo's host-side UI lands" — confirmed still true: `WpfPluginUIHost` (`src/ROROROblox.App/Plugins/Adapters/WpfPluginUIHost.cs`) is still a logging-only stub as of the current `main`, and it's still the sole DI registration (`App.xaml.cs:541-542`). The plugin never even calls `UpdateUI`/`AddTrayMenuItem` in source — it built its own tray icon and window from scratch instead, because there's nothing to plug into.
- **No host-pushed foreground/focus event.** The plugin polls `GetForegroundWindow()` client-side on a 250ms UI timer (`PluginRuntime.cs:190`) and cross-references the pid against its own `AccountRegistry` (built from `SubscribeAccountLaunched`/`Exited`). Works, but it's the exact kind of polling a host event would replace.
- **Game-identity race forces a re-poll.** `AccountLaunchedEvent.place_id`/`place_name` are "often still 0/empty at launch time (presence lags the process attach)" per the proto's own comment. Ur Task compensates with `RefreshRunningAccountsAsync` (`PluginClient.cs:116-139`) — a manual `GetRunningAccounts()` re-fetch to backfill presence after the fact. Ur Task is also the plugin that **proposed and shipped** the `place_id`/`place_name` fields (contract 0.4.0, PR-coordinated with this repo) — it already asked once and got the fields; the ordering gap is what's left over.
- **No host theme query/subscription exists.** The plugin reads RoRoRo's `%LOCALAPPDATA%\ROROROblox\settings.json` + `themes\*.json` directly off disk via `FileSystemWatcher` (`HostThemeService.cs`), and hand-mirrors RoRoRo's three built-in theme palettes as hardcoded constants (`HostThemeReader.cs:46-61`) with an explicit code comment flagging drift risk if the host's built-ins ever change.
- **Cross-plugin consent gap (design doc, not yet built).** `docs/superpowers/specs/2026-07-06-recipes-macro-slots-design.md:57-70` — Ur Task wants to push OCR trigger/region config into Ur-OCR (plugin-configures-plugin). The author weighed a new PluginContract-level capability ("Ur Task wants to configure Ur OCR") against a weak shared-drop-folder fallback, and flagged the cross-plugin-consent story as "hairy." No host mechanism for plugin-to-plugin capability grants exists today.
- **Host-side install bug (older versions):** README documents that RoRoRo 1.4.0–1.4.2 "silently accept the install and then ask you to restart RoRoRo, and that restart is known to fail" — historical, worth confirming closed.
- **Host-UX ask, filed only in a session-handoff doc:** per-plugin Launch button on the Plugins window rows, because a fresh install with autostart-off requires a toggle-then-restart dance today.

### 1.2 RoRoRo Ur AFK (`rororo-ur-afk`, v0.5.2, contract NuGet 0.4.0)

**What it does:** single-purpose anti-idle keep-alive — every 60s calls `GetAccountActivity`, computes which enabled accounts are idle past a threshold (15 min default + jitter), focuses each due account and taps Space, restores focus. Gates itself to `minHostVersion 1.8.0.0` (the release that shipped `GetAccountActivity`).

**Declared capabilities:** `system.synthesize-keyboard-input`, `host.events.account-launched`, `host.events.account-exited`, `host.queries.account-activity`. No mouse, no `watch-global-input`, no `host.ui.*` at all — it builds its own floating "activity pill" UI.

**The v1.8 `GetAccountActivity` consumption check — this is the important one.** Wiring is clean (`GetAccountActivity` is the plugin's single source of truth, no local idle timers per `KeepActiveService.cs:12`), but there is a **real, well-documented host-behavior bug it had to work around**:

> `KeepActiveService.cs:141-157` (`EffectiveIdleSeconds`): "The host only credits activity to the account whose window is FOREGROUND when its sampler ticks — and a grab flips focus for barely a second, so the synthetic Space usually goes uncredited and the host's idle clock keeps climbing right through a successful grab... observed live: fire at 10m, again at 11m."

This is corroborated by a dedicated regression test suite (`GrabRefireRegressionTests.cs`) and a changelog entry (v0.4.1, "No more back-to-back fires") calling it a bug "latent since v0.1; invisible until you could see fires happen." The plugin's fix is entirely client-side: `EffectiveIdleSeconds = min(host-reported idle, time since our own last successful grab)`. **The host's activity sampler appears to be tick-based and foreground-window-gated, so synthetic input that lands off-tick or off-foreground is silently not credited** — this is the single clearest host-side correctness gap surfaced by any of the three plugins.

**Other notes:**
- No hardcoded/faked fields — `GetRunningAccounts` "list saved accounts" gap is a non-issue (ur-afk only tracks running accounts via the event streams, never needed the full inventory).
- Sidesteps the "consent revocation doesn't kill open streams" gap for `account-activity` specifically by using a unary poll (not a subscription) and explicitly catching `PermissionDenied` every cycle — but its `SubscribeAccountLaunched`/`Exited` streams have no such handling, so that gap still applies there.
- No self-update mechanism; relies entirely on RoRoRo's installer + the standard three-artifact marketplace convention (README/DEV.md confirm clean compliance).
- `docs/DEV.md` still describes an already-completed "swap to PackageReference 0.3.0" checklist as pending — stale doc, not a real blocker.

### 1.3 Ur-OCR (`Ur-OCR`, v0.4.0, contract NuGet 0.3.0)

**What it does:** watches user-defined screen regions for OCR'd text or a pixel color, fires a keybind — or, since v0.3.0, a Ur Task macro over its own private named pipe — on a no-match→match transition, with an optional account-aware gate.

**Declared capabilities:** `system.read-screen`, `system.synthesize-keyboard-input`, `host.events.account-launched`, `host.events.account-exited`, `host.ui.tray-menu` (declared but, same as Ur Task, never actually wired to a host RPC — it builds its own tray icon).

**Window-to-account correlation is a non-issue.** Never calls `GetRunningAccounts`; subscribes to the launched/exited event streams to build a `pid → RobloxUserId` map, then resolves `hwnd` via plain `Process.GetProcessById(pid).MainWindowHandle` — no `FindWindow`, no guessing. This is the cleanest of the three integrations and needs nothing further from the host today.

**Two forward-looking asks:**
- The plugin-to-plugin macro bridge to Ur Task runs over an **unconsented private named pipe** (`\\.\pipe\626labs-ur-task`), explicitly because a `plugins.send-run-requests`-shaped host capability doesn't exist yet — the plan doc says outright "do NOT add it to the manifest… capability string is deferred." Same gap Ur Task's design doc flags from the other side (§1.1).
- Same theme-mirroring workaround as Ur Task (hand-copied built-in palettes off a disk read), with its own doc naming the fix: **"Cheap future fix host-side: publish built-ins as JSON in the themes folder so plugins read them like user themes."**

Contract NuGet is 0.3.0 — one minor behind Ur Task/Ur AFK's 0.4.0, but the plugin doesn't need the 0.4.0 `place_id`/`place_name` fields, so this isn't friction, just a versioning-state fact worth tracking (§3).

---

## 2. Cross-cutting gaps (needed by 2+ plugins — highest host-add value)

| Gap | Who hits it | Evidence |
|---|---|---|
| **Host-side UI rendering is a total no-op.** `host.ui.tray-menu` / `row-badge` / `status-panel` are declared, consent-gated, and accepted by the RPC — but `WpfPluginUIHost` paints nothing. | Ur Task, Ur-OCR (both declare `host.ui.tray-menu` and get zero rendering; both had to build fully separate tray icons + windows instead) | `WpfPluginUIHost.cs` (still stub); both READMEs say so explicitly |
| **Built-in theme palettes have no host-published source of truth.** Both plugins read RoRoRo's `settings.json`/`themes/*.json` off disk and hand-mirror the three built-in palettes as hardcoded constants, with acknowledged drift risk if RoRoRo's built-ins ever change. | Ur Task, Ur-OCR | `HostThemeReader.cs:27-29,46-61` (Ur Task); `docs/theming-sync.md:82-85` (Ur-OCR, names the exact fix) |
| **No foreground/focus-change host event.** Both Task and OCR independently reimplement "which alt window is focused right now" via `GetForegroundWindow` + pid cross-reference against their own account registries built from the launch/exit streams. | Ur Task (250ms poll timer), Ur-OCR (on-demand resolver) | `PluginRuntime.cs:190`, `ForegroundWatcher.cs` in both repos |
| **No plugin-to-plugin capability/consent model.** The one cross-plugin bridge that exists (Ur-OCR → Ur Task macro trigger) runs over a private, unconsented named pipe because there's no host-mediated way to grant one plugin permission to call another. | Ur-OCR (built it), Ur Task (wants to extend it for config-push) | `docs/superpowers/plans/2026-06-29-ur-ocr-bridge-client.md:15` (Ur-OCR); `docs/superpowers/specs/2026-07-06-recipes-macro-slots-design.md:57-70` (Ur Task) |
| **Mid-stream consent revocation doesn't close open subscription streams (known v1.4 gap, still open).** | Ur Task and Ur AFK both leave `SubscribeAccountLaunched`/`Exited` streams unguarded against a live revoke | AUTHOR_GUIDE.md "What you can NOT do (yet)"; confirmed no revoke-handling in either plugin's stream consumer |

---

## 3. Contract / versioning state

- **Wire contract:** all three plugins pin `contractVersion: "1.0"` in their manifests — no drift, no rejected handshakes expected.
- **NuGet package (`ROROROblox.PluginContract`):** currently ships **0.4.0** (added `place_id`/`place_name` to `RunningAccount`/`AccountLaunchedEvent`, proposed and co-designed by Ur Task's author).
  - Ur Task: 0.4.0 (current)
  - Ur AFK: 0.4.0 (current)
  - Ur-OCR: 0.3.0 (one minor behind; doesn't need 0.4.0's fields, so functionally fine)
- **Documentation drift:** `docs/plugins/AUTHOR_GUIDE.md`'s capability table only documents NuGet versions through 0.3.0 (`account-activity`). It does not mention the 0.4.0 `place_id`/`place_name` game-identity fields at all — a real author-facing doc gap given one of the three shipped plugins is built directly on top of that bump.
- **`minHostVersion` gating works as designed** — `PluginInstaller.cs:87-102` refuses installs cleanly with an "update RoRoRo" message when the manifest requires a newer host; Ur AFK uses this correctly (`minHostVersion: 1.8.0.0` for `GetAccountActivity`).
- **Marketplace (v1.9) release-asset convention:** all three plugins comply cleanly with `manifest.json` + `manifest.sha256` + `plugin.zip` — zero friction found on this specific mechanic. The one adjacent product-level note: the marketplace (browse/one-click install/update) is gated to **unpackaged builds only** per the compliance design (`docs/superpowers/specs/2026-07-04-plugin-marketplace-design.md`) — Store-installed and sideload-MSIX users, i.e. most real end users, still only get the original paste-URL install flow. Not a plugin-side complaint (none of the three repos mention it), but worth naming since it caps who actually benefits from v1.9's UX win.

---

## 4. Ranked recommendation — top host-side additions

1. **Ship real host-side UI rendering for `host.ui.tray-menu`/`row-badge`/`status-panel` — replace the `WpfPluginUIHost` stub.** Highest value: the entire consent/contract/translator pipeline for this is already built and two of three shipped plugins have already paid the capability-declaration cost banking on it. Today every plugin is forced to duplicate a full tray icon + window instead of contributing to RoRoRo's own surfaces, which fragments the UX the plugin system was designed to unify. Effort: medium — wire `WpfPluginUIHost` to `Tray.ITrayService` + a MainWindow row-badge overlay + a status-panel pane; the RPC/consent layer needs no changes.

2. **Fix `GetAccountActivity`'s idle-crediting semantics** so a synthetic input event is credited to the account it targeted regardless of sampler-tick timing or foreground-window coincidence. Value: high — it's a correctness bug in a capability RoRoRo shipped one release ago specifically for this use case, and ur-afk's client-side `min(host-idle, time-since-own-grab)` workaround is a symptom, not a fix; any future activity-aware plugin hits the same bug. Effort: low-medium — likely a change to how/when the host's activity tracker records "last seen" per account.

3. **Publish the three built-in theme palettes as read-only JSON under the themes folder** (or a trivial `GetTheme`/`GetBuiltInThemes` query) instead of leaving plugins to hand-mirror hardcoded copies off a private settings-file read. Value: medium, rising — two plugins already carry acknowledged drift risk, and Ur-OCR's own docs name the exact fix. Effort: low — the values already exist in `ThemeStore.BuildBuiltIns`; just serialize them where the `FileSystemWatcher` plugins already look.

4. **Design a host-mediated plugin-to-plugin capability/consent surface** (e.g. `plugins.send-run-requests`) so cross-plugin bridges (Ur-OCR → Ur Task today, Ur Task → Ur-OCR config-push planned) go through RoRoRo's consent sheet instead of a private unconsented named pipe. Value: medium — only one live pair today, but it's the "family arc" thesis's whole point (perception→action chaining), and an ungoverned inter-plugin channel is a trust-model hole RoRoRo would otherwise own. Effort: medium-high — needs new capability semantics plus a discovery/consent UX, not just a proto message.

5. **Emit a presence/game-identity follow-up event** (e.g. `AccountPresenceUpdated`) instead of requiring plugins to re-poll `GetRunningAccounts` to backfill `place_id`/`place_name` after `AccountLaunchedEvent` fires with them empty. Value: low-medium — only Ur Task hits this today, and it already has a working poll-based fallback. Effort: low — the host already tracks the presence update internally; it's one more event stream on data that's already flowing.

---

**A 626 Labs product · *Imagine Something Else*.**
