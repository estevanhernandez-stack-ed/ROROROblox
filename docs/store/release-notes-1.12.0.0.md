# Release notes — v1.12.0.0 (Store candidate)

> Paste the block between the `---` markers below into the GitHub Release body.
> **Bundles two merged PRs since v1.11.1.0** (#68 fail-closed window probe, #69 the
> memory watchdog). This is the build headed to the Microsoft Store.
> Publish call (prerelease vs latest) is yours at draft-review time.
>
> Note for the reviewer letter: this release reads a per-process memory *counter*
> for clients RoRoRo launched. That is a meaningful change to the standing "does not
> read memory from the Roblox client" line and is handled explicitly in
> [`reviewer-letter-1.12.0.0.md`](reviewer-letter-1.12.0.0.md).

---

## Your windows were closing. Here's why, and what we did.

A couple of you reported alt windows shutting themselves down after 20-30 hours. We chased it, and the answer isn't RoRoRo — **it's a memory leak in the Roblox client itself.** Every client slowly eats more RAM the longer it runs. Run several at once and your machine hits its ceiling, and Windows starts killing things.

We measured it on a real rig: one account climbed from 1.7 GB to 6 GB over 14 hours just sitting in an event. Under active play we clocked closer to a gigabyte an hour, per client.

We can't fix Roblox's leak. What we can do is see it coming and give you a one-click way out.

## RoRoRo now watches your memory

Every account row shows what that client is actually using. When one gets heavy — or when your whole machine is heading for the wall — the row turns amber, the tray icon changes, and you get a **Recycle** button.

**Recycle** closes that one client and reopens it right back into the same game or private server. Memory drops back to a fresh start, everything else keeps running, and you don't lose your other alts. Closing and reopening is the only way to actually get that memory back on Windows — so this is the real fix, not a workaround.

The thresholds set themselves from how much RAM you have, so a 16 GB laptop and a 64 GB tower each get sensible numbers without you touching anything.

**What to poke at:** run a few alts for a while and watch the numbers climb on the rows. When something warns, hit Recycle and confirm it lands you back where you were.

## Clearing out Roblox's leftovers

Roblox leaves invisible leftover processes behind when a client closes — no window, just sitting there. If RoRoRo spots them on startup it'll offer to **Clear strays**, which cleans up only the dead ones and never touches a game you have open.

## A safety fix worth mentioning

RoRoRo decides whether a Roblox process is "just a leftover" or "a game you're playing" before it ever closes anything. If it couldn't tell, it used to guess *leftover*. Now it guesses *you're playing* and asks first. Worst case you get one extra prompt; the old worst case was losing a session.

## For plugin authors

Plugin contract **0.7.0** adds a memory-pressure subscription, so a plugin can react when an account gets heavy — recycle it and walk the character back where it belongs. Behind the same per-capability consent prompt as everything else. See the [author guide](https://github.com/estevanhernandez-stack-ed/ROROROblox/blob/main/docs/plugins/AUTHOR_GUIDE.md).

## Download

[**rororo-win-Setup.exe**](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/download/v1.12.0.0/rororo-win-Setup.exe) — single click, installs to your user profile. Installing over an older version keeps your saved accounts.

## Compatibility

- Saved accounts, launch flow, presence, themes, streamer mode, existing plugins: all unchanged.
- The watchdog is on by default and costs nothing — it reads a counter Windows already publishes.
- Nothing new leaves your PC. Memory readings stay in your local log like everything else.

## Known rough edges

- **Windows hides new tray icons.** The tray warning works, but Windows 11 tucks new icons behind the `^` arrow by default. Click it and drag RoRoRo out to keep it visible.
- **Time-to-full is a rough estimate.** The per-client numbers are exact; the "your machine fills up in about N hours" projection is a straight-line guess and real memory doesn't climb in a straight line. Treat it as a nudge, not a countdown. We're collecting real curves to sharpen it.

## Found something?

[Open an issue](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/new) or ping the Discord. Logs live at `%LOCALAPPDATA%\ROROROblox\logs\` — they now include a memory line every 15 minutes, which is exactly what we need to see if you report a window dying.

A 626 Labs product.
