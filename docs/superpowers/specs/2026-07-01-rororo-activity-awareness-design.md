# RORORO — Core activity-awareness capability (Part A) design

---
**Date:** 2026-07-01
**Status:** Approved (brainstorm complete) — ready for implementation plan
**Author:** The Architect + Este
**Scope:** Part A only. Core per-account window-activity awareness + notify, in the ROROROblox repo. Part B (the keep-active plugin) is a separate repo with its own brainstorm, built against this spec's finalized interface.
**Related:** [`docs/superpowers/HANDOFF-2026-06-30.md`](../HANDOFF-2026-06-30.md), [`docs/superpowers/specs/2026-06-30-rororo-limited-followups.md`](2026-06-30-rororo-limited-followups.md) §7, [`docs/superpowers/specs/2026-05-09-rororo-plugin-system-design.md`](2026-05-09-rororo-plugin-system-design.md)
---

## 1. Problem & context

Batch-launched accounts idle-time-out at roughly 20 minutes. Because they were launched together, their client-driven auto-reconnects fire together — a synchronized reconnect wave that trips Roblox's trust gate (captcha / soft-lock, the same 403 flavor Part A's sibling PR #30 handles). RORORO has **no hook into a running client**: the reconnect is a conversation between the Roblox client and Roblox's servers, so RORORO cannot harden or de-stagger it directly. The durable fix is keeping accounts *active* so they never hit the idle timeout — and acting on a client (focus + input) is input automation, which by the project wall lives in a **plugin**, not core.

That splits the work in two:

- **Part A (this spec, core, this repo):** *awareness + notify.* Track per-account time-since-last-activity, surface it in RORORO's own UI, and expose it to plugins through a consent-gated host query. Pure observation. Wall-clean.
- **Part B (separate repo, own brainstorm):** *acting.* A simple keep-active plugin templated on `rororo-ur-task` — the minimum being focus-the-window + press space (jump), per account, only when idle past a threshold, driven by Part A's data. Consent-gated `system.synthesize-keyboard-input`.

Part A is the foundation: it doubles as detect-and-surface for the Limited/reconnect story (RORORO can warn "these accounts are about to reconnect together" even with no plugin installed), and it is the authoritative source of the account↔activity mapping instead of every plugin re-deriving it.

## 2. Goals & non-goals

**Goals:**
1. Core knows, per managed account, how long since anything interacted with that account's window — input-accurate, not just foreground-accurate.
2. Core surfaces that itself: a passive per-row readout, a passive summary banner, and one mutable interrupting toast when an account crosses a warn threshold.
3. Core exposes the data to plugins through one consent-gated pull query, so a plugin can apply its own policy and act.
4. Every mechanism is TDD-able without a live desktop.

**Non-goals (explicitly out — keep this a single implementation plan):**
- No acting on any client (no focus, no input synthesis). That is Part B.
- No push/stream RPC. Pull query only; a stream is a documented additive future if a plugin ever needs sub-second push.
- No multi-tier thresholds, no per-account threshold in core. One global warn default + a setting; per-account policy is the plugin's job.
- No launch de-stagger / offset logic (a separate lever tracked in followups §7).
- No changes to the Limited/Expired states. Idle is orthogonal and additive.
- No global keystroke hook. See §3.

## 3. The signal — foreground + `GetLastInputInfo`, no keystroke hook

The idle timeout fires on lack of **input**, not lack of foreground, so the truer signal is input attribution. But there are two very different ways to get "input," and only one keeps core's hands off keystroke content.

**Chosen:** `GetLastInputInfo` correlated with the foreground window. `GetLastInputInfo` is a standard Windows idle API returning only the *timestamp* of the last system input (keyboard or mouse) — no hook, no privilege, and it never sees what was typed. Each sample: if the foreground window resolves to a tracked account **and** the system last-input tick advanced since the previous sample, stamp that account `LastActivityAt = now`.

This closes the "foreground but AFK" blind spot that foreground-only would miss (a window held foreground with no input still ages toward idle), without ever touching keystroke content.

**Rejected:** a low-level `WH_KEYBOARD_LL` / `WH_MOUSE_LL` hook. It gives per-event attribution but *receives real keystroke content system-wide* even if discarded — a Microsoft Store review flag and a trust liability for a tool brand-spreading to a non-technical clan, and it edges toward the surveillance shape the wall keeps out of core.

**Consequences of the chosen signal (all acceptable):**
- Input to a *background* window can't be attributed — but you cannot input to a background window without focusing it, so this is a non-case in practice.
- A keep-active plugin's own action (focus the window, then send space) trips `GetLastInputInfo` and makes the foreground the target account, so the plugin correctly resets that account's idle timer as a side effect.
- Mouse movement counts as input; a user wiggling the mouse over a foreground account reads it active. Acceptable — that is engagement.
- A window held continuously foreground but truly AFK still ages toward idle (input, not foreground, is the ground truth). This is the deliberate improvement over foreground-only.

## 4. Architecture & components

**`IActivityMonitor` (`Core/Diagnostics`, DI singleton registered in App's composition root — mirrors `PresenceService`/`RobloxProcessTracker`, keeping it UI-free and testable) — the engine.** One responsibility: hold `last-activity-at` per account and decide when an account crosses the idle warn line.

- **State:** `ConcurrentDictionary<Guid, ActivityRecord>` where `ActivityRecord = { DateTimeOffset LastActivityAt; bool WarnLatched }`.
- **Sample loop (~1s timer):**
  1. `GetForegroundWindow()` → pid via `GetWindowThreadProcessId` → resolve to a tracked account through `IRobloxProcessTracker`.
  2. Read `GetLastInputInfo()`. If the tick advanced since the last sample **and** the foreground window is a tracked account → `LastActivityAt = now` for that account.
  3. For every tracked account, compute `now - LastActivityAt`. Newly `>= WarnThreshold` and not latched → latch + add to a "just crossed" batch. Back under the threshold while latched → un-latch (re-arm).
  4. If the batch is non-empty, raise `WarnThresholdCrossed(batch)` once (coalesced, edge-triggered).
- **Lifecycle wiring:** on `account-launched` seed `LastActivityAt = launchedAt` (a fresh launch counts as active); on `account-exited` drop the record.
- **Testability seam:** every Win32 call and the clock sit behind tiny injectable probes — `IForegroundWindowProbe` (foreground hwnd/pid), `ISystemInputClock` (`GetLastInputInfo` tick), `IClock` (now). Tests feed synthetic foreground/input readings and assert the stamps and edge events; no real desktop required.
- **Exposes exactly two things:**
  - `ActivitySnapshot GetSnapshot()` → `IReadOnlyList<AccountActivity>` of `{ Guid AccountId, DateTimeOffset LastActivityAt, TimeSpan SinceActivity }`.
  - `event EventHandler<IReadOnlyList<Guid>> WarnThresholdCrossed` — coalesced, edge-triggered; drives core's toast + banner.
- **Config:** `WarnThreshold`, default 15 minutes (≈5-minute heads-up before the ~20-minute timeout), injectable and surfaced as a setting.

**`IRobloxProcessTracker` addition.** A reverse lookup `bool TryResolveAccountByPid(int pid, out Guid accountId)`. The tracker already holds the forward map (`AttachExisting(Guid, int)`); add the reverse resolve if it is not already present, and reuse an existing equivalent if it is.

**Existing pieces reused (no rework):** `RunningRobloxScanner` / `RobloxWindowDecorator` ([`src/ROROROblox.App/Tray/`](../../../src/ROROROblox.App/Tray/)) already own the account↔pid/window knowledge and the launch/exit events; the monitor consumes that map, it does not duplicate it. The decorator's single job (reapply titles/colors) is left untouched — the monitor is a separate service, not folded into the decorator (rejected Approach 2).

**Hard wall line:** the monitor only ever *reads* — `GetForegroundWindow`, `GetLastInputInfo`, process-tracker lookups. It never focuses a window, never synthesizes input, never touches a client. Asserted in review.

## 5. Data flow

1. **Launch** → `account-launched` → monitor seeds `LastActivityAt = launchedAt`.
2. **Every ~1s** → monitor samples foreground + last-input, stamps the foreground tracked account if input advanced, evaluates thresholds, raises the coalesced `WarnThresholdCrossed` edge event if anything newly crossed.
3. **Core UI** reads `GetSnapshot()` on its *existing* refresh cadence (decorator/presence, ~1.5s) for the per-row "idle 18m" text — not every second, so the grid does not churn. The warn edge event is what fires the toast + banner.
4. **Plugin (Part B, later)** pulls the snapshot over gRPC every few seconds, applies its own threshold + action.
5. **Exit** → `account-exited` → monitor drops the record.

## 6. Notify UX

Core never acts on the client — "notify" is purely surfacing. Copy stays **factual, not predictive**: "idle 18m" is a fact; "times out at 20:04" is a guess (some games shuffle players to dodge the timeout) and is never shown.

**Row chip — "idle 18m".** A small muted label on the row, shown only for **running/tracked** accounts (a non-running account is not idling toward anything). It is a *modifier*, not a status swap — an account is routinely **In game** *and* **idle 18m** simultaneously; that is exactly the situation being flagged. It renders as its own chip, separate from the `StatusDot` (the dot stays owned by Expired / Limited / InGame — no overloading). The chip appears once idle passes ~1 minute (below that the account is effectively active, chip hidden), formats compactly (`idle 45s` → `idle 18m` → `idle 1h4m`), and tints **amber** once past the warn threshold.

**Banner — coalesced, informational.** A separate strip from the launch-eligibility banner (idle does **not** gate launching — an idle account still launches fine). Reads like `3 accounts idle > 15m — may reconnect together`. Soft hedge ("may") on purpose. Updates on the existing UI refresh cadence.

**Toast — edge-triggered + coalesced + mutable.** Fires off `WarnThresholdCrossed`, one toast per batch (`3 accounts idle > 15m — they may reconnect together`), once per crossing, re-arms only after the account goes active again. A **"Mute idle alerts"** setting silences *only the toast* — the row chip and banner always stay (passive). Toasts default **on**.

**Threshold — one tier, configurable.** `WarnThreshold` default 15 minutes, exposed as a setting. Single warn level; no caution/critical tiers.

## 7. Plugin contract surface

Additive only — no breaking change to existing plugins.

- **`plugin_contract.proto`** ([`src/ROROROblox.PluginContract/Protos/`](../../../src/ROROROblox.PluginContract/Protos/)): new messages `AccountActivity { account_id, last_activity_unix_ms, seconds_since_activity }` and `AccountActivityList { repeated AccountActivity items }`; new RPC `GetAccountActivity(Empty) → AccountActivityList` (reuse the existing empty-request message).
- **`PluginCapability.cs`:** new const `host.queries.account-activity` + a plain-language catalog entry for the consent sheet — *"See how long each account has been idle — timestamps only, never what you type or do."* That honesty is load-bearing given the no-keystroke signal.
- **`RpcMethodCapabilityMap.cs`:** map `GetAccountActivity` → `host.queries.account-activity`, so `CapabilityInterceptor` enforces consent with zero new gating code.
- **`PluginHostService.cs`:** implement the RPC by projecting `IActivityMonitor.GetSnapshot()`; `seconds_since_activity` clamped ≥ 0.
- **`docs/plugins/AUTHOR_GUIDE.md`:** document the new capability (folded into the plan).

**Versioning.** Purely additive, so the Handshake `contract_version` stays **"1.0"** — existing plugins never call the new RPC and keep working. The NuGet package `ROROROblox.PluginContract` is already at **0.2.0** (the plugin-system v1.4 work shipped the additive query/command RPCs there), so this additive RPC bumps **0.2.0 → 0.3.0** (minor/additive); Part B references 0.3.0.

## 8. Error handling & edge cases

- Foreground window is a non-tracked window (Discord, desktop) → nobody stamped; tracked accounts age correctly.
- pid resolves to an account mid-exit → `TryResolveAccountByPid` returns false → skip.
- `GetLastInputInfo` tick wrap (~49.7-day uptime) → reads as one no-advance sample, harmless.
- Monitor empty → `GetSnapshot()` returns an empty list; plugin sees nothing to act on.
- `seconds_since_activity` clamped ≥ 0 (guard against clock skew).
- No consent for `host.queries.account-activity` → `CapabilityInterceptor` returns `PermissionDenied` before the RPC body runs (existing behavior; we only register the mapping).
- Thread-safety: `ConcurrentDictionary`; `GetSnapshot()` returns a point-in-time copy; UI updates marshalled to the dispatcher.
- Monitor disposes its timer on app shutdown.

## 9. Testing strategy (TDD, RED → GREEN per task)

**`IActivityMonitor` units (injected probes):**
- Input advances while account A is foreground → A stamped, B ages.
- Foreground is a non-tracked window → nobody stamped.
- Foreground but input did **not** advance → the foreground account ages (the AFK-foreground case we deliberately close).
- Threshold crossing → `WarnThresholdCrossed` fires once, latches, re-arms after the account goes active again, coalesces a multi-account batch into one event.
- `account-launched` seeds; `account-exited` drops.
- `GetSnapshot()` projection correctness.

**Contract path — `PluginTestHarness` integration (real named-pipe gRPC):**
- A consented plugin calling `GetAccountActivity` receives the projected snapshot.
- A non-consented call is `PermissionDenied`.

**ViewModel:**
- `WarnThresholdCrossed` raises the toast via a mockable toast service, respects the mute setting.
- Row chip shows only for running/tracked accounts; idle formatting (`45s` / `18m` / `1h4m`); amber past threshold.
- Banner text + count.

**Converters:** amber idle-warn brush + chip visibility.

## 10. Scope boundary (restated — what Part A does NOT include)

No client acting (Part B). No stream RPC (pull only). No multi-tier or per-account thresholds in core. No launch de-stagger. No Limited/Expired changes. No keystroke hook.

## 11. Decisions & rationale

| Decision | Choice | Why |
|---|---|---|
| Spec scope | Part A only, now | A and B are two subsystems in two repos; B builds on A's finalized interface. Part A has standalone notify value. |
| Activity signal | Foreground **+ input** | The idle timeout is input-driven; foreground alone misses the AFK-foreground case. |
| Input method | `GetLastInputInfo`, no hook | Input-accurate idle without ever seeing keystroke content. Store-clean, privacy-clean, keeps core off the wall. |
| Notify level | Row chip + banner + toast | User's explicit call for the loudest option; engineered edge-triggered + coalesced + mutable so it does not become wallpaper. |
| Plugin delivery | Pull query | Lighter than a stream for a 20-minute-scale signal; keeps the idle-threshold policy in the plugin, core reports facts. |
| Engine placement | Dedicated `IActivityMonitor` | Single responsibility, testable in isolation; rejected folding into `RobloxWindowDecorator` (muddies its job, couples plugin data to a UI decorator). |

## 12. Open questions / follow-ups

- **Part B (separate repo):** the keep-active plugin — own brainstorm, templated on `rororo-ur-task`, referencing contract 0.2.0.
- **Warn-threshold default:** 15 minutes is a reasoned starting point, not measured against real per-game timeout behavior. Revisit once there is field data.
- **Stream RPC:** only if a future plugin genuinely needs sub-second push; additive when it lands.
- **Launch de-stagger lever:** RORORO's other (imperfect) option to offset the initial timeout waves — tracked separately in followups §7, not in Part A.

## References

- Handoff: [`docs/superpowers/HANDOFF-2026-06-30.md`](../HANDOFF-2026-06-30.md)
- Followups (source of §7): [`docs/superpowers/specs/2026-06-30-rororo-limited-followups.md`](2026-06-30-rororo-limited-followups.md)
- Plugin system design: [`docs/superpowers/specs/2026-05-09-rororo-plugin-system-design.md`](2026-05-09-rororo-plugin-system-design.md)
- Plugin author guide: [`docs/plugins/AUTHOR_GUIDE.md`](../../plugins/AUTHOR_GUIDE.md)
- Template plugin: `C:\Users\estev\Projects\rororo-ur-task` (`ForegroundWatcher.cs`, `PluginClient.cs`, `manifest.json`)
