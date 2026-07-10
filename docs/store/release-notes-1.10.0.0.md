# Release notes — v1.10.0.0 (testing pre-release / RC)

> Paste the block between the `---` markers below into the GitHub Release body.
> **This is a RELEASE CANDIDATE — Velopack/direct-download only.** Publish with
> `--prerelease`, NOT `--latest`: v1.9.1.0 stays the pre-release testers had, and no
> stable install is auto-updated to this. The Store submission follows after this RC
> proves out.
> **Bundles eight merged PRs since v1.9.1.0** (#53 brand, #54 default private server,
> #55 trust-aware squad launch, #57 activity crediting, #58 launch-to-home, #59
> start-anyway restore, #60 plugin UI-handle ownership, #62 agent-ops + seamless
> takeover + singleton name-race fix).
> **Updater is still check-only** — download+apply not in this bump.

---

## This is a release candidate

An early build for testers. It won't be pushed to existing installs, and the Microsoft Store version comes later once this round proves out. Want stable? Grab [v1.9.1.0](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/tag/v1.9.1.0).

## Download

[**rororo-win-Setup.exe**](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/download/v1.10.0.0/rororo-win-Setup.exe) — single click, installs to your user profile. Installing over 1.9 keeps your saved accounts.

## What's new to test

### The "Roblox is already running" popup is basically gone

If Roblox starts with Windows, it sits in your system tray holding the multi-instance lock, and RoRoRo used to greet you with a "Roblox is already running" popup on *every* launch. Now RoRoRo just quietly takes the lock over and starts — no popup, no clicking. It closes the windowless tray client, grabs the lock, and puts a tray client right back.

The popup still shows in the one case where it matters: you have an actual Roblox game window open. RoRoRo won't close a client you're playing in without asking.

**What to poke at:** have Roblox running in the tray, open RoRoRo, confirm it starts clean with no popup. Then open a game, launch RoRoRo, and confirm you *do* get the popup (nothing should close a live game silently).

### Retry works the first time now

If you *did* hit that popup, closed Roblox, and clicked Retry — it sometimes needed a second click. That's fixed. Retry now waits the beat it takes Roblox to fully let go of the lock, so one click does it.

### Squad Launch got a lot smarter

Launching a squad now runs in three phases — launch the trusted ones directly, anchor on account #1, then follow the rest in — with a **careful mode** that waits for each account to actually land before moving on. There's a per-account **Join via friend** toggle so you control which accounts follow versus launch on their own.

**What to poke at:** run a squad with careful mode on and off, flip Join-via-friend per account, watch the order things launch in.

### Set a default server and a default game

Mark a private server as your **default** (it gets a DEFAULT badge and jumps to the top), and mark a game as your default too. Launch with no default set and you land on the Roblox home page instead of nowhere. Clear either default anytime.

**What to poke at:** set and clear defaults, launch with and without one set.

### For plugin users (Ur Task and friends)

- Keep-alive plugins can now tell RoRoRo an account is still active, so the idle warning stops misfiring while a plugin is tapping a window for you.
- Plugins can ask RoRoRo to close accounts (for outage recovery — clear the dead clients, relaunch).
- Security hardening under the hood: a plugin can only touch UI it created, and the plugin permission gate now fails closed. Nothing you'll see, everything you'd want.

### "Start anyway" is back

The blocked-startup popup has its **Start anyway** button again — proceed without the lock when another RoRoRo or a compatible tool already holds it.

## Compatibility

- Saved accounts, launch flow, presence, themes, existing plugins: all unchanged.
- Plugin authors: the plugin contract moves to 0.6.0 (additive — existing plugins keep working).
- Nothing new leaves your PC.

## Found something?

That's the point of an RC — [open an issue](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/new) or ping the Discord. Logs live at `%LOCALAPPDATA%\ROROROblox\logs\`.

A 626 Labs product.
