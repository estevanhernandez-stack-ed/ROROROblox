# Discord integration — reviewer disclosure

> Fragment for the next Partner Center submission letter. Fold into the letter's body; don't ship
> as a standalone document. Covers the Discord presence + Join work and the alerts work
> (branches `feat/discord-presence`, `feat/discord-alerts`, `feat/discord-diagnostics`).

## What changed

RoRoRo gained an optional Discord integration with two halves, both off by default:

**Rich presence and Join.** When the user turns it on, RoRoRo publishes what they're playing to the
Discord client already installed on their PC, and can offer friends a Join button that launches one
of the user's own saved accounts into the same server.

**Alerts.** RoRoRo can tell the user when an account drops out of a game, or when a client's memory
use crosses the warning threshold. Alerts go to a Windows tray notification, and/or to a Discord
channel the user chooses by pasting a webhook URL they created themselves.

## No new package capabilities

`Package.appxmanifest` is unchanged. It still declares `runFullTrust` and nothing else.

- The Discord presence connection is a **local named pipe** to the Discord desktop client on the
  same machine. Named-pipe IPC is covered by `runFullTrust`; it needs no separate declaration, and
  it crosses no machine boundary.
- Webhook posts are **outbound HTTPS**, which requires no capability declaration.
- No `broadFileSystemAccess`. No `internetClient`. No background task. No new file locations
  outside the app's existing per-user data directory.

This is the same shape as the plugin host RoRoRo already ships (reviewed in v1.4): local IPC the
user explicitly opts into, plus — new here — an outbound call to an address the user typed in
themselves.

## New third-party library in the signed package

**DiscordRichPresence** (Lachee, MIT licence) — the Discord RPC client. It is the standard library
for this integration and is used only to talk to the local Discord client over the named pipe.

## New outbound host

`discord.com` — and only when the user has pasted a webhook URL and chosen a destination that uses
it. Both alert destinations default to off; a fresh install makes no Discord network calls at all.

Requests carry `User-Agent: RORORO/<version>`. RoRoRo does not spoof a browser User-Agent anywhere,
consistent with the Roblox-facing policy described in earlier letters.

## URI scheme registration

RoRoRo registers a `roblox-rororo:` URI scheme (per-user, `HKCU`) so Discord can hand an inbound
Join back to the app. This is the same mechanism any launcher uses for deep links. Inbound joins
are gated on the user's Join setting being on, and every join shows a confirmation prompt naming
what's about to launch before anything starts.

## What is deliberately NOT sent

Worth stating plainly, because it is enforced in the type system rather than by convention:

- **A webhook message can never carry a private-server link.** The payload type has exactly two
  string fields and no property a URL, invite, or access code could live in. A unit test fails the
  build if anyone adds one. A Discord Join secret reaches only people who can see the user's Join
  button; a channel post reaches everyone who ever reads that channel, including people who join it
  next year — so the two are held to different rules.
- **No cookies, tokens, or account credentials leave the machine.** Alert text carries a display
  name and a game name.
- **Streamer mode is honoured outbound.** When the user has streamer mode on, presence and alerts
  both publish the masked name, never the real Roblox username.
- **No telemetry.** RoRoRo does not report whether any of this was set up, used, or abandoned.

## Storage

Discord settings — including any webhook URLs the user pastes — are stored in `discord.dat` in the
app's per-user data directory, encrypted with DPAPI at `CurrentUser` scope, the same protection
already used for saved account cookies. A copied `discord.dat` cannot be decrypted on another
machine or by another Windows user.

Webhook URLs are treated as bearer credentials throughout: they are never written to the log file,
and neither are the exception objects from a failed post, since `HttpRequestException` messages
routinely carry the request URI.
