# Notes for certification — reviewer letter (v1.8.0.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission options** → **Notes for certification**.
>
> v1.8 is a feature release with exactly ONE disclosure-surface delta: a new consent-gated plugin capability (`host.queries.account-activity`, idle timestamps only). This letter leads with that delta and with what the idle signal is NOT (no keystroke hook) — the two things a reviewer will want settled in the first 30 seconds. Everything else is local-only behavior with zero new Windows capabilities, endpoints, or data paths.

---

```
Hello reviewer,

Thank you for your time on v1.8.0.0. This release adds three features.
Two are entirely local behavior with no disclosure change. One extends
the plugin system (approved in v1.4.0.0) with a single new consent-gated
read-only query. This letter leads with that delta.

DISCLOSURE SURFACE — WHAT CHANGED, WHAT DIDN'T

  Changed:
  - One new plugin capability in the consent vocabulary:
    host.queries.account-activity. A plugin that declares it — and that
    the user explicitly consents to at install time — may ask RoRoRo
    "how long has each account been idle?" The answer is a list of
    timestamps. Nothing else is exposed.

  Unchanged:
  - Package capabilities: runFullTrust only. No new declarations.
  - Network surface: no new endpoints. The same documented Roblox APIs
    as prior versions, same User-Agent ROROROblox/<version>, no browser
    spoofing.
  - Data at rest: no new files or paths. No telemetry, analytics, or
    crash reporting — same as every prior version.
  - Privacy policy: URL unchanged; no new disclosures required (no new
    stored data, no new network calls).

THE IDLE SIGNAL IS A TIMESTAMP, NOT A HOOK

v1.8's idle awareness is built on GetLastInputInfo — the standard
Windows idle API (the same one screen savers use). It returns only the
tick of the most recent input, system-wide. RoRoRo combines it with
GetForegroundWindow to stamp "this account's window was last active at
<time>." There is NO low-level keyboard hook, NO SetWindowsHookEx, NO
raw-input registration, NO keystroke content anywhere in the app. We
rejected the hook approach explicitly during design because it would
receive real keystrokes system-wide — a shape we consider incompatible
with user trust and with Store policy. Every new Win32 call in v1.8 is
a read: GetForegroundWindow, GetWindowThreadProcessId, GetLastInputInfo,
OpenMutex (read-only probe of a named mutex).

WHAT'S NEW IN v1.8

  1. Idle awareness (local UI + the plugin query above). RoRoRo shows
     how long each running Roblox window has been idle (a per-row chip,
     a summary line, an optional tray notification with a user-set
     threshold and mute switch). Purpose: Roblox disconnects idle
     clients after ~20 minutes; users who run several accounts want to
     see which ones are about to time out.

  2. "Limited by Roblox" session state (local UI). When Roblox
     soft-locks an account (its own anti-abuse verification), the
     account's authenticated requests return HTTP 403. RoRoRo now
     recognizes that, labels the account "Limited by Roblox" instead of
     showing stale status, and excludes it from launches until Roblox
     lifts the flag (detected by the same status polling that already
     existed). This REDUCES automated traffic against Roblox — a
     flagged account is left alone rather than retried.

  3. Multi-instance lock handling for current Roblox clients (local
     process/mutex behavior). Recent Roblox clients stay resident in
     the system tray and hold their single-instance lock. RoRoRo now
     checks that lock directly at startup (and while running), explains
     the state to the user, and offers to close Roblox or retry —
     instead of the previous behavior of asking the user to restart
     RoRoRo. This is entirely local: a named-mutex probe (OpenMutex,
     SYNCHRONIZE access, read-only) plus the same process enumeration
     RoRoRo has always used. Any action that would close a Roblox
     client with an open game window asks the user for confirmation
     first.

POLICY 10.2.2 — PLUGIN SYSTEM STATUS (UNCHANGED ARCHITECTURE)

The plugin architecture approved in v1.4.0.0 is unchanged: plugins are
separate, user-installed Windows processes; RoRoRo's MSIX contains no
plugin code; installs are user-initiated from a pasted URL and
SHA-256-verified; every capability is consent-gated per plugin, and the
gRPC interceptor returns PERMISSION_DENIED for anything not granted.
v1.8 adds one read-only query to that vocabulary (above). The first
plugin using it (an anti-idle helper) is a separate product distributed
from its own GitHub repository, never bundled in this MSIX — inspect
the package: there is no plugin EXE inside.

WHAT v1.8 DOES NOT SHIP

  - No keystroke hook, no input recording, no macro or automation
    capability in RoRoRo itself. (Input synthesis remains exclusively
    a plugin-side, consent-gated capability, per the v1.4 architecture.)
  - No new package capabilities, no new network endpoints, no new
    stored data, no telemetry.
  - No change to the in-app updater: it remains check-only. Updates
    install via the Store (this channel) or by the user running the
    new installer.

WHAT REMAINS UNCHANGED FROM v1.7

  - Identity Name (626LabsLLC.RoRoRoBlox).
  - Source repository URL and MIT license.
  - Privacy policy URL and contents.
  - Roblox-side compatibility surface — same documented endpoints, same
    user-agent ROROROblox/<version>, no browser spoofing.
  - DPAPI-encrypted account vault.

WHAT THE APP IS

RoRoRo is a Windows launcher that lets a user save multiple Roblox
account credentials and launch any of them — including several at once,
in separate client windows — without having to log out and back in
between accounts. v1.8 makes it better at telling the user the truth
about those accounts: which are idle, which Roblox has flagged, and
what is holding the multi-instance lock.

WHAT IT IS NOT

RoRoRo does not modify the Roblox client. It does not inject into, hook
into, attach a debugger to, or read memory from the Roblox process. It
does not record input. It does not bundle game content, scripts,
plugins, exploits, or automation. It includes no chat, no multiplayer
networking, no user-generated content, no in-app purchases, and no
advertising.

HOW IT WORKS (TECHNICAL — v1.8 additions)

  - Idle: GetLastInputInfo + GetForegroundWindow +
    GetWindowThreadProcessId on a ~1s timer — read-only, timestamp-only.
  - Limited detection: interpretation of HTTP 403 responses from the
    same authenticated Roblox endpoints RoRoRo already calls; no new
    requests are introduced, and flagged accounts poll less, not more.
  - Lock handling: CreateMutex/OpenMutex (SYNCHRONIZE) on Roblox's
    single-instance named mutex; process enumeration via
    System.Diagnostics.Process — the same surface as prior versions.

TRADEMARK NOTICE

"Roblox" and the Roblox logo are trademarks of Roblox Corporation.
RoRoRo is an independent third-party tool, not affiliated with, endorsed
by, or sponsored by Roblox Corporation. Plugins distributed for use with
RoRoRo are also independent third-party tools, not affiliated with
RoRoRo or with Roblox Corporation.

PRIVACY POLICY

Live at:
https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/

Source code is open under the MIT License at:
https://github.com/estevanhernandez-stack-ed/ROROROblox

WHERE TO LOOK IF YOU HAVE CONCERNS

  - Grep the source for SetWindowsHookEx or RegisterRawInputDevices:
    zero hits. The idle signal is GetLastInputInfo only
    (src/ROROROblox.Core/Diagnostics/ActivityMonitor.cs).
  - Inspect the MSIX: runFullTrust only, no plugin EXE inside.
  - The consent gate for the new query is integration-tested end to
    end over the real named pipe: an unconsented plugin call returns
    PERMISSION_DENIED (src/ROROROblox.PluginTestHarness/).
  - The design specs for all three features are in the repository
    under docs/superpowers/specs/ — written before the code, including
    the explicit rejection of keyboard hooks.

If anything in this submission is unclear, please reach out and we will
respond same-day.

Thank you again for your time and consideration.

Estevan Hernandez
626 Labs LLC
```

---

## Defenses by clause (cheat sheet for v1.8 resubmission edits)

| Clause | Defense paragraph in this letter | If rejected, what to add |
|---|---|---|
| **10.2.2** dynamic-code inclusion | "POLICY 10.2.2 — PLUGIN SYSTEM STATUS" | Same as v1.4: offer a video of MSIX inspection + empty-state Plugins window. The v1.8 delta is one read-only query, not new code inclusion. |
| **10.10** security / surveillance concern | "THE IDLE SIGNAL IS A TIMESTAMP, NOT A HOOK" + "WHERE TO LOOK" | Cite the grep (no SetWindowsHookEx / RegisterRawInputDevices), the ActivityMonitor source, and the design spec's explicit hook rejection. |
| **10.1.4.4.b** unique lasting value | (carried forward) | Idle awareness + Limited detection are launcher-native value no competing Roblox launcher ships. |
| **10.2.10** prohibited uses | "WHAT IT IS NOT" + Limited gating | v1.8 actively REDUCES automated traffic: flagged accounts are excluded from launches instead of retried. |

## Pre-submission sanity check (v1.8-specific)

Before clicking **Submit for review** for v1.8.0.0, eyeball:

- [ ] Version in `Package.appxmanifest` is `1.8.0.0` (finalize-store-build.ps1 patches it with the csproj atomically)
- [ ] `dist/RORORO-Store.msix` built via `scripts/finalize-store-build.ps1` (unsigned — Partner Center signs)
- [ ] Grep the shipped source tag for `SetWindowsHookEx` / `RegisterRawInputDevices` → zero hits
- [ ] Consent sheet for a plugin declaring `host.queries.account-activity` shows the honest copy ("timestamps only, never what you type or do")
- [ ] Idle chip + Preferences threshold/mute visible on a fresh install; tray toast fires once per crossing
- [ ] BLOCKED modal (Roblox tray-resident) shows Close-for-me / Retry / Quit — no restart-RoRoRo advice anywhere
- [ ] Reviewer letter (this file's ```-delimited block) pasted verbatim into Notes for certification
- [ ] Release notes body pasted from `release-notes-1.8.0.0.md`

## Source

This file is the v1.8.0.0 reviewer letter. Predecessor letters live at:

- v1.7.1.0: [`reviewer-letter-1.7.1.0.md`](reviewer-letter-1.7.1.0.md) (maintenance shape)
- v1.4.0.0: [`reviewer-letter-1.4.0.0.md`](reviewer-letter-1.4.0.0.md) (the plugin-system letter this one's structure mirrors)
- v1.1.2.0: [`reviewer-letter.md`](reviewer-letter.md) (the rename letter)

When v1.9+ ships, copy this file to `reviewer-letter-1.9.0.0.md` and update — predecessors stay frozen for audit.
