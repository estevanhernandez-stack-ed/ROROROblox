# RoRoRo v1.34.0.0 — release notes

> **Edit log.** A MINOR bump from 1.33.0.0 per the playbook's bump table: one new feature surface
> (theme sharing), a user-visible change to how metric alerts name accounts, idle-threshold
> behaviour, and clarifying copy. Draft; the version is not tagged.
>
> 2026-10-23: completed the draft, which had covered only the Clarified copy. Added theme
> sharing, metric-alert scoping, the package-version fix and the plugin-facing HostInfo change.
> Corrected the Silent line, which named Ur Score as a plugin that plays sounds; Ur Score has no
> sound code, and the in-app hint now names no plugin, so it cannot go stale. Same error v1.33's
> notes made.

---

# RoRoRo v1.34.0.0

## New

- **Share a theme, and import one.** The theme builder has **Share theme** and **Import theme** beside Open themes folder. Share saves your active custom theme as a file you can send to anyone; Import reads one back and applies it straight away. Built-in themes can't be shared or overwritten, and a theme that's already installed won't import twice.

## Changed

- **Metric alerts name only the accounts running on this PC.** Before, an alert could name every saved account Ur Score reported for, including alts that weren't open. Now it lists only the ones running here, like "3 accounts running here."
- **When none of yours moved it, the alert says so.** A clan-wide number can cross because of other players or an alt on another PC. That alert now reads: "None of your running accounts moved this. It came from other players, or from an alt on another PC."
- **The idle threshold stays inside the offered set.** The picker offers 10, 12, 15 and 18 minutes. A stored value outside that set (a hand edit, say) now reads as the nearest offered one, ties to the lower, instead of silently running at a number the picker can't show. Reading never writes back, so the file keeps what you typed until you pick.
- **A hand-edited idle threshold lands within about 30 seconds.** RoRoRo re-reads it on its 30-second tick, so editing `settings.json` no longer waits for a restart.

## Fixed

- **About shows the version you actually have installed.** Microsoft Store and sideload builds now report their real package version in About and in the log.

## Clarified

- **Roblox presence privacy means no idle alerts.** An account with presence privacy on (Invisible) never gets an idle alert, because RoRoRo can't see it in a game and idle alerts only fire for accounts it sees in one. Settings says so under the idle controls now.
- **Silent covers RoRoRo's own alerts only.** Plugins that play sounds have their own sound settings, and the alert-sound picker now says so beside "Silent still draws the notification."
- **Idle counts per account, from the foreground client.** Working in another program counts as idle for every alt. Settings says so under the idle threshold picker.

## For plugin authors

- **HostInfo reports build identity.** `GetHostInfo` gains `install_flavour`, `process_path` and `informational_version` (fields 4 to 6; 1 to 3 are unchanged). Plugin contract 0.11.0 to 0.12.0. A plugin can now tell a Store install from a sideload or a direct download without guessing from paths.
