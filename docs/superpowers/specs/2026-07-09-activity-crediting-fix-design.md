# RORORO — GetAccountActivity crediting fix design

---
**Date:** 2026-07-09
**Status:** Approved-shape (queue spec — build whenever) — mini-spec, ready for a plan
**Author:** The Architect + Este
**Scope:** Fix the flagship v1.8 `host.queries.account-activity` capability so a keep-alive plugin's synthetic input is credited to the RIGHT account. Today it isn't, so ur-AFK re-fires against accounts it just kept alive.
**Origin:** 2026-07-07 plugin host-support audit (report `docs/plugins/host-support-audit-2026-07-07.md`, item 2); code-confirmed 2026-07-09.
**Contract note:** consumed by plugins via the `GetAccountActivity` gRPC query (`plugin_contract.proto:35`) — a fix that changes *which account* gets credited is a behavior change plugins will feel; contract shape unchanged.
---

## 1. Problem (code-confirmed)

`ActivityMonitor.Sample()` (`src/ROROROblox.Core/Diagnostics/ActivityMonitor.cs`) stamps `LastActivityAt` like this:

```
advanced = _input.LastInputTick != _lastSeenInputTick   // GLOBAL system input tick
if (advanced && foreground pid resolves to an account) → stamp THAT account
```

`GetLastInputInfo` returns **system-wide** last-input, not per-window. A keep-alive plugin (ur-AFK) focuses a background alt, sends Space, and restores focus — all inside the 1s sample window. The synthetic Space **does** advance the global tick, but by the time `Sample()` runs, foreground is back on the user's real account. So the tick advance is credited to the wrong account; the kept-alive alt is never stamped, still reads idle past threshold, and the plugin re-fires against it every cycle. ur-AFK ships a client-side workaround with its own regression suite — the tell that this was observed live, not theorized.

## 2. Design decisions

The core cannot infer that ur-AFK synthesized input into a specific window (global tick + foreground is all `GetLastInputInfo` gives). So the fix is an **explicit host signal a keep-alive plugin calls to credit an account**, plus making the query honest about it.

1. **New host capability + RPC: `host.commands.mark-account-active`** (name TBD at plan time; capability-gated, consent-listed). A plugin that keeps an account alive calls `MarkAccountActive(accountId)` immediately after it synthesizes input to that account's window. The host stamps `LastActivityAt = now` for that account directly — bypassing the foreground/global-tick heuristic that can't see plugin-directed input.
   - This is the honest model: the plugin knows which window it acted on; the host doesn't. The plugin tells the host.
   - Capability-gated exactly like the existing input-synthesis capability — a plugin that can synthesize keyboard input to keep an account alive is the same trust class that can mark it active. Consent copy: "let this plugin tell RoRoRo an account is still active (so idle warnings don't misfire)."
2. **`ActivityMonitor` gains `MarkActive(accountId, nowUtc)`** — a single-writer stamp path alongside `Sample()` (same `_records` lock discipline; it's another writer to `LastActivityAt` + re-arms `WarnLatched`). The plugin-host RPC handler calls it.
3. **Foreground heuristic stays** for the human case (a user actually tabbing to an alt) — unchanged. The two paths are additive: human input via foreground/global-tick, plugin-directed input via explicit mark.
4. **The query's honesty:** `GetAccountActivity` already returns `LastActivityAt`; with `MarkActive` feeding it, a kept-alive account now reports recent activity, so a well-behaved plugin stops re-firing. No proto change (same `AccountActivity` message).
5. **No auto-detection of synthetic input** (the core can't distinguish it and shouldn't try — SetWindowsHookEx is the wall). The plugin-tells-host model keeps the core observation-only.

## 3. Non-goals

- No per-window input tracking in core (impossible cleanly + wall-adjacent).
- No change to the human foreground/global-tick path.
- No auto-marking — the plugin must explicitly call the new RPC (a plugin that keeps an account alive but doesn't mark it gets today's behavior, which is correct — the host shouldn't guess).
- No breaking proto change (additive RPC + capability only; `contract_version` bump if the vocabulary version tracks it).

## 4. Architecture

- **Contract:** add `rpc MarkAccountActive(MarkAccountActiveRequest) returns (Empty)` (request = account id) to `plugin_contract.proto`; new capability token in `PluginCapability.cs`; consent copy. `PluginContract` NuGet minor bump.
- **Host:** `PluginHostService` handler → `CapabilityInterceptor` gates it → `ActivityMonitor.MarkActive(accountId, clock.UtcNow)`. Map the plugin's account id (the host exposes account ids to plugins already via the activity snapshot).
- **Core:** `ActivityMonitor.MarkActive` — validate the account is tracked, stamp `LastActivityAt`, clear `WarnLatched` (re-arm), fire the coalesced edge event if it un-crosses (mirror `Sample()`'s edge logic; extract the shared "stamp + re-evaluate" into a private helper so `Sample()` and `MarkActive` share it).
- **Plugin side (ur-AFK, separate repo):** call `MarkAccountActive` after each keep-alive tap; retire its client-side workaround. Out of THIS repo's scope — noted for the ur-AFK follow-up so the two ship coordinated.

## 5. Edge cases

- **Mark for an untracked/exited account** → no-op (account not in `_records`); no throw.
- **Race with `Sample()`** → both are single-writers to `_records`; `MarkActive` takes the same lock/gate `Sample()` uses (or the `SafeSample` overlap-skip pattern extended). No torn state.
- **Plugin marks without consent** → `CapabilityInterceptor` returns `PERMISSION_DENIED` (existing gate).
- **Un-crossing the threshold** → if a marked account was warn-latched, marking re-arms it and (per `Sample()`'s edge model) it can re-warn later — the coalesced event must handle a latch clearing between ticks (already the `else if (WarnLatched) → re-arm` branch; `MarkActive` reuses it).

## 6. Testing

- **Unit (ActivityMonitor):** `MarkActive` stamps `LastActivityAt` for a tracked account; re-arms a latched account; no-ops an untracked id; races with `Sample()` don't tear (single-writer test); a marked account no longer appears past-threshold in the next snapshot.
- **Integration (PluginTestHarness):** a consented plugin calls `MarkAccountActive` over the real named pipe → the account's `GetAccountActivity` snapshot shows recent activity; an unconsented call → `PERMISSION_DENIED`.
- **Manual:** with ur-AFK (updated), a kept-alive account stops re-firing across cycles.

## 7. What ships (this repo)

Proto RPC + capability + consent copy; `PluginHostService` handler + interceptor gating; `ActivityMonitor.MarkActive` + shared stamp helper; PluginContract NuGet bump; tests per §6. Own small cycle (~3-4 tasks). Coordinated follow-up: ur-AFK adopts the RPC and drops its workaround (separate repo, separate task).

**Honest framing:** this doesn't make the core smarter about synthetic input — it gives the plugin that DID the input a truthful way to say so. Observation-only core, explicit plugin signal. The wall holds.
