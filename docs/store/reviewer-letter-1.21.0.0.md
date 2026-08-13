# Notes for certification — reviewer letter (v1.21.0.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission
> options** → **Notes for certification**.
>
> **This submission spans six versions.** Certification last saw **v1.15.0.0**; v1.16 through v1.21
> shipped to GitHub but were never submitted, because the Store lane was parked on purpose. A
> reviewer opening this will see a version number six ahead of the one on record, so the letter
> leads with that rather than letting it read as a discrepancy discovered halfway down.
>
> **Framing note.** The two questions a reviewer asks first are *does this add capabilities?* (no —
> the manifest is unchanged since v1.4) and *what leaves the machine?* (nothing new; the only
> outbound calls are the same documented Roblox endpoints, the update feed, and a Discord webhook
> the user pastes in themselves). Both are answered in the first fifteen lines.
>
> The plugin-system paragraph is load-bearing and is carried forward from the v1.4 letter, because
> **policy 10.2.2** is the single most likely reason this app gets a second look: the Store edition
> deliberately ships no in-app plugin catalog. That has been true since v1.4 and stays true here.
>
> Sources: `docs/store/release-notes-1.17.0.0.md`, `-1.18`, `-1.19`, `-1.20`, `-1.21`, plus the
> feature ledger rows in `docs/features.md` for v1.16, which has no release-notes file of its own,
> and `docs/superpowers/research/2026-08-04-rororo-settings-ui-audit-findings.md`.

---

```
Hello reviewer,

Thank you for your time on v1.21.0.0.

VERSION JUMP - PLEASE READ FIRST

Certification last reviewed v1.15.0.0. This submission is v1.21.0.0.
The intervening builds (1.16 through 1.20) were released to our
GitHub channel but never submitted to the Store, so nothing was
withdrawn, rejected, or hidden - the Store lane was simply paused
while the UI work below was in progress. This submission carries all
of it at once.

TWO ANSWERS UP FRONT

  1. NO new package capabilities. The manifest declares runFullTrust
     and nothing else, unchanged since v1.4.0.0. No
     broadFileSystemAccess, no internetClient.

  2. NOTHING new leaves the machine. Outbound calls are unchanged:
     the documented Roblox authentication-ticket endpoints, our
     update feed, and - only if the user pastes in a webhook URL
     they created themselves - Discord. A fresh install makes no
     Discord calls at all.

WHAT THIS APP DOES

RoRoRo lets one person run several Roblox clients side by side and
sign each into a different account they own. It does this two ways,
both documented and neither involving modification of the Roblox
client:

  - It holds the named mutex Roblox uses to detect that a copy is
    already running, so a second client starts instead of handing
    off to the first.
  - It uses Roblox's own published authentication-ticket flow
    (cookie -> CSRF -> RBX-Authentication-Ticket -> roblox-player:
    URI) to launch a chosen saved account.

We do not inject code into Roblox, patch it, automate input, or ship
macros. That is a deliberate product boundary, not an omission.

Requests identify themselves as ROROROblox/<version>. We do not
spoof a browser user agent.

CREDENTIAL HANDLING

Saved Roblox session cookies are encrypted with Windows DPAPI, per
user and per machine. A copied data file cannot be decrypted on
another PC. Cookies never leave the device except in the user-driven
export, which is separately passphrase-encrypted to a file the user
chooses.

PLUGINS AND POLICY 10.2.2

RoRoRo supports optional plugins that run as separate processes.
THE STORE EDITION SHIPS NO IN-APP PLUGIN CATALOG AND NO IN-APP
DOWNLOAD OR INSTALL PATH. This has been true since v1.4.0.0 and is
unchanged here. Store users who want a plugin obtain it themselves
from a web page outside the app. There is no downloadable-code
surface inside the Store package.

Plugins hold no permissions by default. Each capability is granted
by name by the user and can be revoked.

One plugin change in this submission is worth naming because it
affects process behaviour: plugin processes are now attached to
RoRoRo via a Windows job object, so they terminate when RoRoRo
terminates, including on an abnormal exit. Previously an orphaned
plugin process could outlive the host. This strictly reduces what
runs on the user's machine.

WHAT CHANGED SINCE v1.15

  - v1.16: Settings became a five-page layout, and every
    interactive control's boundary was brought to WCAG 1.4.11's
    3:1 under every theme. It had been at 1.26:1 in the default
    theme.
  - v1.17: a fourth built-in theme, "flatline", which carries no
    information in colour at all and clears 4.5:1 on every declared
    pair with no exemptions. Building it fixed five status surfaces
    that had been ignoring the active theme, and added non-colour
    markers to expired rows and warning chips in every theme.
  - v1.18: the settings that previously required hand-editing a
    JSON file (memory thresholds, alert timing, startup behaviour)
    moved onto those pages.
  - v1.19: plugins can ask the host for its current colours instead
    of hard-coding them. No new capability; the palette describes
    the app, not the user.
  - v1.20: the app now owns its button rendering rather than
    inheriting Windows defaults. This fixed a contrast defect where
    disabled labels measured 1.29:1 against their own background.
  - v1.21: accessibility and presentation work, including a theme
    whose secondary text had been below the 4.5:1 AA floor since it
    shipped, plus the plugin lifetime fix above and a correction to
    how the app detects a pending Roblox update.

ACCESSIBILITY

This submission fixes two measured contrast failures (4.19:1 and
1.29:1) against the 4.5:1 AA floor, and adds automated gates that
measure rendered pixels rather than declared values. One built-in
theme, "flatline", carries no information in colour at all.

TESTING

1,643 automated tests pass. We do not run automated end-to-end tests
against live roblox.com, deliberately - we do not want to generate
automated traffic or bot-flagged accounts against a third party.
That path is verified by manual smoke on a clean Windows 11 install.

Happy to answer anything. Contact details are on the submission.

- Estevan Hernandez, 626 Labs LLC
```
