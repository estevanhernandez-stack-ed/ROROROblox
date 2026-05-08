# Release notes — v1.3.0.0

> Paste the block between the `---` markers below into the GitHub Release body.
> Tone: clan-facing (Pet Sim 99 audience). Plain, warm, no Microsoft-reviewer formality.

---

## Download

[**rororo-win-Setup.exe**](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/rororo-win-Setup.exe) — single click, installs to your user profile, lands in the Start Menu.

If you've already got RORORO installed via the installer or the Microsoft Store, this update rolls out automatically — no second prompt.

## What changed

### Rename anything to whatever you want — locally

Long Roblox-side game names crowding your row? Want your alt to read "Mr. Solo Dolo" instead of `xX_Estevan_2017_Xx`? Right-click any saved game, saved private server, or account row and pick **Rename…**.

Type whatever you want. Hit Enter. The Roblox-side name stays untouched — your alt is still named the original thing on Roblox. Just the row in RORORO carries your local name. Hit **Reset to original** in the popup any time.

The local name shows up everywhere the row used to: account row, compact mode, the per-row game picker, the Squad Launch sheet, the follow chips. Your renames are saved between RORORO updates and survive re-adding the same game or server.

### Default-game widget — quick-switch from the toolbar

The default game (the one Launch As uses when you haven't picked a per-row game) now lives in the toolbar, between **Games** and **Launch multiple**. Click it for a dropdown of your saved games. Pick one — that's your new default. Right-click any row in the dropdown for the four-item menu (Set as default · Rename… · Reset name · Remove).

If you haven't saved any games yet, the widget shows a one-click "Add a game" entry that drops you into the Games sheet.

## Compatibility

- Old `accounts.dat`, `favorites.json`, `private-servers.json` files load cleanly — no migration step. Renames you set in v1.3 also load fine on older RORORO builds (older builds just ignore the new field on read).
- No new Windows permissions. No new network calls. The rename overlay is local-only — it never talks to roblox.com.

## Other channels

- **Microsoft Store *(recommended)*** — [Install RoRoRo on the Microsoft Store](https://apps.microsoft.com/detail/9NMJCS390KWB). Signed by Microsoft, bypasses SmartScreen, auto-updates through the Store.
- **MSIX sideload** — `RORORO-Sideload.msix` + `dev-cert.cer` are also attached if you prefer the cert-import flow over `Setup.exe`.

## Issues, ideas

- **Bug or suggestion:** [open an issue](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/new)
- **Privacy details:** [privacy policy](https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/)

A 626 Labs product.
