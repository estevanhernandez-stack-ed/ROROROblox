# Notes for certification — reviewer letter (v1.4.2.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission options** → **Notes for certification**.
>
> v1.4.2.0 is a single-bugfix MINOR bump on top of v1.4.1.0. The plugin-system policy 10.2.2 alignment letter from v1.4.0.0 still applies in full; this letter intentionally points back to that submission rather than re-litigating the architecture. Reviewers picking up v1.4.2.0 cold can read [`reviewer-letter-1.4.0.0.md`](reviewer-letter-1.4.0.0.md) for the full plugin-system policy framing.

---

```
Hello reviewer,

Thank you for your time on v1.4.2.0. This release is a single bugfix
on top of v1.4.1.0. No new features, no new permissions, no new
network calls, no new dependencies. The plugin-system policy 10.2.2
alignment documented in the v1.4.0.0 cert notes still applies in full
and is not re-litigated here.

WHAT CHANGED IN v1.4.2.0

One bug, one new file, two small edits to the launcher view-model:

  - src/ROROROblox.App/Diagnostics/AppStorageDefender.cs (new) — a
    self-contained helper that writes the dispatched account's
    identity (Username / DisplayName / UserId) into Roblox's own
    appStorage.json file at:

      %LOCALAPPDATA%\Roblox\LocalStorage\appStorage.json

    It then watches that file via FileSystemWatcher for 12 seconds and
    re-stamps the identity if a sibling RobloxPlayerBeta.exe process
    overwrites it.

  - src/ROROROblox.App/ViewModels/MainViewModel.cs — the launch
    dispatch path constructs one AppStorageDefender per launch when
    the account's Roblox user ID is known, and disposes it
    automatically when its 12-second window closes. The inter-launch
    delay in LaunchAll / SquadLaunch was widened from 1500ms to
    5000ms to reduce the contested-write window between back-to-back
    launches.

The new file only touches a Roblox-owned file inside the current
user's LocalAppData folder. No registry writes. No system file
touches. No process inspection beyond what was already in v1.4.0.0.
The capability the app already declares (runFullTrust) covers it; no
new capabilities are requested.

WHY IT MATTERED

Roblox brands its captcha verification gate from the Username and
DisplayName fields in appStorage.json. Each RobloxPlayerBeta.exe
process writes its own identity to that file approximately 3 to 5
seconds after attach. When the user multi-launches several alts in
quick succession (a common path for our audience), a captcha that
fires for launch N can read identity from launch N+1's RPB write —
because the next launch's RPB has already overwritten the field by
the time the captcha gate reads it. The user then sees the wrong
account name on the gate and, on Submit, Roblox signs them into that
wrong account.

The defender ensures the captcha-rendering RPB sees the identity the
user intended to launch. The fix only affects the user's own machine
and the user's own multi-launch flow; there is no change to network
behavior, no new server-side calls, and no change to the Roblox-side
contract surface.

WHAT STAYED THE SAME FROM v1.4.1.0

  - Identity Name (626LabsLLC.RoRoRoBlox), Publisher CN, Privacy
    policy URL, Source repository URL, Roblox-side compatibility
    surface, DPAPI-encrypted account vault, Authenticode signing.
  - The plugin contract (ROROROblox.PluginContract v0.1.0). Plugins
    built against v1.4.0.0's contract keep working unchanged — this
    fix is entirely on the launcher dispatch path and does not touch
    the plugin host, the named-pipe gRPC server, the plugin manifest
    format, or any plugin capability surface.
  - The plugin policy 10.2.2 alignment story documented in the
    v1.4.0.0 cert notes: MSIX still contains no plugin code, plugin
    install is still user-initiated from a GitHub URL, downloads are
    still SHA-256-verified, capabilities are still consent-gated, the
    named pipe is still per-user ACL'd, and plugins remain optional
    (Plugins window still shows empty state on a fresh install).
  - The manifest delta from v1.4.1.0 is one attribute: Identity Version
    1.4.1.0 → 1.4.2.0. Nothing else in Package.appxmanifest changed.
  - No telemetry has been added. RoRoRo continues to make no
    analytics, crash-reporting, or usage-tracking calls.

FILES TOUCHED

Four files in total:

  - src/ROROROblox.App/Diagnostics/AppStorageDefender.cs (NEW, 226
    lines, self-contained, no new package references).
  - src/ROROROblox.App/ViewModels/MainViewModel.cs (~45 lines added:
    defender construction at dispatch, two delay increases, one
    diagnostic SHA-256 helper for safer cookie logging that never
    emits the cookie value itself).
  - src/ROROROblox.App/ROROROblox.App.csproj (Version 1.4.1.0 →
    1.4.2.0).
  - src/ROROROblox.App/Package.appxmanifest (Identity Version
    1.4.1.0 → 1.4.2.0).

TRADEMARK NOTICE

"Roblox" and the Roblox logo are trademarks of Roblox Corporation.
RoRoRo is an independent third-party tool, not affiliated with,
endorsed by, or sponsored by Roblox Corporation.

PRIVACY POLICY

Live at:
https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/

Source code is open under the MIT License at:
https://github.com/estevanhernandez-stack-ed/ROROROblox

The v1.4.0.0 reviewer letter (plugin-system policy 10.2.2 framing) is
at:
https://github.com/estevanhernandez-stack-ed/ROROROblox/blob/main/docs/store/reviewer-letter-1.4.0.0.md

The v1.4.1.0 reviewer letter (most recent predecessor) is at:
https://github.com/estevanhernandez-stack-ed/ROROROblox/blob/main/docs/store/reviewer-letter-1.4.1.0.md

If anything in this submission is unclear, please reach out and we
will respond same-day.

Thank you again for your time and consideration.

Estevan Hernandez
626 Labs LLC
```

---

## Pre-submission sanity check (v1.4.2.0-specific)

- [ ] Version in `Package.appxmanifest` is `1.4.2.0`
- [ ] Version in `ROROROblox.App.csproj` is `1.4.2.0`
- [ ] Manifest delta from v1.4.1.0 is **only** the Identity Version attribute (diff before submit)
- [ ] `dist/RORORO-Store.msix` is built off the `v1.4.2.0` tag
- [ ] Reviewer letter (this file's `---`-delimited block) is pasted verbatim into Partner Center → Submission options → Notes for certification
- [ ] Authenticode signature on the MSIX validates with `signtool verify`
- [ ] `AppStorageDefender` writes confined to `%LOCALAPPDATA%\Roblox\LocalStorage\appStorage.json` — no other filesystem paths touched by the new code
- [ ] No new package references in `ROROROblox.App.csproj` between v1.4.1.0 and v1.4.2.0

## Source

This file is the v1.4.2.0 reviewer letter. Predecessor letters live at:

- v1.4.1.0: [`reviewer-letter-1.4.1.0.md`](reviewer-letter-1.4.1.0.md) (AccountSummary RobloxUserId hotfix)
- v1.4.0.0: [`reviewer-letter-1.4.0.0.md`](reviewer-letter-1.4.0.0.md) (plugin-system policy 10.2.2 framing — still load-bearing)
- v1.1.2.0: [`reviewer-letter.md`](reviewer-letter.md) (the rename letter)

When v1.5+ ships, copy this file to `reviewer-letter-1.5.0.0.md` and update — predecessors stay frozen for audit.
