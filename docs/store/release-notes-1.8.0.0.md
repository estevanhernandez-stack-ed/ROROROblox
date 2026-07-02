# Release notes — v1.8.0.0

> Paste the block between the `---` markers below into the GitHub Release body.
> Tone: clan-facing (Pet Sim 99 audience). Plain, warm, no Microsoft-reviewer formality.
> Feature release: three things — Limited-account handling, idle awareness, and the
> tray-residence fix. NOTE: do NOT claim Setup.exe installs auto-update — the in-app
> updater is still check-only; download+apply is now scoped to v1.9. Store installs
> update via the Store.

---

## Download

[**rororo-win-Setup.exe**](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/rororo-win-Setup.exe) — single click, installs to your user profile, lands in the Start Menu. Reinstalling over an old version keeps your saved accounts.

On the Microsoft Store version? It updates by itself through the Store. Installed with `Setup.exe`? Grab the new one above — takes a few seconds.

## What changed

Three real features this time, all born from things that actually bit us in the clan.

### Roblox hiding in your system tray can't stop RoRoRo anymore

Newer Roblox stays running in your system tray after you close its window — sometimes it even starts itself with Windows. That invisible Roblox holds the lock RoRoRo needs for multi-instance, and the old RoRoRo told you to close everything and restart the app. No more. Now RoRoRo checks the lock itself, the moment it matters:

- If a hidden Roblox has the lock, RoRoRo shows you exactly how to quit it from the tray (the little `^` near your clock) — or just click **Close Roblox for me** and RoRoRo handles it and carries on. **Retry** works too. You never restart RoRoRo.
- Leftover Roblox processes from an earlier session don't block anything anymore. RoRoRo tells you what it found — including which ones are real game windows — and lets you continue or clean them up. Cleaning up always asks first if any real windows are open.
- If Roblox grabs the lock *while* RoRoRo is running, a banner appears in the main window within a few seconds with the same one-click fixes. No more silent mystery errors.

### RoRoRo tells you when Roblox has flagged an account

If Roblox soft-locks one of your accounts (the "suspicious activity" / verification thing — it happens), that account used to just silently fail: frozen "In game" dot, launches doing nothing. Now the row tells the truth: **Limited by Roblox** with a magenta dot. While an account is limited, RoRoRo keeps it out of launches so you don't burn more trust with Roblox by hammering a flagged session — and the moment Roblox lifts the flag, the row clears on its own. Re-logging the account clears it too.

### See how long each account has been idle

Roblox kicks an idle client after about 20 minutes — and if a bunch of your alts idle out together, they all reconnect together, which is exactly what triggers the captcha wall. RoRoRo now watches how long it's been since anything touched each account's window and tells you before it becomes a problem:

- Each running account's row shows an idle chip ("idle 18m") that turns amber past your threshold.
- A tray notification warns you when accounts cross the line — one notification, even if five accounts cross at once. It re-arms only after the account is active again, so it never spams.
- Tune it in Preferences: the threshold (15 minutes by default) and a mute switch if you'd rather not hear about it.
- Plugins can ask RoRoRo for idle times too (with your permission — you'll see it on the consent sheet). That's the doorway for the upcoming **Ur AFK** plugin, which keeps your idle alts alive automatically. Watch this space.

RoRoRo only tracks *when* a window was last touched — a timestamp from Windows. It never sees what you type. Not the keys, not the content, nothing.

## Compatibility

- Verified against the current Roblox client (0.728) — including the new tray behavior.
- Nothing new leaves your PC. No new addresses, no new Windows permissions, no telemetry. The idle tracking is a local timestamp; the plugin query runs over the same local, consent-gated plugin pipe as v1.4.
- Saved accounts, the launch flow, presence, export/import, plugins, games, themes: all unchanged.

## Other channels

- **Microsoft Store *(recommended)*** — [Install RoRoRo on the Microsoft Store](https://apps.microsoft.com/detail/9NMJCS390KWB). Signed by Microsoft, bypasses SmartScreen, auto-updates through the Store.
- **MSIX sideload** — `RORORO-Sideload.msix` + `dev-cert.cer` are attached if you'd rather install a package than run `Setup.exe`. Download both, right-click `dev-cert.cer` → Install → Local Machine → Trusted People, then open the `.msix`. Not sure? Just use `Setup.exe` above.

## Issues, ideas

- **Bug or suggestion:** [open an issue](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/new)
- **Privacy details:** [privacy policy](https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/)

A 626 Labs product.
