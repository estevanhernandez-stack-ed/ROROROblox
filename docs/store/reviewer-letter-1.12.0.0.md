# Notes for certification — reviewer letter (v1.12.0.0)

> Paste the block between the `---` markers below into Partner Center → your app →
> **Submission options** → **Notes for certification**.
>
> **Read this before editing.** Every prior letter since v1.4 carried the standing
> sentence *"RoRoRo does not modify, inject into, hook, or read memory from the
> Roblox client."* This release reads a per-process memory **usage counter** for
> clients it launched. That sentence, left as-is, would now be inaccurate — and a
> reviewer diffing this submission against the approved v1.11.1.0 would be right to
> ask. The letter below **leads with that distinction** and states it in the
> reviewer's own vocabulary: we read a number Windows publishes about the process,
> never the contents of its memory. Do not soften or shorten that paragraph to save
> space; cut elsewhere if the field is tight.
>
> No new package capabilities, no new network endpoints, no new stored secrets. The
> 10.2.2 marketplace posture is unchanged (compiled out of this package).

---

```text
Hello reviewer,

Thank you for your time on v1.12.0.0. One change in this release
touches a statement in our previous letters, so I am leading with it
rather than leaving you to find it.

MEMORY USAGE MONITORING (NEW) - AND A CORRECTION TO OUR PRIOR WORDING

Our approved submissions stated that RoRoRo "does not modify, inject
into, hook, or read memory from the Roblox client." That remains true
of memory CONTENTS and always will be. This release does newly read a
memory USAGE COUNTER, and we want the distinction on the record.

What the app now does: for Roblox clients that RoRoRo itself launched,
it periodically reads the private-bytes counter Windows already
publishes about the process, via the standard .NET
System.Diagnostics.Process API (the same number Task Manager shows in
its Memory column). It samples this every 30 seconds.

What the app does NOT do: it does not call ReadProcessMemory, does not
open a process handle for memory access, does not inspect, scan, or
interpret the contents of the Roblox client's memory, and does not
attach a debugger. It reads one integer per process. The source is MIT
and greppable: there are zero occurrences of ReadProcessMemory,
WriteProcessMemory, VirtualQueryEx, SetWindowsHookEx, or
RegisterRawInputDevices anywhere in the codebase.

Why: the Roblox client leaks memory over long sessions. Users running
several clients over 20+ hours hit their machine's RAM ceiling and
Windows terminates clients unpredictably. RoRoRo now shows each
client's memory use, warns before the machine runs out, and offers a
one-click "Recycle" that closes one client and reopens it to the same
place. Closing and restarting the process is the only way Windows
reclaims that memory. All of this is local; no reading is transmitted
anywhere.

PROCESS LIFECYCLE (EXTENDED, SAME POSTURE AS v1.10/v1.11)

Two additions, both ordinary management of processes the user has
already delegated to RoRoRo:

  - "Recycle" closes one client the user selected and relaunches it to
    the same destination. User-initiated, one click, never automatic.
  - "Clear strays" closes leftover Roblox processes that have NO
    window - the remnants Roblox leaves behind after a client exits.
    It never touches a process with an open game window. Where the app
    cannot determine whether a process has a window, it treats it as
    windowed (i.e. as a live game) and leaves it alone; that
    fail-closed behavior is unit-tested.

As before, the app still asks first before closing any client with an
open game window, and never closes one without consent.

PLUGIN HOST: ONE CAPABILITY ADDITION (SAME CONSENT MODEL)

The local plugin interface (approved v1.4.0.0, per-capability user
consent, local named pipe only) adds one capability: a plugin may
subscribe to memory-pressure notifications, so an automation plugin
can recycle a heavy account. It carries the same explicit
per-capability consent prompt as every existing capability, and
delivers only the same counters described above - account identifier
and memory figures, no credentials. The plugin MARKETPLACE remains
compiled out of this package via the runtime IsPackaged() gate
approved in v1.9.0.0: no catalog fetch, no marketplace UI, no network
call to any catalog in the Store build.

UNCHANGED FROM v1.11: runFullTrust as the only declared capability; no
new network endpoints (all Roblox calls remain the documented ones
previously disclosed, User-Agent ROROROblox/<version>); no telemetry;
identity name; DPAPI-encrypted local account vault; privacy policy
(https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/); the
in-app updater remains check-only; streamer mode unchanged.

"Roblox" is a trademark of Roblox Corporation; RoRoRo is an
independent third-party tool, not affiliated with or endorsed by
Roblox Corporation. Source is MIT at
https://github.com/estevanhernandez-stack-ed/ROROROblox.

If anything is unclear, please reach out and we will respond same-day.

Estevan Hernandez
626 Labs LLC
```

---

## Defenses by clause (cheat sheet for v1.12)

| Clause | Defense in this letter | If rejected, what to add |
|---|---|---|
| **10.10** security / surveillance — *the one to watch this cycle* | The lead paragraph draws the counter-vs-contents line explicitly and names the exact APIs we do not call | Offer a source walkthrough: `ProcessMemoryProbe.cs` is 20 lines and its entire body is `p.PrivateMemorySize64`. Grep evidence for zero `ReadProcessMemory` / `VirtualQueryEx` / `SetWindowsHookEx` / `RegisterRawInputDevices`. Point out the same counter is visible to any user in Task Manager. |
| **Prior-statement discrepancy** (if a reviewer diffs against v1.11.1.0) | We raise it ourselves in the first paragraph rather than being caught on it | Emphasise that we volunteered the correction unprompted; the underlying commitment (no memory contents, no injection, no hooks) is unchanged and still enforced. |
| **Process termination** | "Ordinary management of processes the user has already delegated"; Recycle is user-initiated and one-click; Clear strays only touches windowless remnants and fails closed when unsure | Video: a windowed client is never closed without the confirm; the strays path with three windowless leftovers closes exactly those and leaves an open game running. |
| **10.2.2** dynamic-code inclusion | Marketplace still compiled out; the new capability is host-side RPC delivering integers, not code | Reuse the v1.9 MSIX-inspection walkthrough; the `IsPackaged()` gate is unit-tested. |
| **10.5** privacy | Readings are local, logged locally, transmitted nowhere; plugin delivery is consent-gated and carries no credentials | Privacy policy link; note the feature reduces a failure mode rather than collecting anything new. |

## Pre-submission sanity check (v1.12-specific)

- [ ] `Package.appxmanifest` Version = `1.12.0.0` (4th component zero)
- [ ] `ROROROblox.App.csproj` `<Version>` = `1.12.0.0` (must match the manifest)
- [ ] `PublisherDisplayName` = `626Labs LLC` (NO space in 626Labs — the spaced form fails Partner Center validation)
- [ ] `TargetDeviceFamily MinVersion` still `10.0.19045.0` (Windows 10 22H2)
- [ ] Grep source for `ReadProcessMemory` / `WriteProcessMemory` / `VirtualQueryEx` / `SetWindowsHookEx` / `RegisterRawInputDevices` → **zero hits** (this is the evidence behind the lead paragraph — actually run it, do not assume)
- [ ] Inspect the `.msix`: no plugin EXE inside; marketplace UI absent on a packaged install
- [ ] This letter's block pasted into Notes for certification
- [ ] Public "What's new in this version" filled from `listing-copy.md` v1.12.0.0 block (**no marketplace mention**)

## Source

Predecessor letters: [`reviewer-letter-1.11.1.0.md`](reviewer-letter-1.11.1.0.md) · [`reviewer-letter-1.9.0.0.md`](reviewer-letter-1.9.0.0.md) · [`reviewer-letter-1.4.0.0.md`](reviewer-letter-1.4.0.0.md) (the full plugin-system 10.2.2 defense).
