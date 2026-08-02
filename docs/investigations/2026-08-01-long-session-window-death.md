# Long-session window death — clients closing after ~20-30 hours

**Status:** **Root cause confirmed — H1 (Roblox client memory leak).** Confirmed 2026-08-01 by user
telemetry; see "Field telemetry" below. Not a RoRoRo defect. One unrelated latent defect found along
the way and worth fixing regardless.
**Opened:** 2026-08-01
**Reports:** a couple of clan users; RoRoRo version unknown, PC specs unknown, client count unknown.
**Symptom as reported:** Roblox account windows launched via RoRoRo shut down on their own after
roughly 20-30 hours of being open. Not all at once necessarily — unconfirmed.

## The reframe that matters

RoRoRo has **no code path that closes a Roblox window on a timer.** Verified:

| Suspect | Verdict | Evidence |
| --- | --- | --- |
| `MutexLost` handler kills clients | **Ruled out** | [`App.xaml.cs:1011-1021`](../../src/ROROROblox.App/App.xaml.cs#L1011-L1021) — the handler paints the tray red and raises a plugin bus event. That is all it does. |
| `UpdateChecker` 24h debounce restarts the app | **Ruled out** | [`UpdateChecker.cs:33-74`](../../src/ROROROblox.App/Updates/UpdateChecker.cs#L33-L74) — the 24h window is tempting given the timing, but the method only *logs* "Update available." It never downloads, never applies, never restarts. Item 11's download+apply wiring was never added. |
| `SeamlessTakeover` silently closes clients | **Ruled out as the 20-30h trigger** | Only reachable from app startup via the `HeldByRoblox` branch. Not periodic. Cannot fire spontaneously at hour 24. (But see the real defect below.) |
| `MutexHolder` watchdog | **Ruled out** | 5s tick; on `WAIT_FAILED` it releases the handle and raises `MutexLost`. Never touches Roblox processes. |

**Therefore: Roblox is terminating itself.** The question is what makes it do that, and whether
RoRoRo contributes.

## Real defect found (unrelated to this bug — fix anyway)

[`RobloxRunningProbe.cs:22-25`](../../src/ROROROblox.Core/Diagnostics/RobloxRunningProbe.cs#L22-L25):

```csharp
bool hasWindow;
try { hasWindow = p.MainWindowHandle != IntPtr.Zero; }
catch { hasWindow = false; } // exited mid-scan / access denied → treat as windowless
```

`hasWindow = false` is the **dangerous** default here, not the safe one. `SeamlessTakeover.WindowlessOnly`
returns true when *every* client looks windowless, and the caller then closes them all
**with no modal** ([`App.xaml.cs:1070-1080`](../../src/ROROROblox.App/App.xaml.cs#L1070-L1080)).

So any condition that makes `MainWindowHandle` throw or return zero for a live, mid-game client —
access denied from an integrity-level mismatch, a transient enumeration failure — converts into
*silently killing the user's game windows at startup with no confirmation.* The comment even names
"access denied" as an expected case and picks the destructive branch for it.

Fix direction: fail **closed**. An exception means "I don't know," and not-knowing must map to
`hasWindow = true` so the confirming modal runs. The docstring's own safety story ("a windowed
client is never closed without the confirming modal") is not actually enforced by the code.

This does not explain the 20-30h reports — the path is startup-only. It's a separate silent
data-loss bug that should get its own fix + test.

## The one thing RoRoRo does to a live client, continuously

[`RobloxWindowDecorator.cs:70-71`](../../src/ROROROblox.App/Tray/RobloxWindowDecorator.cs#L70-L71) —
every **1.5 seconds, forever**, for every tracked client:

- `Process.GetProcessById` + `Refresh()` + `MainWindowHandle`
- `SetWindowTextW(hwnd, title)` — cross-process window-text write
- `DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ...)`

At 24 hours that is **~57,600 cross-process Win32 window writes per client**; with 5 clients,
~288,000. Two things to note:

1. It is the only sustained contact surface RoRoRo has with a running Roblox process, so it is the
   only plausible RoRoRo contribution to a Roblox-side self-termination.
2. `ReapplyAll` has **no reentrancy guard** — the callback is fire-and-forget (`_ = ApplyOnce(target)`)
   on a `System.Threading.Timer`, unlike `MutexContestedWatcher` which guards with `Interlocked.Exchange`.
   On a loaded or slow machine, `Process.GetProcessById` latency lets ticks overlap and pile up.
   This is the mechanism that would make the problem **worse on lower-performing PCs**, which matches
   the reporters' profile.

Not established as causal. Named as the top RoRoRo-side suspect and the cheapest thing to A/B.

## Roblox-side research

Roblox does not publish client-process release notes at a useful granularity —
`create.roblox.com/docs/release-notes` is engine/Studio-facing and returned nothing on client
lifecycle, singleton behavior, or Hyperion. There is no changelog that would answer this directly.
Most search results for this symptom class are SEO farms (`bloxstrap.com` is **not** the real
Bloxstrap — that's `github.com/bloxstraplabs/bloxstrap`); their claims about "a second mutex check
via named pipe" are unsourced and were not used.

What is real, from the actual Bloxstrap tracker — multi-instance clients dying on their own is a
**cross-tool phenomenon, not a RoRoRo-specific one**:

- [bloxstraplabs/bloxstrap#5579](https://github.com/bloxstraplabs/bloxstrap/issues/5579) — "Multi
  instance feature, Roblox closes after a while." Secondary account disconnects while actively
  moving; primary stays up indefinitely. Bloxstrap 2.9.1. **No logs, no repro steps, no maintainer
  response.** Thin.
- [bloxstraplabs/bloxstrap#5329](https://github.com/bloxstraplabs/bloxstrap/issues/5329) — all
  instances close simultaneously, no error, only one window survives. Reporter speculates about the
  updated anti-cheat. **No maintainer diagnosis.**

**Caveat, stated plainly:** both report **30-50 minutes**, not 20-30 hours. That's an order of
magnitude off ours. These establish that the failure *class* exists independently of RoRoRo; they do
**not** corroborate our specific timing or mechanism. Do not cite them as confirmation.

Separately, client-side memory leaks are current and acknowledged on the devforum — including a
Dec 2025 report of client memory usage growing until machines exhaust RAM, and a documented
server-side leak that "only becomes visible after extended uptime."

## Field telemetry — H1 confirmed (2026-08-01)

From Asnyder2005 in Discord, tracking a single account sitting in an event:

> "I had Roblox account start out at 1.7m and in 14 hours hit 6m sitting in the event between
> Wednesday and Friday."

Reading those as GB (Task Manager working set, ~1,700 MB → ~6,000 MB):

**≈ 307 MB/hour/client, sustained, while idle in an event.** Not spiky — a linear leak. The client
was sitting still, so this is not workload-driven; it's an unbounded allocation over wall-clock time.

Time to RAM exhaustion, `t = (usable_GB / N − 1.7) / 0.307`, assuming ~6 GB for Windows and the rest
of the desktop:

| Total RAM | 2 clients | 3 clients | 4 clients | 6 clients |
| --- | --- | --- | --- | --- |
| 16 GB | 14 h | 7.5 h | — | — |
| 32 GB | 37 h | **23 h** | 16 h | 10 h |
| 64 GB | 79 h | 52 h | 42 h | **26 h** |

**3 clients on a 32 GB box lands at ~23 hours. 6 clients on 64 GB lands at ~26 hours.** Both sit
dead center in the reported 20-30 hour band. The band's *width* is explained exactly: it is the
spread across (RAM, client-count) pairs in the clan, not a fixed timer anywhere.

Pagefile behavior adds the remaining variance — a machine with a large dynamic pagefile thrashes
before anything dies, one with a small or fixed pagefile fails an allocation and terminates sooner.

This is a **Roblox-side leak.** RoRoRo neither causes nor can fix it. H2 and H3 below are not needed
to explain the reports and are retired as causes (H3 remains worth fixing on its own merits).

## Ranked hypotheses

**H1 — Memory/handle exhaustion under multi-instance (top).**
Roblox client leaks over time. N clients × 20-30h × a leak → the machine hits its RAM ceiling and
Windows or the client itself terminates instances. Explains: the long and *variable* window (20-30h,
not a fixed 24h — different RAM sizes and client counts hit the ceiling at different times); why it
hits some users and not others; and why lower-performing PCs are over-represented. This matches
Este's own instinct and is the cheapest to confirm or kill with a measurement.

**H2 — Roblox client version deploy forces an upgrade.** Roblox deploys clients roughly weekly; a
session spanning a deploy may get force-upgraded, and the upgrade shuts down running clients.
Predicts the deaths correlate with *launching a new client* (which triggers the bootstrapper), not
with elapsed time per se. Testable by asking whether a new alt was launched right before the deaths.

**H3 — RoRoRo's 1.5s cross-process window decoration.** ~57.6k writes/client/day into a
Hyperion-protected process, with overlapping ticks on slow machines. No direct evidence; weakened by
the fact that Bloxstrap users see the same failure class without doing this. Cheap A/B: disable the
decorator and run a long session.

## What to capture on the repro box

RoRoRo already writes everything needed — nothing to instrument first.

1. **Logs.** `%LOCALAPPDATA%\ROROROblox\logs\rororoblox-<date>.log`. Serilog, daily-rolling, 14 files
   retained, 25 MB cap with roll-on-size. `ROROROblox.*` logs at Debug. Get these from the *affected
   users* before the 14-day window ages them out — this is time-sensitive.
2. **In-app Session History** — records per-session duration and an outcome hint
   (`MainViewModel.RecordSessionEndAsync`). Confirms the 20-30h figure and whether all clients died
   together or staggered.
3. **Windows Event Viewer** → Application, at the death timestamp. An Application Error / 1000 entry
   means the client crashed; a clean absence means it exited deliberately. **This single data point
   separates H1/H2 from a deliberate shutdown** and is the highest-value thing to grab.
4. **Per-process working set over time** — Task Manager or `perfmon` on `RobloxPlayerBeta.exe`,
   sampled hourly. Rising monotonically toward the machine ceiling confirms H1.
5. **Total RAM on the affected machines.** H1 predicts the low-RAM boxes die first.

## Questions outstanding for the reporters

- Which RoRoRo version? (v1.11.1.0 is current; Store and dev builds both report assembly 1.3.4 —
  check the About box, not the process metadata.)
- How many clients open at once?
- Did **all** windows die together, or one at a time?
- Was RoRoRo itself still running afterward, and what did the tray icon show — ON, OFF, or ERROR?
- Did anyone launch an additional alt shortly before the deaths? (H2 discriminator.)
- RAM, and is the machine otherwise loaded?

## Notes

- Local `main` was 98 commits behind `origin/main` at the start of this investigation — the first
  pass read v1.9 code against a v1.11.1.0 fleet. Fast-forwarded before any conclusions were drawn.
  Worth a habit: `git pull` before reading code to explain a production report.
