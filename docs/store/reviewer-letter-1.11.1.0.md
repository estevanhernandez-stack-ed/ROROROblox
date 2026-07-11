# Notes for certification — reviewer letter (v1.11.1.0)

> Paste the block between the `---` markers below into Partner Center → your app →
> **Submission options** → **Notes for certification**.
>
> Short form (the 1.7.0.0 fit-the-field shape). This submission spans TWO release
> trains since the approved v1.9.0.0 (v1.10.0.0 shipped direct-download only). The
> headline is streamer mode — a privacy-positive, entirely-local feature — plus two
> plugin-host capability additions under the same consent model, and a startup
> behavior change around the multi-instance lock that deserves plain-language
> disclosure. No new package capabilities, no new network endpoints, no new stored
> secrets. The 10.2.2 marketplace posture is unchanged (compiled out of this package).

---

```text
Hello reviewer,

Thank you for your time on v1.11.1.0. This update spans two release
trains since the approved v1.9.0.0; the notable changes are below.
Everything else is unchanged from that approved submission.

STREAMER MODE (NEW, ENTIRELY LOCAL)

A privacy feature for users who stream or screen-share: one toggle
replaces the account names and avatars shown in RoRoRo's own window
with bundled fictional stand-ins (e.g. "CaptainNoodle" and a cartoon
avatar shipped inside the package), so a screen-share does not reveal
the user's account list. It also applies the stand-in name to the
window titles RoRoRo already sets on Roblox clients it launched. The
mode is off by default, changes nothing inside the Roblox client
itself, makes no network calls, and stores its stand-in assignments in
the same DPAPI-encrypted local vault the app already uses (plus a
small local JSON for friend-list stand-ins — display labels only, no
credentials). The 12 avatar images are static art bundled in the
package.

MULTI-INSTANCE STARTUP BEHAVIOR (CHANGED)

Roblox can start with Windows as a windowless background process that
holds the single-instance handle RoRoRo needs. Previous versions
showed a popup asking the user to intervene. v1.10+ resolves the
common case automatically: if the only Roblox running is that
windowless background process (no game window open), RoRoRo closes
that background process, takes the handle, and starts a fresh
background client. If any Roblox game window is open, RoRoRo still
asks first and never closes a client the user is playing in. This is
ordinary process management of processes the user already delegates to
RoRoRo (it has always launched and, on request, closed Roblox
clients). RoRoRo still does not modify, inject into, hook, or read
memory from the Roblox client, and does not record input.

PLUGIN HOST: TWO CAPABILITY ADDITIONS (SAME CONSENT MODEL)

The local plugin interface (approved v1.4.0.0, per-capability user
consent, local named pipe only) adds two capabilities: a plugin may
report that an account is active (suppresses a false idle warning) and
may request that accounts be stopped (outage recovery). Both prompt
for the same explicit per-capability consent as every existing
capability. The plugin MARKETPLACE remains compiled out of this
package via the runtime IsPackaged() gate approved in v1.9.0.0: no
catalog fetch, no marketplace UI, no network call to any catalog in
the Store build.

ALSO SINCE v1.9 (LOCAL BEHAVIOR, NO DISCLOSURE CHANGE)

  - Squad launch got a staged mode that waits for each account to load
    before launching the next, with per-account follow settings.
  - Users can mark a default private server and default game; launching
    with none set opens the Roblox home page via the same documented
    launch flow.
  - New bundled artwork for the streamer-mode avatars; assorted fixes
    (launch retry timing, taskbar icon on the direct-download portable
    flavor — not applicable to the packaged build).

UNCHANGED FROM v1.9: runFullTrust as the only declared capability; no
new network endpoints (all Roblox calls remain the documented ones
previously disclosed, User-Agent ROROROblox/<version>); no telemetry;
identity name; DPAPI-encrypted local account vault; privacy policy
(https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/); the
in-app updater remains check-only.

"Roblox" is a trademark of Roblox Corporation; RoRoRo is an
independent third-party tool, not affiliated with or endorsed by
Roblox Corporation. Source is MIT at
https://github.com/estevanhernandez-stack-ed/ROROROblox.

If anything is unclear, please reach out and we will respond same-day.

Estevan Hernandez
626 Labs LLC
```

---

## Defenses by clause (cheat sheet for v1.11 resubmission edits)

| Clause | Defense in this letter | If rejected, what to add |
|---|---|---|
| **10.2.2** dynamic-code inclusion | "The plugin MARKETPLACE remains compiled out" + both new capabilities are host-side RPC, not code delivery | Offer the MSIX-inspection walkthrough from the v1.9 letter; the `IsPackaged()` gate is unit-tested and the marketplace UI never renders in the packaged build. |
| **10.10** security / surveillance | Streamer mode paragraph (local display substitution, no capture) + the standing "no hook / no injection / no input recording" line | Grep the source: zero `SetWindowsHookEx` / `RegisterRawInputDevices`. Streamer mode only changes what RoRoRo's own window and its own window titles display. |
| **10.5** privacy | Stand-ins stored in the existing DPAPI vault + a display-label-only JSON; no new data collected, no telemetry | Point at the privacy policy — no data leaves the machine; streamer mode reduces on-screen exposure rather than adding collection. |
| **Process termination concern** (if raised) | "ordinary process management of processes the user already delegates to RoRoRo" + never closes a client with an open game window | Video: tray-only Roblox → RoRoRo starts silently and a tray client returns; open game window → the popup still appears and nothing closes without consent. |

## Pre-submission sanity check (v1.11-specific)

- [ ] `Package.appxmanifest` Version = `1.11.1.0` (4th component zero)
- [ ] `PublisherDisplayName` = `626Labs LLC` (NO space in 626Labs — the spaced form fails Partner Center validation)
- [ ] `TargetDeviceFamily MinVersion` still `10.0.19045.0` (Windows 10 22H2)
- [ ] Grep source for `SetWindowsHookEx` / `RegisterRawInputDevices` → zero hits
- [ ] Inspect the `.msix`: no plugin EXE inside; marketplace UI absent on a packaged install. (The 12 avatar PNGs are WPF `<Resource>` items embedded in ROROROblox.App.dll — like the tray icons — so they will NOT appear as loose files in the package; that's correct, not a missing asset.)
- [ ] This letter's block pasted into Notes for certification
- [ ] Public "What's new in this version" filled from `listing-copy.md` v1.11.1.0 block (streamer mode leads; **no marketplace mention**)

## Source

Predecessor letters: [`reviewer-letter-1.9.0.0.md`](reviewer-letter-1.9.0.0.md) · [`reviewer-letter-1.8.0.0.md`](reviewer-letter-1.8.0.0.md) · [`reviewer-letter-1.4.0.0.md`](reviewer-letter-1.4.0.0.md) (the full plugin-system 10.2.2 defense).
