# Release notes — v1.5.0.0

> Paste the block between the `---` markers below into the GitHub Release body.
> Tone: clan-facing (Pet Sim 99 audience). Plain, warm, no Microsoft-reviewer formality.
> Minor bump centered on the account list telling the truth — rows now show the game each alt is actually in, stop falsely reporting "Closed" when an alt is still running, and Launch multiple tells you exactly what it launched and what it skipped.

---

## Download

[**rororo-win-Setup.exe**](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/rororo-win-Setup.exe) — single click, installs to your user profile, lands in the Start Menu.

If you've already got RoRoRo installed via the installer or the Microsoft Store, this update rolls out automatically — no second prompt.

## What changed

### Your alts now show the game they're actually in

Each account row reads the live game — **"In Pet Sim 99"**, **"In Adopt Me"** — instead of a vague "active." Exit the game but stay in the client and it switches to **"At Roblox home."** Open Studio and it says **"In Studio."** The status comes from Roblox's own presence, so it's the real state, not a guess.

### Fixed: accounts falsely showing "Closed" while still running

This is the big one. If you launched several alts, RoRoRo could show only the most-recent one as running and mark the rest "Closed" — even though every client was alive and in-game. That was RoRoRo losing track of the windows, not your alts actually closing.

Now the running state comes from Roblox presence (server truth), with the local window tracking as a backup. A row shows as active when **either** signal says so, and only flips to "Closed" when **both** agree the client is really gone. Losing track of a window can no longer make a live, in-game alt read "Closed."

### Launch multiple tells you what it did — and what it skipped

Before, if Launch multiple skipped an account (because it was already running), it just quietly launched the rest and never said why. Now:

- The result names the skips: **"Launch multiple finished. 6 clients dispatched. (1 already running.)"** No more silent counts.
- If nothing's eligible, it says so plainly — **"Nothing to launch — 7 already running."** — instead of looking like the button did nothing.
- It double-checks each alt's live state right before launching, so an alt you just closed gets picked up instead of wrongly skipped. (One caveat: Roblox's presence takes a few seconds to catch up after a client closes — if you close an alt and *immediately* relaunch, give it a moment and retry.)

## Compatibility

- No changes to **saved accounts**, **mutex handling**, the **auth-ticket flow**, **plugins**, favorites, private servers, themes, or renames — all unchanged from v1.4.3.
- **One new outbound call:** RoRoRo now reads each saved account's own Roblox presence (`presence.roblox.com`) on a light ~25-second timer, using that account's own login. It only ever asks about your own accounts. No new Windows permissions, no new dependencies.
- Cookies are never logged and never leave your machine — same as always.

## Other channels

- **Microsoft Store *(recommended)*** — [Install RoRoRo on the Microsoft Store](https://apps.microsoft.com/detail/9NMJCS390KWB). Signed by Microsoft, bypasses SmartScreen, auto-updates through the Store.
- **MSIX sideload** — `RORORO-Sideload.msix` + `dev-cert.cer` are also attached if you'd rather install a package than run `Setup.exe`. Download both, right-click `dev-cert.cer` → Install → Local Machine → Trusted People, then open the `.msix`. Not sure? Just use `Setup.exe` above; it needs none of this.

## Issues, ideas

- **Bug or suggestion:** [open an issue](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/new)
- **Privacy details:** [privacy policy](https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/)
- **Plugin authors:** [author guide](https://github.com/estevanhernandez-stack-ed/ROROROblox/blob/main/docs/plugins/AUTHOR_GUIDE.md)

A 626 Labs product.
