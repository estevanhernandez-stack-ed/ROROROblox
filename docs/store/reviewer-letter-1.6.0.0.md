# Notes for certification — reviewer letter (v1.6.0.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission options** → **Notes for certification**.
>
> v1.6.0.0 is a feature bump on v1.5.0.0. The one item that materially changes the disclosure surface is **account export/import** — for the first time, a user can move their saved Roblox session cookies off the machine, but ONLY via a deliberate, user-initiated, passphrase-encrypted local file (no cloud, no third party, no auto-upload). It is called out first and in full. The plugin-system policy 10.2.2 alignment from v1.4.0.0 still applies and is not re-litigated.

---

```
Hello reviewer,

Thank you for your time on v1.6.0.0. This release adds the ability to
move saved accounts to another PC, surfaces saved private servers in the
per-account launch dropdown, refines the tag UI, and fixes two launch
bugs. One item changes the data-handling disclosure surface (account
export); it is detailed first. No new MSIX capabilities. No new outbound
network endpoints. No new dependencies. No telemetry.

WHAT CHANGED IN v1.6.0.0

  1. ACCOUNT EXPORT / IMPORT (the disclosure-surface change).

     Users can now move their saved accounts to another PC. Previously
     the saved session cookies were DPAPI-encrypted to the local Windows
     user and could not leave the machine. v1.6.0.0 adds a deliberate,
     user-initiated export:

     - The user opens Settings -> Accounts -> Export accounts, picks
       accounts, and chooses a passphrase (enforced minimum length +
       strength meter). RoRoRo writes a single ".rororo-accounts" file
       to a location the USER selects via the standard Windows save-file
       dialog.
     - Inside that file, the accounts (cookies included) are encrypted
       with AES-256-GCM under a key derived from the user's passphrase
       via PBKDF2-HMAC-SHA256 at 600,000 iterations (random per-file
       salt + nonce). The file is useless without the passphrase.
     - Import on the other PC reverses it: the user picks the file,
       enters the passphrase, and the accounts are decrypted and
       re-encrypted into THAT machine's DPAPI vault (merge by Roblox
       user id; existing accounts kept).

     Privacy posture, stated plainly:
     - The export is ALWAYS user-initiated. RoRoRo never auto-exports.
     - The file goes ONLY where the user saves it. RoRoRo never uploads
       it, never transmits it, and never stores the passphrase. There is
       no cloud component and no 626 Labs server involved.
     - No new outbound network endpoint. Export/import is pure local
       file I/O.
     - NO new MSIX capability. File save/open use the standard Windows
       file-picker dialogs (the user grants access by choosing the file);
       broadFileSystemAccess is NOT declared and NOT needed. The app
       still declares only runFullTrust.
     - The privacy policy is updated to reflect this: cookies leave the
       machine only via this deliberate, passphrase-encrypted, user-saved
       export. https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/

     Implemented in src/ROROROblox.Core/Transport/AccountTransportService.cs
     (pure crypto, no network), AccountStore export/import, and the two
     dialogs under src/ROROROblox.App/Transport/. A repository security
     audit confirms the passphrase, derived key, and cookie are never
     logged, never written outside the encrypted bundle/DPAPI, and the
     key material is zeroed after use.

  2. SAVED PRIVATE SERVERS IN THE PER-ACCOUNT DROPDOWN (UI only).

     Saved private servers (already stored locally) now appear in each
     account's existing game dropdown so a user can launch an alt
     straight into a saved private server. No new storage, no new
     network calls, no new capability — a UI/launch-routing change over
     the existing private-server data and the existing launch path.

  3. TAG UI REFINEMENT (UI only).

     The per-account free-text tags from v1.5.0 get a tidier add control
     (a "+" affordance) and a list filter. Tags remain stored locally in
     the same DPAPI-encrypted accounts file. No new surface.

  4. FOLLOW GUARD (UI/logic only).

     The friend-follow path now checks presence before launching and
     declines with a clear message when the friend is not in a joinable
     game, instead of landing the user on the Roblox home page. No new
     API surface or capability.

  5. LAUNCH IDENTITY FIX (no new surface).

     Fixes a case where a Roblox client update appearing mid-launch could
     cause a different saved account to launch than the one selected. The
     local identity-stamp now holds until the launched client consumes
     it. No new network, no new capability — a timing fix in existing
     local launch code.

WHAT STAYED THE SAME FROM v1.5.0.0

  - Identity Name (626LabsLLC.RoRoRoBlox), Publisher CN, Authenticode
    signing, the DPAPI-encrypted account vault, the Roblox-side
    compatibility surface (auth-ticket flow, presence reads, mutex
    handling), and the plugin contract.
  - The app declares ONLY runFullTrust. No broadFileSystemAccess, no new
    capability of any kind.
  - No telemetry, analytics, or crash reporting. No new package
    references in ROROROblox.App.csproj between v1.5.0.0 and v1.6.0.0.

FILES TOUCHED (relative to v1.5.0.0)

  - src/ROROROblox.Core/Transport/ — NEW. AccountTransportService
    (PBKDF2 + AES-GCM), AccountExportRecord, IAccountTransport,
    AccountTransportException.
  - src/ROROROblox.Core/AccountStore.cs + IAccountStore.cs — bulk export
    read + merge-by-userId import.
  - src/ROROROblox.App/Transport/ — NEW. Export/Import dialogs +
    PassphraseStrength helper.
  - src/ROROROblox.App/Preferences/PreferencesWindow.* — Export/Import
    entry points (Settings -> Accounts).
  - src/ROROROblox.Core/FavoriteGame.cs + MainViewModel — saved private
    servers in the dropdown; tag "+" + filter; Follow guard.
  - src/ROROROblox.App/Diagnostics/AppStorageDefender.cs + MainViewModel
    — launch identity-stamp install-resilience.
  - src/ROROROblox.App/Friends/FriendFollowWindow.xaml.cs — no longer
    retains the cookie as a field (security-audit fix).
  - src/ROROROblox.App/ROROROblox.App.csproj + Package.appxmanifest —
    Version 1.5.0.0 -> 1.6.0.0.
  - docs/PRIVACY.md — account-export disclosure.
  - src/ROROROblox.Tests/ — new tests for transport crypto, store
    export/merge, passphrase strength, launch-target mapping, Follow
    guard, and the defender.

The manifest delta from v1.5.0.0 is one attribute: Identity Version
1.5.0.0 -> 1.6.0.0. Nothing else in Package.appxmanifest changed.

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

If anything in this submission is unclear, please reach out and we will
respond same-day.

Thank you again for your time and consideration.

Estevan Hernandez
626 Labs LLC
```

---

## Pre-submission sanity check (v1.6.0.0-specific)

- [ ] Version in `Package.appxmanifest` is `1.6.0.0`
- [ ] Version in `ROROROblox.App.csproj` is `1.6.0.0`
- [ ] Manifest delta from v1.5.0.0 is **only** the Identity Version attribute (diff before submit)
- [ ] `dist/RORORO-Store.msix` is built off the `v1.6.0.0` tag
- [ ] Reviewer letter (this file's `---` block) pasted into Partner Center → Submission options → Notes for certification
- [ ] App still declares ONLY `runFullTrust` — confirm no `broadFileSystemAccess` crept in for the file dialogs (it must not; standard pickers don't need it)
- [ ] Privacy policy live copy reflects the account-export disclosure
- [ ] App-wide cookie audit clean (FriendFollowWindow cookie-lifetime fix in); `*.rororo-accounts` gitignored
- [ ] `dotnet test ROROROblox.slnx` passes (519 unit + integration harness)

## Source

This file is the v1.6.0.0 reviewer letter. Predecessors:

- v1.5.0.0: [`reviewer-letter-1.5.0.0.md`](reviewer-letter-1.5.0.0.md) (presence disclosure + tags)
- v1.4.3.0: [`reviewer-letter-1.4.3.0.md`](reviewer-letter-1.4.3.0.md) (plugin lifecycle + manifest flags)
- v1.4.0.0: [`reviewer-letter-1.4.0.0.md`](reviewer-letter-1.4.0.0.md) (plugin-system policy 10.2.2 — still load-bearing)
