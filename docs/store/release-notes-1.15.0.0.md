# RoRoRo v1.15.0.0 — release notes

## Know when something goes wrong while you're away from the PC

This is the release's real feature.

Run eight accounts and you can't watch all eight. A client drops out of a game, or starts eating
enough memory that it's heading for trouble, and you find out an hour later when you come back.
RoRoRo can now tell you the moment it happens — on your phone, through Discord.

**Two things are worth interrupting you for**, and only two: an account dropping out of a game
when you didn't ask it to, and a client crossing a memory threshold. Each one is routed
separately, so you can send health noise to your own channel and only the interesting things to
your clan's.

**Closes you asked for stay silent.** Stop, Recycle, and quitting RoRoRo don't alert. Being told
that the thing you just closed is closed isn't news — and since the memory warning suggests
recycling, alerting on it would mean the app paged you for taking its own advice.

**Any account can be muted on its own** from its row's right-click menu. Routing is per-trigger,
muting is per-account, and that's the whole configuration surface.

**Eight accounts crossing at once is one message**, not eight. A client that keeps flapping gets
one alert every five minutes, not one per flap.

### Setting it up

The only real step is a Discord webhook, and it starts one click earlier than most guides assume.
If you've only ever *joined* a Discord server, there's a walkthrough in Settings for making one of
your own — it's free, it can be just you, and nobody else can see it.

Paste the wrong thing and RoRoRo tells you what you actually pasted — a server invite, a channel
link, a bot token — instead of "invalid URL." Paste the right thing and it tells you which channel
it will post to, before the first alert lands somewhere you didn't expect. **Send test** sends a
real message down the real path, so "it says it's connected but nothing arrives" surfaces while
you're sitting there rather than at 3am.

Settings says plainly what will and won't reach your phone — including when a webhook has been
deleted, which Discord never tells you about.

**Alerts can never contain a private-server link.** That's enforced by how the message is built,
not by remembering not to.

## Also: your Discord status can show what you're playing

Smaller, and off by default. Turn it on and your Discord status shows the game, how many of your
accounts are in it, and how long you've been going. Nothing to set up — it uses the Discord app
already on this PC.

One limit worth knowing: while Roblox is running, your friends see Roblox on your profile rather
than RoRoRo. Discord gives that spot to games it recognizes, and RoRoRo isn't on that list yet —
apps get there once enough people are running them. Your own card is always correct. **The
groundwork is in place for when that changes**, including a Join button that drops a friend into
the same server.

## The name

RoRoRo now introduces itself as RoRoRo. It had been showing up as `ROROROblox.App` in Task
Manager, the UAC prompt, and Windows' file details, and as `RORORO` in the installer and the Store
package. Cosmetic, but it's the name people see before they ever open the app.

## Fixes

- A memory warning no longer silences a genuine dropped-out alert for the same account.
- Alert settings take effect immediately instead of when the Settings window closes.
