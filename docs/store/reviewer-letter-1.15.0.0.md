# Notes for certification — reviewer letter (v1.15.0.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission options** → **Notes for certification**.
>
> v1.15 adds an optional Discord integration. The letter leads with the two things a reviewer will
> want answered before anything else — *does this add capabilities?* (no) and *what leaves the
> machine?* (nothing, until the user pastes a webhook URL they created themselves). The package
> `DisplayName` also changes from RORORO to RoRoRo in this release; that is called out explicitly
> so it does not read as an identity discrepancy.
>
> Source for the technical detail: [`discord-disclosure.md`](discord-disclosure.md).

---

```
Hello reviewer,

Thank you for your time on v1.15.0.0. This release adds an optional
Discord integration. Two answers up front, because they are the
questions the change raises:

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

Two independent halves, both OFF by default:

Rich presence — RoRoRo publishes what the user is playing to the
Discord desktop app already installed on the same PC, over a LOCAL
NAMED PIPE. This is the standard Discord IPC mechanism used by games
with a Discord status. It is not a network connection and it does not
leave the machine. A "Join" option lets a friend launch one of the
user's own saved accounts into the same game.

Alerts — RoRoRo can notify the user when one of their accounts drops
out of a game unexpectedly, or when a client crosses a memory-use
threshold. Alerts go to a Windows notification and/or to a Discord
channel the user chooses by pasting a webhook URL. RoRoRo cannot
discover, enumerate, or reach any Discord channel the user has not
explicitly supplied a webhook for.

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

Enabling the Join option registers a per-user (HKCU) roblox-rororo:
link handler so Discord can hand an inbound join back to the app —
the same mechanism any launcher uses for deep links. Every inbound
join shows a confirmation naming what is about to launch before
anything starts.

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
