# Notes for certification — reviewer letter (v1.4.1.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission options** → **Notes for certification**.
>
> v1.4.1.0 is a single-bugfix BUILD bump on top of v1.4.0.0. The plugin-system policy 10.2.2 alignment letter from v1.4.0.0 still applies in full; this letter intentionally points back to that submission rather than re-litigating the architecture. Reviewers picking up v1.4.1.0 cold can read [`reviewer-letter-1.4.0.0.md`](reviewer-letter-1.4.0.0.md) for the full plugin-system policy framing.

---

```
Hello reviewer,

Thank you for your time on v1.4.1.0. This release is a single bugfix on
top of v1.4.0.0. No new features, no new permissions, no new network
calls, no new dependencies. The plugin-system policy 10.2.2 alignment
documented in the v1.4.0.0 cert notes still applies in full and is not
re-litigated here.

WHAT CHANGED IN v1.4.1.0

One bug, one file, one line of code:

  - src/ROROROblox.App/ViewModels/AccountSummary.cs — the constructor
    now copies the RobloxUserId field from the underlying Account
    record at construction time, instead of leaving it null until the
    in-app Friends modal explicitly resolved it.

WHY IT MATTERED

The plugin host (Plugins/ module shipped in v1.4.0.0) reads
RobloxUserId off each AccountSummary when it builds the per-account
snapshot it hands to plugins over the named-pipe gRPC server. Before
this fix, the snapshot emitted 0 for every alt on a fresh launch
because RobloxUserId hadn't been read on construction. 0 matches every
account trivially, which collapsed plugin per-alt behavior — the
first-party "RoRoRo Ur Task" plugin's per-account macro gating, for
example, couldn't tell alts apart until the user opened the Friends
modal on each one. The underlying store had the value persisted
already (via AccountUserIdBackfillService, shipped in v1.3.x); the
summary just needed to read it on construction.

WHAT STAYED THE SAME FROM v1.4.0.0

  - Identity Name (626LabsLLC.RoRoRoBlox), Publisher CN, Privacy
    policy URL, Source repository URL, Roblox-side compatibility
    surface, DPAPI-encrypted account vault, Authenticode signing.
  - The plugin contract (ROROROblox.PluginContract v0.1.0). Plugins
    built against v1.4.0.0's contract keep working unchanged — this
    fix is entirely on the host side of the pipe.
  - The plugin policy 10.2.2 alignment story documented in the
    v1.4.0.0 cert notes: MSIX still contains no plugin code, plugin
    install is still user-initiated from a GitHub URL, downloads are
    still SHA-256-verified, capabilities are still consent-gated, the
    named pipe is still per-user ACL'd, and plugins remain optional
    (Plugins window still shows empty state on a fresh install).
  - The manifest delta from v1.4.0.0 is one attribute: Identity Version
    1.4.0.0 → 1.4.1.0. Nothing else in Package.appxmanifest changed.
  - No telemetry has been added. RoRoRo continues to make no
    analytics, crash-reporting, or usage-tracking calls.

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

The one-line fix is at:
https://github.com/estevanhernandez-stack-ed/ROROROblox/commit/a39f22a

If anything in this submission is unclear, please reach out and we
will respond same-day.

Thank you again for your time and consideration.

Estevan Hernandez
626 Labs LLC
```

---

## Pre-submission sanity check (v1.4.1.0-specific)

- [ ] Version in `Package.appxmanifest` is `1.4.1.0`
- [ ] Version in `ROROROblox.App.csproj` is `1.4.1.0`
- [ ] Manifest delta from v1.4.0.0 is **only** the Identity Version attribute (diff before submit)
- [ ] `dist/RORORO-Store.msix` is built off the `v1.4.1.0` tag
- [ ] Reviewer letter (this file's `---`-delimited block) is pasted verbatim into Partner Center → Submission options → Notes for certification
- [ ] Authenticode signature on the MSIX validates with `signtool verify`

## Source

This file is the v1.4.1.0 reviewer letter. Predecessor letters live at:

- v1.4.0.0: [`reviewer-letter-1.4.0.0.md`](reviewer-letter-1.4.0.0.md) (plugin-system policy 10.2.2 framing — still load-bearing)
- v1.1.2.0: [`reviewer-letter.md`](reviewer-letter.md) (the rename letter)

When v1.5+ ships, copy this file to `reviewer-letter-1.5.0.0.md` and update — predecessors stay frozen for audit.
