# RoRoRo agent-ops surface — design

**Date:** 2026-07-10
**Status:** Proposed
**Contract:** `ROROROblox.PluginContract` 0.6.0 (additive)
**Motivating PR:** [#60](https://github.com/estevanhernandez-stack-ed/ROROROblox/pull/60) — UI handle ownership fail-open

## Why

An internet outage kills the Roblox clients. Este is away from the machine. He reaches his
home box over Chrome Remote Desktop, opens Claude Desktop, and asks Claude to put things back.

Claude's job is recovery and reporting, in that order:

1. See which accounts are alive and which are zombies.
2. Force-close the dead clients.
3. Relaunch each account back into the private server.
4. Watch the plugin that drives the actual gameplay loop, and tell the user how it's going.

**Claude is not the loop driver.** Ur Task is. Ur Task runs its own multi-step macro flow —
a positioning macro that runs once (hatch, launch, dig), then a per-account loop that either
runs a set of actions ending where the next loop starts, or a stay-awake action. Claude
triggers nothing inside that flow. It restores the accounts, then observes and reports.

## The wall

RoRoRo never dispatches a macro. This is a Roblox-relations position, not a technical one,
and the code already holds the line in three places worth naming:

- `system.*` capabilities (`synthesize-keyboard-input`, `read-screen`, `focus-foreign-windows`,
  …) are **disclosure-only**. `PluginCapability.IsHostEnforced()` returns true only for `host.*`.
  RoRoRo tells the user what a plugin will do; it never performs or mediates the act.
- Host→plugin rpcs number exactly three: `OnUIInteraction`, `OnConsentChanged`, `OnShutdown`.
  There is no "run your thing" rpc. Not gated — **absent**.
- Nothing in the contract synthesizes input.

This design adds **reads and one account-ops command**. It does not add an agent→plugin
invoke channel, and nothing here should be read as a step toward one.

Ur Task does not need to be invoked. It already subscribes to `SubscribeAccountLaunched`.
When Claude relaunches the accounts, RoRoRo emits the event, and Ur Task starts its own flow
off its own trigger. The event exists today.

## What already exists

No work required for any of this:

| Need | Existing rpc |
| --- | --- |
| See running accounts (ids, PID, place) | `GetRunningAccounts` (free read) |
| Spot idle / stalled accounts | `GetAccountActivity` |
| Get the private-server share link | `GetCurrentServer` |
| Relaunch an account | `RequestLaunch` |
| Relaunch into a specific server | `RequestLaunchTarget` |
| Credit an account as active (stay-awake loop) | `MarkAccountActive` |
| Let Ur Task self-trigger on relaunch | `SubscribeAccountLaunched` |

`MarkAccountActive` deserves a callout: its contract comment reads *"a keep-alive plugin
credits an account as active after it acts on that account's window (idle heuristic can't see
plugin input)."* That rpc was designed for exactly Ur Task's second macro. Its consent copy is
already written and already honest: *"It cannot see what you type or do — only mark an account
active."*

### Readiness race

After an outage, all accounts relaunch at once and presence lags the process attach. The proto
warns twice: `place_id` / `place_name` are often `0` / empty at launch, and plugins wanting fresh
identity must re-query `GetRunningAccounts`. Any consumer (Ur Task or Claude) must gate on
`place_id != 0` per account before treating a client as in-game. Firing a positioning macro at
eight clients still on the loading screen is how this flow eats itself.

## Gap 1 — stop the zombies

The only missing piece of the recovery loop. **Do not use `IRobloxInstanceStopper`.** That
interface is the blunt instrument: *"stops ALL running clients, tracked or not."* It is the
right tool for the startup leftover modal and the wrong tool for an agent command.

`IRobloxProcessTracker` already provides the precise one, keyed by account:

- `RequestClose(Guid accountId)` — graceful `CloseMainWindow`.
- `Kill(Guid accountId)` — hard kill, documented as *"Use only as a fallback after RequestClose."*
- `Attached` — `IReadOnlyDictionary<Guid, TrackedProcess>`, PID per account.

`MainViewModel` already runs exactly this sequence for the UI's per-account close: try
`RequestClose`, fall back to `Kill`. The rpc reuses that path verbatim — no new Core interface,
no new kill logic.

```proto
// Command surface (additive, NuGet 0.6.0): stop Roblox clients RoRoRo tracks, per account.
// Graceful close first, hard kill as fallback. Never touches untracked processes.
rpc StopAccounts(StopAccountsRequest) returns (StopAccountsResult);

message StopAccountsRequest {
  // Empty = every account RoRoRo is currently tracking. Otherwise only these.
  repeated string account_ids = 1;
}

message StopAccountsResult {
  int32 stopped_count = 1;
  repeated string failed_account_ids = 2;  // unknown, untracked, or kill failed
}
```

- **Capability:** `host.commands.stop-accounts`
- **Consent copy:** *"Allow the plugin to close Roblox clients that RoRoRo launched. Any unsaved
  in-game progress in those clients is lost."*
- **Kind:** `act`. Destructive — the consent string says so plainly.

### On "stop by PID"

The contract speaks `account_id`, never PID. The PID *is* the targeting mechanism — it lives in
`TrackedProcess.Pid` and the tracker kills by it — but it stays internal. Exposing PIDs as a
command input would let a caller pass an arbitrary PID, which is a strictly worse surface than
"an account you already have consent to launch." Per-account targeting, PID-backed underneath.

## Gap 2 — the plugin roster

Claude cannot report on a plugin it cannot see.

```proto
rpc ListPlugins(Empty) returns (PluginList);

message InstalledPluginInfo {
  string plugin_id = 1;
  string name = 2;
  string version = 3;
  string publisher = 4;
  repeated string granted_capabilities = 5;
  bool autostart_enabled = 6;
  bool running = 7;
  int32 process_id = 8;   // 0 when not running
}

message PluginList { repeated InstalledPluginInfo plugins = 1; }
```

- **Capability:** `host.queries.plugins`
- **Consent copy:** *"See which plugins are installed, what each one is allowed to do, and
  whether it's currently running."*
- **Sources:** `PluginRegistry.ScanAsync()` + `ConsentStore` + `PluginProcessSupervisor.RunningPids`.
- **Kind:** `read`.

This discloses one plugin's consent record to another plugin. That is a real disclosure and the
reason it is capability-gated rather than a free read. See open question 3.

## Gap 3 — structured plugin status

The important design call, and the one the current code forces.

**Do not read status back off the UI channel.** Three reasons, all verified in source:

1. `WpfPluginUIHost` is a **v1.4 stub**. It logs every call and wires to nothing. The real
   landing surface is item 16 (Plugins page + tray menu + row-badge plumbing), unshipped.
2. `AddStatusPanel(pluginId, title, bodyMarkdown)` **discards `bodyMarkdown` entirely** —
   it stores `StubElement(pluginId, "StatusPanel", title, null)`. The payload never lands.
3. `IPluginUIHost.Update(string handle, string newLabel)` carries only a label. The typed
   `UIUpdate` spec (menu item / row badge / status panel) has nowhere to go. This is precisely
   why `PluginHostService.UpdateUI` was an ungated no-op — and why it shipped fail-open.

Beyond the stub problem: UI is a presentation layer. Scraping markdown intended for a human
into an agent's context is fragile and lossy by construction.

Instead — **one structured status object, published once, consumed twice**: rendered for the
user when item 16 lands, and served to agents now.

```proto
// Plugin -> host: publish this plugin's own current state. Identity is the caller's
// x-plugin-id; a plugin can only ever publish its own status.
rpc PublishStatus(PluginStatus) returns (Empty);

// Agent -> host: read the latest status every plugin has published.
rpc GetPluginStatus(Empty) returns (PluginStatusList);

message PluginStatus {
  string state = 1;                  // "idle" | "running" | "paused" | "error"
  string summary = 2;                // one-line human-readable summary
  int32  step = 3;                   // optional progress, 0 when unused
  int32  total_steps = 4;
  string current_account_id = 5;     // optional
  map<string, string> fields = 6;    // plugin-defined stats
}

message PluginStatusEntry {
  string plugin_id = 1;              // host-stamped, never client-supplied
  PluginStatus status = 2;
  int64  updated_at_unix_ms = 3;     // host-stamped
}

message PluginStatusList { repeated PluginStatusEntry entries = 1; }
```

- **`PublishStatus` capability:** none. A plugin publishing its own state needs no grant — the
  same reasoning that leaves `Handshake` ungated. The host stamps `plugin_id` from the
  `x-plugin-id` header and ignores any client-supplied value, so plugin A can never write
  plugin B's status. This is the same ownership pattern PR #60 just enforced for UI handles,
  and it must be enforced the same way: at the service layer, not assumed.
- **`GetPluginStatus` capability:** `host.queries.plugin-status`
- **Consent copy:** *"Read the status other plugins publish — what they're doing and how far
  along they are."*
- **Retention:** last status per plugin, in memory, cleared when the plugin process exits
  (`PluginProcessSupervisor.PluginExited`).

Ur Task publishes `{state: "running", summary: "Loop 3 of 8", step: 3, total_steps: 8,
current_account_id: "…", fields: {"mode": "dig"}}`. Claude reads it and tells Este. The user
sees the same object rendered in the status panel once item 16 ships.

## Gap 4 — make the capability map fail closed

This is the root-cause class behind PR #60, and adding four rpcs to the map is the forcing
function to fix it.

`RpcMethodCapabilityMap.Required()` returns `null` for any method not in its dictionary, and
`CapabilityInterceptor.EnforceCapability()` treats `null` as *ungated* and returns early.
An rpc added to the proto but forgotten in the map ships wide open. That is exactly how
`UpdateUI` and `RemoveUI` shipped.

Changes:

- Distinguish **known-and-ungated** from **unknown**. `IsKnown()` already exists and is unused
  by the interceptor.
- Unknown method name → `PermissionDenied`. Fail closed.
- Add a startup assert: every method on the `RoRoRoHost` service has a map entry, or the host
  refuses to start. Enumerate from the generated reflection descriptor — confirm the exact
  accessor (`PluginContractReflection.Descriptor.Services[0].Methods` or the generated
  `RoRoRoHost.Descriptor`) during implementation.

The assert is the deliverable. A map entry can be forgotten; a failing startup cannot.

## Contract versioning

`ROROROblox.PluginContract` **0.6.0** — purely additive (new rpcs, new messages, no field
renumbering). Existing plugins compiled against 0.5.0 keep working. The handshake's
`contract_version` stays `"1.0"`; the NuGet version is what moves.

## Tests

**Unit (`ROROROblox.Tests`)**

- Capability-map exhaustiveness: every `RoRoRoHost` method has an entry. This test fails today
  if anyone adds an rpc and forgets the map.
- Unknown method name → interceptor denies.
- `StopAccounts` dispatches to `IRobloxInstanceStopper`; empty `account_ids` means all.
- `PublishStatus` stamps `plugin_id` from the header and ignores a client-supplied value.
- `GetPluginStatus` returns the latest entry per plugin.

**Harness (`ROROROblox.PluginTestHarness`, real named pipe)**

- `StopAccounts` denied without `host.commands.stop-accounts`.
- `ListPlugins` denied without `host.queries.plugins`.
- Plugin A's `PublishStatus` cannot overwrite plugin B's entry.
- An rpc absent from the capability map is refused, not allowed.

**vibe-access**

Re-run `scan` → `map` → `verify`. The manifest grows from 16 to 20 affordances; every new one
must land `pass` before this is done. The stop affordance is `act` and destructive — its
manifest description must say "force-closes real Roblox clients" the way `request-launch`
already says "launches a real Roblox client — do not call casually."

## Explicitly out of scope

- **Agent→plugin invoke channel.** If that product is ever wanted, it gets its own spec and its
  own threat model. It is not a footnote to a recovery feature.
- **Any input synthesis in RoRoRo.** That is MaCro. The wall holds.
- **Any inbound network service.** Chrome Remote Desktop is the access path. RoRoRo continues to
  listen on a per-user named pipe and nothing else.

## Decisions (2026-07-10)

**1. Per-account targeting, now.** `StopAccounts` targets by `account_id`, backed by the PID in
`TrackedProcess`. No stop-all-or-nothing interim. No `StopByPid` added to
`IRobloxInstanceStopper` — the tracker already does per-account, and the blunt stopper stays
reserved for the startup modal.

**2. Tracked clients only. Orphans are the startup gate's job.** While RoRoRo is running, every
Roblox client on the box is one it launched, so `StopAccounts` never needs to reach an untracked
process. If foreign or orphaned clients do exist, the answer is to restart RoRoRo and let the
existing leftover-processes gate offer "Clean up + continue" or "Continue."

This holds up better than expected, because RoRoRo **re-attaches on restart**:
`RunningRobloxScanner` calls `IRobloxProcessTracker.AttachExisting(account.Id, pid)` for
already-running clients, re-establishing account↔PID tracking across an app restart. So after a
restart the orphans are tracked again, and `StopAccounts` can target them by account. The blunt
`StopAll()` path stays for genuinely unattributable processes.

### Blocking hazard: the startup modal blocks the plugin host

Decision 2's restart path has a defect that lands squarely on the recovery scenario, confirmed
by direct observation on 2026-07-10 while re-verifying the agent-access layer.

`App.OnStartup` shows `LeftoverProcessesWindow` via **`ShowDialog()`** (blocking, nested message
loop) at line ~132. `StartPluginHost()` runs at line ~151. Therefore: **when leftover Roblox
processes exist, the plugin-host pipe never binds until a human dismisses the modal.** The app
log freezes at `StartupGate: mutex acquired with N windowed leftover Roblox process(es)` and no
agent can connect.

That is exactly the condition this design exists to recover from. Restart RoRoRo after an outage —
the zombies are still running — and the agent is locked out of the pipe by a dialog. (The XAML
comment on that window claims it is *"Non-blocking"*. The call site is `ShowDialog()`. The comment
is wrong.)

Two candidate fixes, recommendation first:

1. **Split `StartPluginHost()`.** Bind Kestrel to the pipe *before* the startup gate, and defer
   plugin autostart until *after* the gate resolves. The agent can connect and observe while the
   modal is up; no plugin starts macroing before the user has answered it.
2. **Make the modal actually non-blocking** — `Show()` plus a callback for `CleanUpRequested`,
   matching what its own comment already claims.

Fix 1 is narrower and directly serves the recovery path. Neither is in scope for the stop rpc,
but the recovery story is not real until one of them ships. Tracked as a follow-up.

## Open questions

1. **Roster disclosure.** Should `ListPlugins` redact `granted_capabilities` for plugins other
   than the caller, or is full disclosure to a consented reader acceptable?
2. **Status retention across restart.** Clear on plugin exit, or keep a last-known entry with a
   staleness timestamp so Claude can say "Ur Task died 6 minutes ago"? The second is more useful
   for the exact failure this design exists to report on.

Both gate Gaps 2 and 3 only. Gap 1 (stop) and Gap 4 (fail-closed map) are unblocked.
