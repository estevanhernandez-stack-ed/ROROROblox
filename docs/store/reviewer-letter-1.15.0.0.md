# Notes for certification — reviewer letter (v1.15.0.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission options** → **Notes for certification**.
>
> v1.15 adds optional Discord alerting. The letter leads with the two things a reviewer will want
> answered before anything else — *does this add capabilities?* (no) and *what leaves the machine?*
> (nothing, until the user pastes a webhook URL they created themselves). The package `DisplayName`
> also changes from RORORO to RoRoRo in this release; that is called out explicitly so it does not
> read as an identity discrepancy.
>
> **Framing note.** The webhook alerting is the feature; the Discord status display is described as
> minor and "ready if that changes." That is honest — the status display is genuinely limited by
> Discord giving the profile slot to detected games — and it also keeps the reviewer's attention on
> the half where the security questions actually live. Leading with rich presence would invite
> questions about a capability that barely functions.
>
> Source for the technical detail: [`discord-disclosure.md`](discord-disclosure.md).

---

```
Hello reviewer,

Thank you for your time on v1.15.0.0. This release adds optional
Discord alerting, so a user running several Roblox clients finds out
when one of them fails while they are away from the PC. Two answers
up front, because they are the questions the change raises:

  1. It requires NO new package capabilities. The manifest still
     declares runFullTrust and nothing else.
  2. Nothing leaves the machine unless the user pastes in a Discord
     webhook URL they created themselves. A fresh install makes no
     Discord network calls at all.

DISPLAY NAME CHANGE

The package DisplayName changes from "RORORO" to "RoRoRo" in this
submission. This is a casing correction, not a new product — the app
has always presented itself as RoRoRo in its own UI, and the Store
package name had drifted. Identity Name (626LabsLLC.RoRoRoBlox),
Publisher CN, and PublisherDisplayName are UNCHANGED. Store listing
graphics have been re-uploaded to match.

WHAT THE INTEGRATION DOES

Two independent halves, both OFF by default. The substantive one is
the second.

Status display (minor) — RoRoRo can publish what the user is playing
to the Discord desktop app already installed on the same PC, over a
LOCAL NAMED PIPE. This is the standard Discord IPC mechanism used by
games with a Discord status. It is not a network connection and it
does not leave the machine. Its practical reach is currently limited
by Discord itself: while Roblox is running, Discord shows Roblox on
the user's profile rather than RoRoRo, because that slot goes to a
game Discord detects and RoRoRo is an application. We have built the
groundwork — including a "Join" option that launches one of the
user's OWN saved accounts into the same game — so the feature is
ready if that changes. Today it is a small convenience, and it is
described that way in the app.

Alerts by webhook (the actual feature) — this is what the release is
for. A user running several Roblox clients cannot watch all of them.
RoRoRo now notifies them when one of their accounts drops out of a
game unexpectedly, or when a client crosses a memory-use threshold,
so they find out while away from the PC instead of an hour later.

Delivery is a Windows notification and/or a Discord channel the user
chooses BY PASTING A WEBHOOK URL THEY CREATED THEMSELVES. That is the
entire mechanism — RoRoRo cannot discover, enumerate, browse, or
reach any Discord channel the user has not explicitly handed it a
webhook for, and it has no Discord account, bot, or OAuth
relationship of any kind. It never reads from Discord; messages flow
one way, outbound, to an address the user typed in.

Alerts are rate-limited by design (one message per account per five
minutes, and simultaneous events coalesced into a single message),
and closes the user initiated themselves are deliberately silent.

CAPABILITIES: NONE ADDED

  - The Discord connection is a local named pipe on the same machine.
    Named-pipe IPC is covered by runFullTrust and crosses no machine
    boundary. This is the same shape as the plugin host reviewed in
    v1.4.
  - Webhook posts are outbound HTTPS, which requires no capability
    declaration.
  - No broadFileSystemAccess. No internetClient. No background task.
    No new file locations outside the app's existing per-user data
    folder.

NEW THIRD-PARTY LIBRARY

DiscordRichPresence (Lachee, MIT licence) — the standard Discord RPC
client, used only to talk to the local Discord app over the named
pipe.

NEW OUTBOUND HOST

discord.com, and only when the user has pasted a webhook URL and
chosen a destination that uses it. Requests carry
User-Agent: RORORO/<version>. RoRoRo does not spoof a browser
User-Agent anywhere, consistent with previous submissions.

URI SCHEME

This belongs to the minor status-display half. Enabling its Join
option registers a per-user (HKCU) roblox-rororo: link handler so
Discord can hand an inbound join back to the app — the same mechanism
any launcher uses for deep links. Every inbound join shows a
confirmation naming what is about to launch before anything starts,
and the target is always one of the user's own saved accounts. The
alerting half registers nothing.

USER DATA

Discord settings, including any webhook URLs the user pastes, are
stored in discord.dat in the app's per-user data folder, encrypted
with DPAPI at CurrentUser scope — the same protection already used
for saved account cookies. Webhook URLs are treated as credentials:
never written to the log file, never included in a diagnostics
bundle.

Alert messages carry a display name and a game name. By design they
cannot carry a private-server link, invite, or access code: the type
that carries an alert has two text fields and no field such a value
could occupy, and a unit test fails the build if one is added.

NO TELEMETRY

Unchanged from every prior submission. RoRoRo has no analytics, no
telemetry, and no third-party SDKs. It does not report whether any of
this was set up, used, or turned off.

AGE RATING / CONTENT

No change. The integration adds no user-generated content surface, no
chat, and no social feed inside RoRoRo. Messages flow one way, out to
a channel the user configured, and RoRoRo never reads a Discord
channel.

TESTING NOTES

The Discord features need the Discord desktop app installed and signed
in to exercise fully, which certification hardware may not have. Both
halves are off by default and degrade silently — with Discord absent,
RoRoRo runs exactly as v1.14 did, and the Settings panel reports
"Discord isn't running" in plain language rather than erroring.
Multi-instance launching, account management, and every prior feature
are unaffected and testable without Discord.

Thank you again for your time.

Este Hernandez
626Labs LLC
```

---

## Reviewer-letter checklist for this submission

- [ ] Version in the letter matches the uploaded package (`1.15.0.0`).
- [ ] Listing graphics re-uploaded from `docs/store/graphics/` — the previous set read RORORO.
- [ ] Listing title updated in Partner Center to **RoRoRo — Multi-launcher for Windows**.
- [ ] `PublisherDisplayName` still reads `626Labs LLC` (no space) — matches Partner Center.
- [ ] Privacy policy URL still resolves; `docs/PRIVACY.md` now carries the Discord section.
