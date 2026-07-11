# Release notes — v1.11.0.0 (Store candidate)

> Paste the block between the `---` markers below into the GitHub Release body.
> **Bundles three merged PRs since v1.10.0.0** (#63 streamer mode, #64 portable
> taskbar icon fix, #65 final avatar art). This is the build headed to the
> Microsoft Store — the streamer-mode avatar set cleared the no-placeholder gate,
> so the Store MSIX is cut from this same version.
> Publish call (prerelease vs latest) is yours at draft-review time: if v1.10.0.0
> testing felt solid, this can be the one that goes `--latest`.

---

## Streamer mode is here

One flip and your whole roster goes incognito. Every saved account gets a silly fake identity — a name like **CaptainNoodle** and its own goofy avatar — so you can screen-share or stream RoRoRo without showing the world your alt list. Friends in the picker get disguises too, and private-server share links hide behind a reveal-only-for-you pill.

The disguises stick: alt #3 stays CaptainNoodle across restarts, so your stream doesn't reshuffle mid-series. Don't like a name? **Reroll** it — per account, or the whole roster at once. There are 98 names and 12 avatars in the pool, so every combo on your screen stays distinct.

Two honest notes:

- It masks **RoRoRo's window**, not what's inside Roblox. Your alt's real name still shows on leaderboards and in chat — that part is Roblox's, not ours.
- Roblox **window titles** get the fake name too, so alt-tabbing on stream doesn't leak either.

**What to poke at:** flip Streamer mode on, check every corner — account rows, friends picker, history, launch banners — for anything real leaking through. Reroll a few identities, restart, confirm they stick. Then flip it off and confirm everything comes back real.

## Twelve hand-drawn avatars

The disguise avatars are real character art now — a ramen bowl captain, a smug duck, a monocled gentleman cube, a waffle with a syrup scarf, and eight more friends. Drawn for RoRoRo in the 626 colors, and they hold up sharp even at tiny sizes.

## The portable build shows its icon now

If you run RoRoRo from the portable zip, the taskbar icon could show up as a blank generic window — especially if you'd ever installed an old version on the same PC. Fixed. The portable build now carries its own identity, so the RoRoRo mark shows on the taskbar no matter what's installed alongside it.

**What to poke at:** run the portable zip, check the taskbar button shows the RoRoRo mark.

## Download

[**rororo-win-Setup.exe**](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/download/v1.11.0.0/rororo-win-Setup.exe) — single click, installs to your user profile. Installing over an older version keeps your saved accounts.

## Compatibility

- Saved accounts, launch flow, presence, themes, existing plugins: all unchanged.
- Streamer mode is off by default — nothing changes until you flip it.
- Nothing new leaves your PC. The fake identities live in a local file like everything else.

## Found something?

[Open an issue](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/new) or ping the Discord. Logs live at `%LOCALAPPDATA%\ROROROblox\logs\`.

A 626 Labs product.
