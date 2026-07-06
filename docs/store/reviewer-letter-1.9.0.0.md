# Notes for certification — reviewer letter (v1.9.0.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission options** → **Notes for certification**.
>
> Short form (the 1.7.0.0 fit-the-field shape). Two disclosure-surface facts lead: the minimum OS dropped to Windows 10 22H2, and the friends picker reads one already-disclosed Roblox endpoint from a second account. No new capabilities, endpoints, or stored data. The plugin marketplace added in the direct-download build is COMPILED OUT of the packaged/Store build by a runtime `IsPackaged()` gate — call that out so a reviewer who inspects the binary isn't surprised to find marketplace code paths that never execute in the Store package.

---

```
Hello reviewer,

Thank you for your time on v1.9.0.0. Two things worth surfacing up front;
everything else is unchanged from the approved v1.8.0.0 submission.

MINIMUM OS LOWERED TO WINDOWS 10 22H2

This submission drops TargetDeviceFamily MinVersion from 10.0.22000.0
(Windows 11) to 10.0.19045.0 (Windows 10 22H2). The app is a self-
contained .NET 10 desktop application and runs on Windows 10 22H2
unchanged; users have been running it on Windows 10 via direct download
for months. No API in the app requires Windows 11 (every Win32 call is
long-standing: named mutex, foreground-window read, GetLastInputInfo,
OpenMutex). No new package capabilities accompany this change.

FRIENDS PICKER READS A SECOND ACCOUNT'S FRIENDS LIST

v1.9 lets a user browse their "main" saved account's friends list (not
just each account's own) to launch an alt into a friend's game. This
calls the same documented, already-disclosed Roblox endpoint the app
already used (friends.roblox.com/v1/users/{id}/friends), now for a
second saved account the user owns. No new endpoint, no new host, no
new stored data. Same User-Agent ROROROblox/<version>, no browser
spoofing.

PLUGIN MARKETPLACE IS COMPILED-OUT OF THIS PACKAGE

The direct-download build gained a plugin marketplace (browse/install a
curated plugin catalog). It is gated behind a runtime IsPackaged() check
and is ENTIRELY ABSENT from the Store/packaged build: no Available
section, no catalog fetch, no network call to any catalog. If you
inspect the binary you may find the code paths; they are unreachable in
the packaged build (the gate returns false). This preserves the policy
10.2.2 posture approved in v1.4.0.0 — the Store binary does not fetch a
curated list of external code. Plugins remain separate, user-installed,
SHA-verified, per-capability-consented products, unchanged from v1.4.

ALSO IN v1.9 (LOCAL BEHAVIOR, NO DISCLOSURE CHANGE)

  - A session-flag fix: an account that Roblox soft-locks or that needs
    re-login now clears its flag reliably after re-authentication, and
    every re-login outcome (including a mid-login two-factor prompt) is
    surfaced to the user. Reduces stale state; adds no new traffic.
  - Multi-instance lock-recovery and logging refinements.

UNCHANGED FROM v1.8: runFullTrust as the only declared capability; no
new network endpoints; no telemetry; identity name; DPAPI-encrypted
local account vault; privacy policy
(https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/); the
in-app updater remains check-only.

RoRoRo does not modify, inject into, hook, or read memory from the
Roblox client, and does not record input. "Roblox" is a trademark of
Roblox Corporation; RoRoRo is an independent third-party tool, not
affiliated with or endorsed by Roblox Corporation. Source is MIT at
https://github.com/estevanhernandez-stack-ed/ROROROblox.

If anything is unclear, please reach out and we will respond same-day.

Estevan Hernandez
626 Labs LLC
```

---

## Defenses by clause (cheat sheet for v1.9 resubmission edits)

| Clause | Defense in this letter | If rejected, what to add |
|---|---|---|
| **10.2.2** dynamic-code inclusion | "PLUGIN MARKETPLACE IS COMPILED-OUT OF THIS PACKAGE" | Offer a video of MSIX inspection + the empty-state Plugins window on a clean Store install; the `IsPackaged()` gate is unit-tested (`IDistributionMode`), the marketplace UI never renders. |
| **10.10** security / surveillance | (carried from v1.8 — no keystroke hook) | Grep the source: zero `SetWindowsHookEx` / `RegisterRawInputDevices`. The friends read is a documented Roblox API call, cookie-authenticated as the account's owner. |
| **min-OS / compatibility** | "MINIMUM OS LOWERED TO WINDOWS 10 22H2" | The app is self-contained (.NET 10 bundled); it does not depend on any Windows 11 runtime component. Months of real-world Windows 10 direct-download use. |

## Pre-submission sanity check (v1.9-specific)

- [ ] `Package.appxmanifest` `TargetDeviceFamily MinVersion` = `10.0.19045.0` (Windows 10 22H2)
- [ ] `PublisherDisplayName` = `626Labs LLC` (NO space — the spaced form fails Partner Center validation)
- [ ] Version in the packaged manifest is `1.9.0.0` (4th component zero)
- [ ] Grep source for `SetWindowsHookEx` / `RegisterRawInputDevices` → zero hits
- [ ] Inspect the `.msix`: no plugin EXE inside; marketplace UI absent on a packaged install
- [ ] This letter's block pasted into Notes for certification
- [ ] Public "What's new in this version" filled from `listing-copy.md` v1.9.0.0 block (Windows 10 leads; **no marketplace mention**)

## Source

Predecessor letters: [`reviewer-letter-1.8.0.0.md`](reviewer-letter-1.8.0.0.md) · [`reviewer-letter-1.7.0.0.md`](reviewer-letter-1.7.0.0.md) (the short-to-fit-the-field lesson) · [`reviewer-letter-1.4.0.0.md`](reviewer-letter-1.4.0.0.md) (the full plugin-system 10.2.2 defense referenced here).
