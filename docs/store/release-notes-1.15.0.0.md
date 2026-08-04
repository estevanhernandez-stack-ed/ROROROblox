# RoRoRo v1.15.0.0 — release notes

## Discord, if you want it

RoRoRo can now talk to the Discord app on your PC. Both halves are off until you turn them on, and
a fresh install makes no Discord connection at all.

**Show what you're playing.** Your Discord status shows the game, how many of your accounts are in
it, and how long you've been going. Nothing to set up — it uses the Discord app already on this PC.
Turn on Join and a friend can drop into the same server with one click, private servers included,
with a warning that they may not get in.

*Worth knowing:* while Roblox is running, your friends see Roblox on your profile instead of
RoRoRo. Discord gives that spot to games it recognises, and RoRoRo isn't on that list yet — it
gets there once enough people are running it. Your own card is always correct.

**Get told when something goes wrong while you're away.** Two things are worth interrupting you
for: an account dropping out of a game when you didn't ask it to, and a client eating enough
memory that it's heading for trouble. Send them to a Windows notification, to your own Discord
channel, or to a clan channel — each trigger routed separately, so health noise can go to you and
only the interesting things go to the clan.

Closes you asked for don't alert. Stop, Recycle, and quitting RoRoRo are all silent, because being
told that the thing you just closed is closed isn't news.

Any account can be muted on its own from its row's right-click menu.

**Setting up the webhook** is the only real step, and it starts one click earlier than most guides
assume — if you've only ever *joined* a Discord server, there's a walkthrough for making one of
your own. Paste the wrong thing and RoRoRo tells you what you actually pasted rather than "invalid
URL". Paste the right thing and it tells you which channel it posts to, before the first alert
lands somewhere you didn't expect. "Send test" sends a real message down the real path.

Alerts can never contain a private-server link. That's enforced by how the message is built, not
by remembering not to.

## The name

RoRoRo now introduces itself as RoRoRo. It had been showing up as `ROROROblox.App` in Task
Manager, the UAC prompt, and Windows' file details, and as `RORORO` in the installer and the Store
package. Cosmetic, but it's the name people see before they ever open the app.

## Fixes

- A memory warning no longer silences a genuine dropped-out alert for the same account.
- Alert settings take effect immediately instead of when the Settings window closes.
- Settings now says plainly what will and won't reach your phone, including when a webhook has
  been deleted — a state Discord never tells you about.
