# Notes for certification — reviewer letter (v1.25.0.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission
> options** → **Notes for certification**.
>
> **This is the letter that retires the standing "network behaviour is unchanged" line** — the
> headline feature adds two optional, off-by-default outbound services, disclosed up front in
> the `phone-push-disclosure.md` fragment (the `discord-disclosure.md` mold that has passed
> certification before). The manifest is untouched from v1.24 — after last cycle's
> manifest-delta letter, that is worth saying explicitly.
>
> Sources: `docs/store/release-notes-1.25.0.0.md`, `docs/store/phone-push-disclosure.md`,
> `docs/superpowers/specs/2026-09-04-phone-alerts-design.md`.

---

```
Hello reviewer,

Thank you for your time on v1.25.0.0. Certification last saw
v1.24.0.0; this is a single-version update. The manifest is
UNCHANGED from the version you certified — same capabilities
(runFullTrust only), same protocols, same startup task. The
change this letter must disclose is in network behaviour.

1. NEW: OPTIONAL PHONE NOTIFICATIONS, OFF BY DEFAULT, TO
   ENDPOINTS THE USER CONFIGURES.

   The app's local alerts (an account dropped out; a memory
   warning; and two new local kinds, a recycle completion and a
   periodic "still running" mark) can now be routed to the
   user's phone through one of two push services the user sets
   up themselves in Settings: Pushover (api.pushover.net, using
   the user's own account key and application token) or ntfy
   (ntfy.sh by default, or a server address the user enters). A
   fresh install makes no calls to either host: the feature is
   off until the user picks a service and pastes its
   credentials, which are stored DPAPI-encrypted on the user's
   machine and never logged. What is transmitted when an alert
   fires is the alert's title and text - an account display name
   and a game name - nothing more; no telemetry, no analytics,
   and the app still ships no credentials of its own. The
   published privacy policy (same URL) was updated to name both
   services before this submission.

2. ALERT ROUTING IS NOW MULTI-DESTINATION. Each alert kind can
   be sent to any combination of desktop notification, the
   user's own Discord webhook, a shared webhook, and the phone
   service above. This changes where existing local data goes at
   the user's direction; it collects nothing new.

3. A NEW LAUNCH OPTION (default on) clears a saved fullscreen
   flag in Roblox's local settings file before each launch - the
   same local file on the user's machine this app has always
   written the user's frame-rate preference into. Local file,
   local effect; nothing transmitted.

4. THE REST IS UI POLISH: a window-open rendering flash fixed,
   and settings fields that now display the values the app
   derives when left blank.

Network behaviour is otherwise unchanged and remains limited to
Roblox-owned endpoints and GitHub Releases (update checks and
the signed compatibility feed), with the app's own User-Agent.
Credential handling is unchanged: session cookies remain
DPAPI-encrypted, local-only, and are never exposed to plugins.

The trademark position is unchanged from prior certifications:
RoRoRo is an independent tool, not affiliated with Roblox
Corporation, and the disclaimer appears on the Store
description, the About box, and the privacy policy. Pushover
and ntfy are independent third-party services, named in the
listing and policy solely to describe the optional integration.

Thank you,
626 Labs LLC
```
