# Notes for certification — reviewer letter (v1.7.1.0)

> Paste the block between the `---` markers into Partner Center → your app → **Submission options** → **Notes for certification**.
>
> v1.7.1.0 is a pure maintenance patch on v1.7.0.0 — the easiest letter in the series. Zero disclosure-surface delta: no new endpoint, no new capability, no new at-rest data, no new feature. The letter says exactly that, names the fixes in one block (one is a privacy improvement worth surfacing), and stops.

---

```
Hello reviewer,

Thanks for your time on v1.7.1.0. This is a maintenance release: bug
fixes from an internal full-app audit. There is NO change to the
disclosure surface — no new outbound endpoint, no new MSIX capability
(still runFullTrust only), no telemetry, no new at-rest data, no new
user-facing feature.

WHAT v1.7.1.0 FIXES

  - Privacy improvement: the temporary WebView2 browser profile used by
    the "Add account" login window is now deleted the moment the login
    window closes (previously it persisted until the next add), plus a
    startup sweep removes any profile orphaned by a crash. Less data at
    rest; nothing new leaves the machine.
  - The background presence check (account status rows) now survives
    transient errors instead of silently stopping for the session.
  - A Roblox client that exits immediately after launch can no longer
    be missed and shown as still running.
  - Plugin installs now refuse non-HTTPS URLs before any download
    (hardening of the existing SHA-verified, user-initiated install
    flow — same consent model as v1.4.0.0, unchanged).
  - Internal: errors previously swallowed without a trace are now
    written to the local diagnostics log (local file only — no
    telemetry), and two stale UI strings were corrected.

UNCHANGED FROM v1.7.0.0. Only runFullTrust is declared (no
broadFileSystemAccess, no internetClient). The outbound surface is
identical to v1.7.0.0. The DPAPI account vault, passphrase export/import,
auth-ticket launch flow, presence reads, plugin consent model, and the
Authenticode signing identity are all unchanged. The Package.appxmanifest
delta is one attribute: Identity Version 1.7.0.0 -> 1.7.1.0.

Full detail is in the open-source repo (MIT), including the audit that
produced these fixes:
  docs/reviews/2026-06-12-full-app-review.md

"Roblox" and the Roblox logo are trademarks of Roblox Corporation.
RoRoRo is an independent third-party tool, not affiliated with, endorsed
by, or sponsored by Roblox Corporation.

Privacy policy: https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/
Source (MIT):   https://github.com/estevanhernandez-stack-ed/ROROROblox

Happy to clarify anything — same-day response.

Estevan Hernandez
626 Labs LLC
```

---

## Pre-submission sanity check (v1.7.1.0-specific)

- [ ] Version in `Package.appxmanifest` is `1.7.1.0`
- [ ] Version in `ROROROblox.App.csproj` is `1.7.1.0`
- [ ] Manifest delta from v1.7.0.0 is **only** the Identity Version attribute (diff before submit)
- [ ] `dist/RORORO-Store.msix` is built off the `v1.7.1.0` tag
- [ ] Reviewer letter (this file's `---` block) pasted into Partner Center → Submission options → Notes for certification
- [ ] App still declares ONLY `runFullTrust` — no `broadFileSystemAccess`, no `internetClient`
- [ ] No new outbound endpoint anywhere in the diff (`git diff v1.7.0.0..v1.7.1.0 -- src/` shows no new URL)
- [ ] WebView2 profile sweep verified: add an account, confirm `%LOCALAPPDATA%\ROROROblox\webview2-data\` is empty after the login window closes
- [ ] Plugin install of an `http://` URL is refused with the friendly error (manual smoke)
- [ ] Privacy policy live copy is current (no data-surface change this release)
- [ ] `dotnet test ROROROblox.slnx` passes (unit + integration harness; full-solution CI gate green — including the new full-tree secret-scan job)

## Source

This file is the v1.7.1.0 reviewer letter. Predecessors:

- v1.7.0.0: [`reviewer-letter-1.7.0.0.md`](reviewer-letter-1.7.0.0.md) (client-version CDN check + stop-all + 2 plugin RPCs)
- v1.6.0.0: [`reviewer-letter-1.6.0.0.md`](reviewer-letter-1.6.0.0.md) (account export/import disclosure)
- v1.5.0.0: [`reviewer-letter-1.5.0.0.md`](reviewer-letter-1.5.0.0.md) (presence disclosure + tags)
- v1.4.0.0: [`reviewer-letter-1.4.0.0.md`](reviewer-letter-1.4.0.0.md) (plugin-system policy 10.2.2 — still load-bearing)
