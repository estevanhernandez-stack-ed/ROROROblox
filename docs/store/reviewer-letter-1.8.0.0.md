# Notes for certification — reviewer letter (v1.8.0.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission options** → **Notes for certification**.
>
> Kept deliberately short so it fits the field without truncating (the lesson recorded on the 1.7.0.0 letter): the disclosure-surface changes lead and are stated in full; everything else is compressed to a line. The v1.4.0.0 plugin-system policy 10.2.2 framing still applies and is not re-litigated. The one thing a reviewer will probe on this release — "is the idle tracking a keylogger?" — is answered in the second paragraph.

---

```
Hello reviewer,

Thank you for your time on v1.8.0.0. One disclosure-surface change:
a new consent-gated plugin query, host.queries.account-activity. A
plugin the user has installed and explicitly consented to may ask
RoRoRo "how long has each account's window been idle?" The answer is
timestamps only. The plugin architecture itself is unchanged from
v1.4.0.0 (out-of-process, user-initiated installs, per-capability
consent, PERMISSION_DENIED for anything not granted).

The idle signal behind that query is GetLastInputInfo — the standard
Windows idle API screen savers use — combined with GetForegroundWindow.
It returns a timestamp, never input content. There is no keyboard
hook anywhere in the app: grep the open source for SetWindowsHookEx
or RegisterRawInputDevices — zero hits.

Also in v1.8, both entirely local, no disclosure change:

  - Accounts soft-locked by Roblox's own anti-abuse checks (HTTP 403)
    now show "Limited by Roblox" and are excluded from launching until
    Roblox lifts the flag — this release reduces automated traffic,
    not increases it.
  - Current Roblox clients stay resident in the system tray holding
    their single-instance lock; RoRoRo now detects that directly
    (read-only OpenMutex probe) and helps the user resolve it, asking
    confirmation before closing any Roblox client with an open window.

Unchanged from v1.7: runFullTrust as the only declared capability, no
new network endpoints, no new stored data, no telemetry, identity name,
privacy policy (https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/),
DPAPI-encrypted local account vault, User-Agent ROROROblox/<version>
(no browser spoofing), and the in-app updater remains check-only.

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

## Defenses by clause (cheat sheet for v1.8 resubmission edits)

| Clause | Defense in this letter | If rejected, what to add |
|---|---|---|
| **10.10** security / surveillance concern | Paragraph 2 (timestamp, not a hook) | Cite the grep (no SetWindowsHookEx / RegisterRawInputDevices), `src/ROROROblox.Core/Diagnostics/ActivityMonitor.cs`, and the design spec's explicit hook rejection in `docs/superpowers/specs/`. |
| **10.2.2** dynamic-code inclusion | Paragraph 1 (architecture unchanged from approved v1.4) | Re-attach the full v1.4.0.0 letter's six-point defense; offer a video of MSIX inspection + empty-state Plugins window. |
| **10.2.10** prohibited uses | The Limited bullet (reduces automated traffic) | Flagged accounts are excluded from launches instead of retried — the opposite of abuse tooling. |
| **10.1.4.4.b** unique lasting value | (carried forward) | Idle awareness + Limited detection are launcher-native value no competing Roblox launcher ships. |

## Pre-submission sanity check (v1.8-specific)

- [ ] `dist/RORORO-Store.msix` built via `finalize-store-build.ps1` with the **playbook Phase 3 constants verbatim** — in particular `PublisherDisplayName` = `626Labs LLC` (NO space; the spaced form fails Partner Center validation — hit on 2026-07-02)
- [ ] Version in the packaged manifest is `1.8.0.0` (4th component zero)
- [ ] Grep source for `SetWindowsHookEx` / `RegisterRawInputDevices` → zero hits
- [ ] Consent sheet for `host.queries.account-activity` shows the honest copy ("timestamps only, never what you type or do")
- [ ] BLOCKED modal shows Close-for-me / Retry / Quit — no restart-RoRoRo advice anywhere
- [ ] This letter's block pasted into Notes for certification; the PUBLIC "What's new in this version" field filled separately from `listing-copy.md` (playbook Phase 7 step 4 — the step that keeps getting left off)

## Source

This file is the v1.8.0.0 reviewer letter. Predecessor letters live at:

- v1.7.1.0: [`reviewer-letter-1.7.1.0.md`](reviewer-letter-1.7.1.0.md) (maintenance shape)
- v1.7.0.0: [`reviewer-letter-1.7.0.0.md`](reviewer-letter-1.7.0.0.md) (the short-to-fit-the-field lesson this letter follows)
- v1.4.0.0: [`reviewer-letter-1.4.0.0.md`](reviewer-letter-1.4.0.0.md) (the full plugin-system defense, referenced not repeated)
- v1.1.2.0: [`reviewer-letter.md`](reviewer-letter.md) (the rename letter)

When v1.9+ ships, copy this file to `reviewer-letter-1.9.0.0.md` and update — predecessors stay frozen for audit.
