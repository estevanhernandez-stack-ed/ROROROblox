# RoRoRo v1.34.0.0 — release notes

> **Edit log.** A MINOR bump from 1.33.0.0 per the playbook's bump table: user-visible copy and
> idle-alert behaviour change, no new feature surface. This is a draft; the version is not tagged.

---

# RoRoRo v1.34.0.0

## Clarified

- **Roblox presence privacy means no idle alerts.** An account with presence privacy on (Invisible) never gets an idle alert, because RoRoRo cannot see it in a game and idle alerts only fire for accounts it sees in one. Settings says so under the idle controls now.
- **Silent covers RoRoRo's own alerts only.** Plugins such as Ur Score play their own sounds from their own settings, and the alert-sound picker now says so beside "Silent still draws the notification."
- **Idle counts per account, from the foreground client.** Working in another program counts as idle for every alt. Settings says so under the idle threshold picker.
- **The idle threshold stays inside the offered set.** The picker offers 10, 12, 15 and 18 minutes; a stored value outside that set (a hand-edit, say) now reads as the nearest offered one, ties to the lower, instead of silently running at a number the picker cannot show. A read never writes back, so the file keeps what you typed until you pick.
- **A hand-edited idle threshold lands within about 30 seconds.** RoRoRo re-reads it on its 30-second tick, so editing `settings.json` no longer waits for a restart.
