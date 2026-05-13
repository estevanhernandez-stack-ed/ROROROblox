# Release notes — v1.4.2.0

> Paste the block between the `---` markers below into the GitHub Release body.
> Tone: clan-facing (Pet Sim 99 audience). Plain, warm, no Microsoft-reviewer formality.
> Small minor bump — one launcher-side fix for a multi-launch captcha bug. Lead with the user-visible outcome, not the diff.

---

## Download

[**rororo-win-Setup.exe**](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/rororo-win-Setup.exe) — single click, installs to your user profile, lands in the Start Menu.

If you've already got RoRoRo installed via the installer or the Microsoft Store, this update rolls out automatically — no second prompt.

## What changed

### Captcha gates show the right account during multi-launch

If Roblox asked you to verify yourself during a multi-launch — the puzzle gate that pops up once in a while when you sign in — the captcha sometimes showed the **wrong account's name** at the top. Worse: if you solved it and tapped Submit, Roblox would log you in as that wrong account instead of the one the captcha appeared for.

That's fixed.

What was happening: Roblox brands the captcha gate from a small file on your PC — `appStorage.json` — that stores the "current account" identity. When you multi-launch four alts in quick succession, each Roblox client writes its own identity to that file a few seconds after it attaches. So a captcha that fired for alt #2 was sometimes reading identity from alt #3's later write, because alt #3 won the write race.

The fix is a tiny defender that runs once per launch on RoRoRo's side: it stamps the correct identity into `appStorage.json` the moment we dispatch a launch, then watches that file for 12 seconds and re-stamps it if a sibling Roblox client overwrites it. Each launch gets its own defense window; new launches gracefully take over from the previous one. We also bumped the gap between back-to-back launches from 1.5 seconds to 5 seconds, which gives each launch more breathing room.

You'll see this most if you multi-launch four or more alts at once and Roblox decides to gate one of them. Solo launches are unaffected.

## Compatibility

- No changes to **saved accounts**, **mutex handling**, **auth-ticket flow**, or the **plugin contract**. Plugins built against `ROROROblox.PluginContract` v0.1.0 keep working unchanged.
- No new Windows permissions. No new network calls. No new dependencies.
- Saved accounts, favorites, private servers, default-game widget, renames, themes, installed plugins, consent grants — all unchanged from v1.4.1.
- Multi-launch is slightly slower end-to-end (5s between launches instead of 1.5s). For four alts that's about 10 extra seconds total — the trade is worth it.

## Other channels

- **Microsoft Store *(recommended)*** — [Install RoRoRo on the Microsoft Store](https://apps.microsoft.com/detail/9NMJCS390KWB). Signed by Microsoft, bypasses SmartScreen, auto-updates through the Store.
- **MSIX sideload** — `RORORO-Sideload.msix` + `dev-cert.cer` are also attached if you prefer the cert-import flow over `Setup.exe`. Same dev-cert as v1.3.x / v1.4.x — if you've already imported it once, no re-import needed.

## Issues, ideas

- **Bug or suggestion:** [open an issue](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/new)
- **Privacy details:** [privacy policy](https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/)
- **Plugin authors:** [author guide](https://github.com/estevanhernandez-stack-ed/ROROROblox/blob/main/docs/plugins/AUTHOR_GUIDE.md)

A 626 Labs product.
