# Notes for certification — reviewer letter (v1.14.0.0)

> Paste the block between the `---` markers below into Partner Center → your app →
> **Submission options** → **Notes for certification**.
>
> **Read this before editing.**
>
> 1. **This submission covers TWO internal releases.** v1.13.0.0 was tagged and built but never
>    submitted; its FPS-cap work ships inside this package. A reviewer diffing against the last
>    approved submission will see both sets of changes, so the letter discloses both rather than
>    describing v1.14 alone.
> 2. **Confirm what is actually live in Partner Center before pasting.** This letter says "since
>    v1.12.0.0" on the assumption that v1.12 was the last submission. If the approved version in
>    Partner Center is older, adjust that reference — do not let the letter claim a baseline the
>    reviewer cannot see.
> 3. **The v1.12 memory-counter paragraph stays.** It is now part of the shipped baseline, not a
>    v1.12-only disclosure. Dropping it would leave the standing "no memory reads" phrasing from
>    the pre-v1.12 letters as the most recent thing on file about memory.
>
> What is genuinely new this cycle: one additional **request type** on the PlaceLauncher endpoint
> already disclosed since v1.1 (`request=RequestGameJob`), and more frequent calls to the presence
> endpoint already disclosed since v1.5. No new hosts, no new capabilities, no new stored data,
> no new persisted fields. The 10.2.2 marketplace posture is unchanged (compiled out).

---

```text
Hello reviewer,

Thank you for your time on v1.14.0.0. This package bundles two of our
internal releases (1.13 and 1.14); 1.13 was built but never submitted,
so both sets of changes are described below.

JOINING A SPECIFIC SERVER (NEW)

RoRoRo lets a user run several Roblox accounts side by side and launch
each into a game. Until now it could only ask for a GAME - Roblox then
placed the account in whichever server had room, which is often not
the one the user's friends are in.

This release can ask for a specific server. Two mechanisms, both using
interfaces we have already disclosed:

  - The launch URL adds one request type on the endpoint we have used
    since v1.1: assetgame.roblox.com/game/PlaceLauncher.ashx, with
    request=RequestGameJob and the server's job id, alongside the
    RequestGame / RequestPrivateGame / RequestFollowUser forms already
    described in prior letters. Same host, same endpoint, no new
    network destination.

  - The server id comes from Roblox's own presence API
    (presence.roblox.com), which RoRoRo has queried since v1.5 to show
    the user which of their accounts are online. Each account queries
    ITS OWN presence with its own credentials; the field is one Roblox
    already returns in that response and which the app previously
    discarded.

The user's two existing actions now use it: "Recycle" (close one
client and reopen it) returns the account to the server it was in, and
"Squad Launch" can put a set of the user's own accounts into one
public server rather than scattering them. Both are user-initiated,
one click, never automatic.

After a launch, the app re-checks that account's presence to see
whether it actually reached the requested server, and tells the user
by name if it did not. This is a read of the user's own presence, at
most once every 15 seconds for up to four minutes after a launch, and
only for the account just launched. Nothing is transmitted anywhere;
the server id lives in memory for the session and is never written to
disk.

We found during testing that a full server does not reject the
request - Roblox places the user in a visible queue and admits them as
spots open. RoRoRo therefore reports and waits; it never retries
automatically, which would discard the user's position in that queue.

FRAME-RATE SETTINGS TIMING (FROM 1.13)

RoRoRo has written Roblox's own settings file
(%LOCALAPPDATA%\Roblox\GlobalBasicSettings_<N>.xml) since v1.2 to
apply a user-chosen frame-rate cap. Roblox keeps one such file for all
clients, so launching two accounts seconds apart could apply one
account's cap to the other. The app now waits for the previously
launched client to read its own setting before writing the next.

This reads and writes a settings FILE the user's own Roblox
installation owns. It does not read from the Roblox client process and
does not change our posture on that in any way.

UNCHANGED, AND RESTATED SO THE RECORD IS COMPLETE

RoRoRo does not modify, inject into, hook, or read the MEMORY CONTENTS
of the Roblox client. As disclosed in v1.12, it does read the
private-bytes usage COUNTER Windows publishes about clients RoRoRo
itself launched (the number Task Manager shows), via the standard .NET
System.Diagnostics.Process API, to warn users before their machine
runs out of RAM. There are zero occurrences of ReadProcessMemory,
WriteProcessMemory, VirtualQueryEx, SetWindowsHookEx, or
RegisterRawInputDevices anywhere in the source, which is MIT and
greppable.

Also unchanged from v1.12: runFullTrust as the only declared
capability; no telemetry; no new network endpoints (all Roblox calls
remain the documented ones previously disclosed, User-Agent
ROROROblox/<version>); DPAPI-encrypted local account vault; privacy
policy
(https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/);
check-only in-app updater; the plugin host's local named-pipe
interface with per-capability user consent; and the plugin
MARKETPLACE compiled out of this package via the runtime IsPackaged()
gate approved in v1.9.0.0 - no catalog fetch, no marketplace UI, no
catalog network call in the Store build.

"Roblox" is a trademark of Roblox Corporation; RoRoRo is an
independent third-party tool, not affiliated with or endorsed by
Roblox Corporation. Source is MIT at
https://github.com/estevanhernandez-stack-ed/ROROROblox.

If anything is unclear, please reach out and we will respond same-day.

Estevan Hernandez
626 Labs LLC
```

---

## Defenses by clause (cheat sheet for v1.14)

| Clause | Defense in this letter | If rejected, what to add |
|---|---|---|
| **10.1.1 / "circumvention"** — *the one to watch this cycle.* Asking for a specific server could read as gaming a matchmaker | We use a documented Roblox launch endpoint, in the same shape as the private-server and follow-a-friend forms Roblox itself exposes in its UI. Joining a friend's specific server is a first-party Roblox feature; this is the same intent for the user's own accounts | Point out Roblox's own "Join" button on a friend's profile does exactly this. Note that a full server queues us like anyone else — we get no priority and no bypass, which the letter states unprompted. Offer `RobloxLauncher.BuildPlaceLauncherUrl` (one switch expression, ~40 lines) |
| **10.5 privacy** | Each account reads its OWN presence with its own credentials; the field is already in a response we already receive; held in memory, never persisted, never transmitted | Grep evidence that `ServerInstance` appears in no store or serializer. Privacy policy link |
| **Rate/abuse posture** (if asked why presence calls increased) | At most one call per 15 s per account, only for an account just launched, bounded at four minutes, and an account that lands leaves the loop immediately | Show the poll interval and cap as named constants in `ServerLandingGate` with the measurement that set them |
| **10.2.2** dynamic code | Marketplace still compiled out; nothing in this release adds code loading | Reuse the v1.9 MSIX-inspection walkthrough; the `IsPackaged()` gate is unit-tested |
| **10.10** security / surveillance | Standing memory-contents commitment restated verbatim; the counter distinction from v1.12 carried forward rather than quietly dropped | The v1.12 defense applies unchanged — see [`reviewer-letter-1.12.0.0.md`](reviewer-letter-1.12.0.0.md) |
| **Process termination** | Recycle unchanged in behavior — user-initiated, one click; it only changes WHERE the relaunch goes | The v1.12 defense applies unchanged |

## Pre-submission sanity check (v1.14-specific)

- [ ] Confirm in Partner Center which version is actually approved/live, and fix the letter's
      "since v1.12.0.0" reference if it is not v1.12
- [ ] `Package.appxmanifest` Version = `1.14.0.0` (4th component zero)
- [ ] `ROROROblox.App.csproj` `<Version>` = `1.14.0.0` (must match the manifest)
- [ ] `PublisherDisplayName` = `626Labs LLC` (NO space in 626Labs — the spaced form fails Partner Center validation)
- [ ] `TargetDeviceFamily MinVersion` still `10.0.19045.0` (Windows 10 22H2)
- [ ] Grep source for `ReadProcessMemory` / `WriteProcessMemory` / `VirtualQueryEx` / `SetWindowsHookEx` / `RegisterRawInputDevices` → **zero hits** (actually run it, do not assume)
- [ ] Grep for the new endpoint shape: `RequestGameJob` appears only in `RobloxLauncher.BuildPlaceLauncherUrl` and its tests — no second construction site
- [ ] Inspect the `.msix`: no plugin EXE inside; marketplace UI absent on a packaged install
- [ ] This letter's block pasted into Notes for certification
- [ ] Public "What's new in this version" filled from `listing-copy.md` v1.14.0.0 block (**no marketplace mention**)

## Source

Predecessor letters: [`reviewer-letter-1.12.0.0.md`](reviewer-letter-1.12.0.0.md) (memory-counter disclosure) ·
[`reviewer-letter-1.9.0.0.md`](reviewer-letter-1.9.0.0.md) (marketplace gate) ·
[`reviewer-letter-1.4.0.0.md`](reviewer-letter-1.4.0.0.md) (the full plugin-system 10.2.2 defense).
Design doc for this release: [`../superpowers/specs/2026-08-02-server-instance-targeting-design.md`](../superpowers/specs/2026-08-02-server-instance-targeting-design.md).
