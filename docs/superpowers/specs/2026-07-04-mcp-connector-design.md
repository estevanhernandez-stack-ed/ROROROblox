# MCP connector — design (Layer 1 + macro reach)

**Date:** 2026-07-04
**Status:** Approved design, planned. Plans: [host `GetAccounts`](../plans/2026-07-06-mcp-host-getaccounts.md), [ur-task bridge](../plans/2026-07-06-mcp-urtask-bridge-extensions.md), [`rororo-ur-mcp` plugin](../plans/2026-07-06-mcp-connector-plugin.md).
**Origin:** The "AI connector" next-focus vision ([`docs/vision/2026-07-04-ai-connector-and-knowledge-base.md`](../../vision/2026-07-04-ai-connector-and-knowledge-base.md)). This spec is the **MCP connector MVP**, expanded (per Este) to include **macro orchestration** so the first working version does the full internet-outage recovery loop. The weekly-changelog analysis + knowledge base (vision Layers 3-4) stay out of scope.

> **⚠ Plan-time reconciliation (2026-07-06) — two "additive" claims corrected against the code. Scope, tools, and the wall are all unchanged; only the implementation cost was understated.**
> 1. **§5.1 `GetAccounts` is additive to the *contract*, more than additive to the *host wiring*.** The data (`is_main`, `roblox_user_id`) is tracked on `AccountSummary`, but there is **no all-saved provider across the plugin-host boundary** — existing providers filter to running-only and carry no `is_main`. The host plan adds `ISavedAccountsProvider` + adapter + DI + a 10th `PluginHostService` ctor param (~18 test call sites updated).
> 2. **§5.2 `repeat`/`StopMacro` were over-stated.** "The loop already exists internally, just expose it" refers to `AssignmentRunner`, which is **not** on the bridge path — the bridge runs single-pass `SequencePlayer.PlayAsync`, so `repeat` is a new `do/while` in `MacroRunInvoker`. And there is **no playbackId registry** today (`PlaybackId` is generated, never stored; playback is single-flight), so `StopMacro` adds a `playbackId→CTS` registry + an abort seam; id-scoped and all-scoped stop currently resolve to the same active playback.
> The plans implement the corrected shapes. Everything below is the original design and stands.

---

## 1. What this is — the north star scenario

Let **Claude Code / Claude Desktop drive RoRoRo** through an MCP server, across two things: **accounts** (launch, follow) and **macros** (Ur Task). The design target is the recovery loop:

> Internet drops mid-session. A Discord notification says an account is "not in game." Internet comes back; Este remotes in and talks to Claude:
> 1. *"Launch Pokey, Spud, and Clover."*
> 2. *"Run the get-in-position macro on all three."*
> 3. *"Now run the farm macro on repeat."*
> 4. Later: *"Stop everything."*

Claude is the operator; RoRoRo launches the alts; Ur Task's hands run the macros. The MVP works end to end when that loop works.

---

## 2. Home + transport (the load-bearing decisions)

**Home: a consent-gated plugin, out of Store core.** Automation via MCP is the same posture the plugin system was built for — it stays a **direct-download / consent-gated plugin**, unpackaged-only, never the Store binary. Same macro-wall + policy-10.2.2 discipline as the marketplace.

**Transport: stdio, Claude-launched.** The bridge is a RoRoRo plugin (installed via the marketplace, consent-gated) but launched by **Claude** as a stdio MCP subprocess — both Claude Code and Desktop support stdio local MCP servers, so it reaches both with the simplest config. Claude owns the process lifecycle; RoRoRo must be running for the tools to act.

**Feasibility (verified against the code):** the host's `Handshake` ([`PluginHostService.cs`](../../../src/ROROROblox.App/Plugins/PluginHostService.cs)) authenticates by **manifest only** — `FindById(pluginId)` + contract-version match — with **no supervisor token, no per-launch secret**, and the supervisor starts plugins with no special args. The host pipe (`\\.\pipe\rororo-plugin-host`) and the Ur Task bridge pipe (`626labs-ur-task`) are both fixed-name, same-Windows-user-ACL'd. So a **Claude-spawned bridge can connect** to both — its authority comes from being an *installed, consented* plugin, not from who spawned it. Autostart is **off** for this plugin (Claude launches it, not RoRoRo's supervisor).

---

## 3. Architecture

The MCP bridge plugin (new Ur-family sibling repo — working name `rororo-ur-mcp`, final name Este's call) is three units:

- **RoRoRo host client** — a gRPC client of `RoRoRoHost` over `\\.\pipe\rororo-plugin-host`. Handshake as the installed plugin, then account actions.
- **Ur Task bridge client** — a length-prefixed-JSON client of the Ur Task action bridge over `626labs-ur-task`. Macro actions.
- **stdio MCP server** — exposes the tools (§4) over stdio; maps each MCP tool call to a host RPC or a bridge call.

Three repos, all shipped this cycle:
- **RoRoRo** — host contract + host impl gets one additive RPC (`GetAccounts`, §5.1).
- **rororo-ur-task** — the action bridge gets `ListMacros`, a `repeat` field, and `StopMacro` (§5.2).
- **new bridge repo** — the plugin itself (manifest, capabilities, icon via the design skill; the two IPC clients; the MCP server).

---

## 4. MCP tool surface (v1)

| Domain | MCP tool | Params | Reaches |
|---|---|---|---|
| Accounts | `list_accounts` | — | `GetAccounts` (host, new §5.1) |
| Accounts | `launch_account` | account (id or name) | `RequestLaunch` |
| Accounts | `launch_into_game` | account, game (share URL / place id) | `RequestLaunchTarget{share_url}` |
| Accounts | `follow_main` | account | `RequestLaunchTarget{follow_user_id = main's uid}` |
| Accounts | `follow_friend` | account, friend (userId) | `RequestLaunchTarget{follow_user_id}` |
| Accounts | `running_status` | — | `GetRunningAccounts` + `GetCurrentServer` |
| Macros | `list_macros` | — | Ur Task `ListMacros` (new §5.2) |
| Macros | `run_macro` | targets (accounts / foreground), macro (id or name), repeat? | Ur Task `RunMacro` (+ repeat §5.2) |
| Macros | `stop_macro` | playbackId or targets | Ur Task `StopMacro` (new §5.2) |

**Name → id resolution.** Tools accept human names (accounts and macros) and resolve to ids inside the bridge via `list_accounts` / `list_macros`, so Este can say "Pokey" and "the farm macro" rather than Guids. Ambiguous/unknown names return a clear error listing the candidates.

**`follow_main`** resolves the main's `roblox_user_id` from `GetAccounts` (`is_main` + `roblox_user_id`) and calls `RequestLaunchTarget{follow_user_id}`.

---

## 5. Contract extensions (both additive)

### 5.1 RoRoRo host — `GetAccounts`

> **Reconciled with contract 0.4.0 (PR #47, "game identity on account surfaces").** The *running*-account surfaces (`GetRunningAccounts`, `AccountLaunchedEvent`) now already carry `place_id` + `place_name`, so `running_status` can report **which game each running alt is in** — a free win for the recovery scenario ("who fell out of game"). What's still missing is the **all-saved** list with `is_main`, which is exactly what `GetAccounts` below adds. `SavedAccount` stays place-free on purpose: a saved-but-not-running account isn't in a game; live game identity comes from the enriched `GetRunningAccounts`.

Add to [`plugin_contract.proto`](../../../src/ROROROblox.PluginContract/Protos/plugin_contract.proto) (additive + wire-compatible — the `.proto` package stays `rororo.plugin.v1`; ships as a `ROROROblox.PluginContract` NuGet bump to 0.5.0, current is 0.4.0 — same pattern as `GetAccountActivity` at 0.3.0):

```proto
rpc GetAccounts(Empty) returns (SavedAccountsList);

message SavedAccountsList { repeated SavedAccount accounts = 1; }
message SavedAccount {
  string account_id = 1;
  int64  roblox_user_id = 2;   // 0 when not yet resolved
  string display_name = 3;
  bool   is_main = 4;
}
```

Host impl reads the account store (all saved accounts, `is_main` already tracked). New capability `host.queries.accounts` (mirrors `host.queries.account-activity`), consent-gated. This is what makes "launch **Pokey** (not running)" and "follow the main" resolvable — the existing `GetRunningAccounts` only lists running ones.

### 5.2 Ur Task action bridge — `ListMacros`, `repeat`, `StopMacro`

Additive to [`BridgeContract.cs`](%USERPROFILE%/Projects/rororo-ur-task/src/Ipc/BridgeContract.cs) (1.x line, stays back-compatible):

- **`ListMacros`** — new method: request `{contractVersion, method:"ListMacros"}` → response `{ok, macros:[{id, name}]}`. Enumerates the macro library so the connector can resolve names.
- **`repeat` on `RunMacro`** — add `bool Repeat` (loop from the macro's end back to start until stopped). The loop already exists internally (keep-alive / round-robin); the bridge just exposes it. `run_macro(..., repeat=true)` = the scenario's "on repeat."
- **`StopMacro`** — new method: request `{contractVersion, method:"StopMacro", playbackId?, targets?}` → response `{ok}`. Stops a running playback (by id) or all on the given targets. The abort path exists internally (WM_ENABLE_ABORT hotkey); the bridge exposes it.

---

## 6. Consent / the wall (posture unchanged)

Two distinct gates, because the two IPC surfaces have different trust models:

- **RoRoRo host side — hard capability gate.** The account tools use `host.queries.accounts` (new, §5.1), `host.commands.request-launch`, and `host.events.*`. These are **enforced per-RPC by the host's `CapabilityInterceptor`** against the consent the user granted the MCP bridge at install. Ungranted → `PERMISSION_DENIED`. This is the real gate on launch/follow.
- **Ur Task bridge side — same-user pipe + Ur Task's own consent.** The macro tools call Ur Task's `626labs-ur-task` pipe, which today accepts any same-Windows-user connection (this is how Ur OCR already triggers macros). The MCP bridge **never synthesizes input itself** — it *asks* Ur Task, and the user's consent to **Ur Task's** `system.synthesize-*` capabilities is what authorizes the actual keystrokes. So the input-automation consent lives in Ur Task, not the connector.
- **Disclosure.** The MCP bridge plugin's own install + consent sheet discloses, in plain language, that it drives launches/follows and triggers Ur Task macros for an external client (Claude) — so the user is told the connector is a bridge for AI-driven control, even though the macro-trigger itself rides Ur Task's open same-user pipe rather than a RoRoRo capability enum.
- **Follow-up (noted, not v1):** hardening the Ur Task bridge to authenticate/allowlist callers (it already carries `CallerPluginId`) would turn the macro-trigger into a hard gate too. Out of scope for the MVP; the same-user-pipe model is the shipped Ur OCR precedent.

Net: launch/follow are hard-gated by RoRoRo consent; macro input is gated by Ur Task's consent; the connector's install discloses the whole picture. **Core stays macro-free** — Claude driving alts is an explicit, revocable, disclosed plugin, not a hidden automation surface.

---

## 7. Setup UX

1. Install the MCP bridge plugin in RoRoRo via the marketplace → consent sheet (launch/follow/query + trigger-macros) → **autostart off**.
2. Add it to Claude: `claude mcp add rororo -- <bridge exe path>` (Claude Code) / the equivalent `mcpServers` stdio entry (Claude Desktop).
3. Talk to Claude. RoRoRo must be running; the bridge returns a clean "RoRoRo isn't running — open it and try again" when the host pipe is absent, and "Ur Task isn't installed/running" when the macro pipe is absent.

---

## 8. Error handling

| Case | Behavior |
|---|---|
| RoRoRo not running | Every account tool returns a clear "RoRoRo isn't running" error (host pipe absent); no crash. |
| Ur Task not installed / not running | Macro tools return "Ur Task isn't available" (bridge pipe absent). Account tools still work. |
| Unknown / ambiguous account or macro name | Error listing the candidates so Claude can re-ask. |
| Macro bridge busy (a sequence already running) | Surface Ur Task's `refused: busy` verbatim so Claude can decide (stop-then-run). |
| Follow lands at home (friends-only server / privacy) | The host's `LaunchResult.failure_reason` is returned as the tool result — the connector doesn't paper over it. |
| Consent not granted for a capability | The host/CapabilityInterceptor returns `PERMISSION_DENIED`; the tool surfaces "consent not granted for X — grant it in RoRoRo's Plugins window." |

---

## 9. Testing

- **RoRoRo `GetAccounts`** — contract + host impl unit-tested off the account store (id/uid/name/is_main), + a `PluginTestHarness` integration test over the real named-pipe gRPC.
- **Ur Task bridge extensions** — `ListMacros` / `repeat` / `StopMacro` unit-tested in the ur-task repo against its macro library + runner fakes.
- **MCP bridge** — the tool↔RPC/bridge mapping unit-tested against fake host + fake bridge clients (name resolution, error mapping, follow-main uid resolution). Its two IPC clients get an integration test against the real pipes.
- **The MCP surface** — verified with the MCP inspector and a smoke from Claude Code driving the recovery scenario end to end.
- No end-to-end against real roblox.com — launches are the host's existing (already-tested) lane.

---

## 10. Out of scope (YAGNI)

- Vision Layers 3-4 (weekly PetSim changelog analysis, community knowledge base) — separate specs.
- HTTP/SSE transport — stdio covers both Claude clients for the MVP; HTTP is a later option if an always-on RoRoRo-hosted server is wanted.
- Recording macros via Claude, or authoring new macros — the connector *runs* existing Ur Task macros; recording stays Ur Task's own UI.
- Any new input-synthesis path in the connector — it only ever asks Ur Task.
- Driving Ur OCR / Ur AFK — v1 is accounts + Ur Task macros; other-plugin reach is a follow-on.

---

## 11. Decision log (to mirror to the dashboard on build)

- **Home = consent-gated plugin, unpackaged-only** — same wall/10.2.2 posture as the marketplace; automation never in Store core.
- **Transport = stdio, Claude-launched** — reaches both Claude Code + Desktop; feasibility confirmed (host handshake is manifest-only, no supervisor token, so a Claude-spawned but installed+consented bridge connects).
- **Two IPC surfaces** — RoRoRo host gRPC (accounts) + Ur Task bridge (macros); the connector is a client of both.
- **Two additive contract extensions** — host `GetAccounts` (all saved + is_main), Ur Task bridge `ListMacros` + `repeat` + `StopMacro`.
- **Input synthesis stays Ur Task's** — the connector triggers, never synthesizes; two disclosed consents.
- **All-in-one MVP to the recovery scenario** — accounts + macros in one cycle, three repos, contract-first within the plan.
