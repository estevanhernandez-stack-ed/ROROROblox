# Notes for certification — reviewer letter (v1.5.0.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission options** → **Notes for certification**.
>
> v1.5.0.0 is a MINOR bump on top of v1.4.3.0 focused on account-status accuracy and a small per-account labelling feature. The plugin-system policy 10.2.2 alignment letter from v1.4.0.0 still applies in full and is not re-litigated here.
>
> The one item that affects the disclosure surface is an authenticated background call to a documented Roblox presence endpoint, for the user's OWN saved accounts only. It is called out first and in detail below. No new Windows permission, no new MSIX capability, no new dependency, no telemetry, no third-party data flow.

---

```
Hello reviewer,

Thank you for your time on v1.5.0.0. This release improves the accuracy
of the account list (which alt is in which game, and whether it is
really running) and adds free-text per-account tags. No new
runFullTrust-adjacent capabilities are requested. No new MSIX
capabilities. No new dependencies. No telemetry. The plugin-system
policy 10.2.2 alignment documented in the v1.4.0.0 cert notes still
applies in full and is not re-litigated here.

WHAT CHANGED IN v1.5.0.0

  1. AUTHENTICATED PRESENCE READ FOR THE USER'S OWN ACCOUNTS
     (the one disclosure-surface change).

     RoRoRo now reads each saved account's own Roblox presence to show
     the real game an alt is in ("In Adopt Me"), distinguish "at the
     Roblox home screen" from "in a game", and stop the account list
     from falsely reporting a still-running alt as closed.

     Endpoint: POST https://presence.roblox.com/v1/presence/users
     — a documented Roblox public Web API.

     - The call is authenticated as the user's OWN account, using that
       account's own session cookie, and asks ONLY about that same
       account's user id. RoRoRo never queries presence for anyone but
       the user's own saved accounts. (An account can always see its
       own presence; this is not a query about other people.)
     - It runs on a light background timer (~25 seconds) while RoRoRo
       is open, staggered with a small concurrency cap so it is polite
       to Roblox's servers.
     - It uses the same plain User-Agent the rest of the app uses
       (ROROROblox/<version>) — no browser spoofing.
     - The cookie is read from the existing DPAPI-encrypted vault only
       for the duration of the HTTPS call. It is never logged, never
       written to disk, and never sent anywhere except Roblox's own
       presence endpoint. (A repository audit confirms no cookie value
       reaches any log, exception message, or file on this path.)
     - There is NO new Windows permission and NO new MSIX capability.
       Outgoing HTTPS to Roblox does not require a manifest capability
       and none was added. The app already talks to Roblox public
       endpoints (auth-ticket, users, thumbnails, friends) under the
       same model; presence is one more documented Roblox endpoint in
       that same family.
     - No data is sent to 626 Labs or any third party. The only network
       counterpart is Roblox itself, on behalf of the user's own
       account. No telemetry, analytics, or crash reporting was added.

     Implemented in (new) src/ROROROblox.Core/Diagnostics/PresenceService.cs
     + IPresenceService.cs, consuming the pre-existing
     IRobloxApi.GetPresenceAsync wrapper.

  2. FREE-TEXT PER-ACCOUNT TAGS (local-only labelling).

     Users can attach short free-text tags to each saved account (e.g.
     "PS99", "RCU") to tell their alts apart. Tags are stored locally
     inside the SAME existing DPAPI-encrypted accounts file
     (accounts.dat). No new file, no new storage location, no network.

     Implemented in: Core/Account.cs (a trailing optional Tags field,
     back-compatible with existing accounts.dat), IAccountStore.cs +
     AccountStore.cs (a SetTagsAsync writer mirroring the existing
     per-field writers), and the account row UI in MainWindow.xaml +
     AccountSummary.cs. Tags are trimmed, de-duplicated, length-capped
     (24 chars) and count-capped (8 per account).

  3. ACCOUNT-STATUS RECONCILIATION + LAUNCH-MULTIPLE FEEDBACK
     (UI / logic only).

     The account row now reconciles the presence read above with local
     process tracking so a row reads as active when EITHER signal says
     so, and only "Closed" when both agree. "Launch multiple" now
     reports which accounts it skipped (already running) instead of
     silently launching a subset. No new API surface, no new
     capability, no new network calls beyond item 1.

WHAT STAYED THE SAME FROM v1.4.3.0

  - Identity Name (626LabsLLC.RoRoRoBlox), Publisher CN, Privacy policy
    URL, Source repository URL, Authenticode signing, the DPAPI-
    encrypted account vault, and the Roblox-side compatibility surface
    (auth-ticket flow, mutex handling).
  - The plugin contract wire surface and plugin manifest schemaVersion
    are unchanged. Plugins built against v1.4.0.0's contract keep
    working. The MSIX still contains no plugin code; plugin install is
    still user-initiated, SHA-256-verified, and consent-gated.
  - No telemetry has been added. RoRoRo continues to make no analytics,
    crash-reporting, or usage-tracking calls.
  - No new package references in ROROROblox.App.csproj between v1.4.3.0
    and v1.5.0.0.

FILES TOUCHED (relative to v1.4.3.0)

  - src/ROROROblox.Core/Diagnostics/PresenceService.cs + IPresenceService.cs
    — NEW. Background presence poll for the user's own accounts.
  - src/ROROROblox.Core/Account.cs — trailing optional Tags field.
  - src/ROROROblox.Core/IAccountStore.cs + AccountStore.cs — SetTagsAsync.
  - src/ROROROblox.App/ViewModels/AccountSummary.cs — presence-aware
    status reconciliation + Tags collection + tag commands.
  - src/ROROROblox.App/ViewModels/MainViewModel.cs — presence service
    wiring, launch-multiple eligibility + skip feedback, tag persistence.
  - src/ROROROblox.App/ViewModels/LaunchEligibility.cs — NEW, pure
    eligibility/breakdown helper.
  - src/ROROROblox.App/App.xaml.cs — DI registration for PresenceService.
  - src/ROROROblox.App/MainWindow.xaml (+ .xaml.cs) — game-aware status
    text + tag chips on each row.
  - src/ROROROblox.App/ROROROblox.App.csproj — Version 1.4.3.0 → 1.5.0.0.
  - src/ROROROblox.App/Package.appxmanifest — Identity Version
    1.4.3.0 → 1.5.0.0.
  - src/ROROROblox.Tests/ — new tests for PresenceService, status
    reconciliation, launch eligibility, and account tags.

The manifest delta from v1.4.3.0 is one attribute: Identity Version
1.4.3.0 → 1.5.0.0. Nothing else in Package.appxmanifest changed.

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

If anything in this submission is unclear, please reach out and we
will respond same-day.

Thank you again for your time and consideration.

Estevan Hernandez
626 Labs LLC
```

---

## Pre-submission sanity check (v1.5.0.0-specific)

- [ ] Version in `Package.appxmanifest` is `1.5.0.0`
- [ ] Version in `ROROROblox.App.csproj` is `1.5.0.0`
- [ ] Manifest delta from v1.4.3.0 is **only** the Identity Version attribute (diff before submit)
- [ ] `dist/RORORO-Store.msix` is built off the `v1.5.0.0` tag
- [ ] Reviewer letter (this file's `---`-delimited block) is pasted verbatim into Partner Center → Submission options → Notes for certification
- [ ] Authenticode signature on the MSIX validates with `signtool verify`
- [ ] Presence call goes ONLY to `presence.roblox.com` for the user's own account ids; no cookie value reaches any log/file (dpapi-cookie-blast-radius audit: PASS)
- [ ] No new package references in `ROROROblox.App.csproj` between v1.4.3.0 and v1.5.0.0
- [ ] `dotnet test ROROROblox.slnx` passes (420 unit + integration harness)

## Source

This file is the v1.5.0.0 reviewer letter. Predecessor letters live at:

- v1.4.3.0: [`reviewer-letter-1.4.3.0.md`](reviewer-letter-1.4.3.0.md) (plugin-lifecycle UX + manifest forward-flags)
- v1.4.2.0: [`reviewer-letter-1.4.2.0.md`](reviewer-letter-1.4.2.0.md) (AppStorageDefender captcha fix)
- v1.4.0.0: [`reviewer-letter-1.4.0.0.md`](reviewer-letter-1.4.0.0.md) (plugin-system policy 10.2.2 framing — still load-bearing)
- v1.1.2.0: [`reviewer-letter.md`](reviewer-letter.md) (the rename letter)

When v1.5.1+ ships, copy this file and update — predecessors stay frozen for audit.
