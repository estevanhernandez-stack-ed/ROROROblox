# Release notes — v1.4.1.0

> Paste the block between the `---` markers below into the GitHub Release body.
> Tone: clan-facing (Pet Sim 99 audience). Plain, warm, no Microsoft-reviewer formality.
> Small BUILD bump — one plugin-surfaced bugfix. Lead with the user-visible outcome, not the diff.

---

## Download

[**rororo-win-Setup.exe**](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/rororo-win-Setup.exe) — single click, installs to your user profile, lands in the Start Menu.

If you've already got RoRoRo installed via the installer or the Microsoft Store, this update rolls out automatically — no second prompt.

## What changed

### Per-alt plugin behavior works on first launch now

If you installed a plugin that does anything per-alt — RoRoRo Ur Task's per-account macro playback is the obvious one — the plugin couldn't tell your alts apart on a fresh RoRoRo session until you opened the Friends modal on each account. Macro playback would either hop between alts or fire on whichever account happened to have focus, instead of staying pinned to the one you recorded against.

That's fixed. RoRoRo now hands plugins the right per-account ID on first launch, so per-alt behavior works the moment a plugin starts. No Friends-modal warm-up step.

What was happening: the in-memory account record carried the Roblox user ID, but the snapshot we hand to plugins over the plugin pipe wasn't reading it on construction — it was waiting for the Friends modal to fill it in. Plugins saw `0` for every alt, and `0` matches everything trivially. The store already persisted the value; the snapshot just needed to read it on construction. One line.

## Compatibility

- No changes to **launcher behavior**, **saved accounts**, **mutex handling**, or **auth-ticket flow.** Multi-instance launches behave identically to v1.4.0.
- No changes to the **plugin contract.** Plugins built against `ROROROblox.PluginContract` v0.1.0 keep working unchanged — the fix is on the RoRoRo side of the pipe.
- No new Windows permissions. No new network calls. No new dependencies.
- Saved accounts, favorites, private servers, default-game widget, renames, themes, installed plugins, consent grants — all unchanged from v1.4.0.

## Other channels

- **Microsoft Store *(recommended)*** — [Install RoRoRo on the Microsoft Store](https://apps.microsoft.com/detail/9NMJCS390KWB). Signed by Microsoft, bypasses SmartScreen, auto-updates through the Store.
- **MSIX sideload** — `RORORO-Sideload.msix` + `dev-cert.cer` are also attached if you prefer the cert-import flow over `Setup.exe`. Same dev-cert as v1.3.x / v1.4.0 — if you've already imported it once, no re-import needed.

## Issues, ideas

- **Bug or suggestion:** [open an issue](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/new)
- **Privacy details:** [privacy policy](https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/)
- **Plugin authors:** [author guide](https://github.com/estevanhernandez-stack-ed/ROROROblox/blob/main/docs/plugins/AUTHOR_GUIDE.md)

A 626 Labs product.
