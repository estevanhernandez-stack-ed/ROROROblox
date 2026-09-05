# Phone-push disclosure fragment — for the next reviewer letter (v1.25+)

> The v1.24 letter's standing line "network behaviour is unchanged" retires the release this
> ships. This fragment is the replacement paragraph, written in the `discord-disclosure.md` mold
> that has passed certification, so the letter author pastes rather than re-derives. Source of
> truth for the claims: `docs/superpowers/specs/2026-09-04-phone-alerts-design.md` and
> `docs/PRIVACY.md`'s network table.

---

```
NEW IN THIS VERSION: OPTIONAL PHONE NOTIFICATIONS, OFF BY
DEFAULT, TO ENDPOINTS THE USER CONFIGURES.

The app's existing alerts (an account dropped out; a memory
warning) can now be routed to the user's phone through one of
two push services the user sets up themselves in Settings:
Pushover (api.pushover.net, using the user's own account key
and application token) or ntfy (ntfy.sh by default, or a server
address the user enters). A fresh install makes no calls to
either host: the feature is off until the user picks a service
and pastes its credentials, which are stored DPAPI-encrypted on
the user's machine and never logged. What is transmitted when
an alert fires is the alert's title and text — an account
display name and a game name — nothing more; no telemetry, no
analytics, and the app still ships no credentials of its own.
```
