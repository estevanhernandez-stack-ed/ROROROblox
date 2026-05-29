# Release notes — v1.7.0.0

> Paste the block between the `---` markers below into the GitHub Release body.
> Tone: clan-facing (Pet Sim 99 audience). Plain, warm, no Microsoft-reviewer formality.
> Feature bump: launches survive a Roblox update mid-batch, a one-click "close every Roblox," and a tray fix that recovers multi-instance without restarting.

---

## Download

[**rororo-win-Setup.exe**](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/rororo-win-Setup.exe) — single click, installs to your user profile, lands in the Start Menu.

If you've already got RoRoRo installed via the installer or the Microsoft Store, this update rolls out automatically — no second prompt.

## What changed

### Roblox can update mid-launch and your batch still lands

This is the big one. When Roblox decides it needs an update, the black installer box pops up right as a client launches — and if you were running Launch multiple, that used to break the batch: a different account would land than the one you picked, often with a captcha, or you'd get a scary "check your antivirus" error.

Now RoRoRo handles it for you. It sends your first client through the update, shows a clear **"Roblox is updating — hold on"** while the installer does its thing, waits for that client to come up, then launches the rest of the batch into the already-updated client. The update happens once, up front, and every account that follows lands the way you picked it. When there's no update pending, nothing slows down — your batch launches at normal speed.

One more thing: if you run Bloxstrap or Fishstrap, RoRoRo notices and stays out of the way, so you're never updated twice.

### Stop all Roblox instances — one click

New tray menu item: **Stop all Roblox instances**. It closes every running Roblox client at once. RoRoRo asks first ("N Roblox clients are running. This closes them all immediately.") so you don't fat-finger it, and if nothing's running it just does nothing. Only touches your own Roblox — nothing else on your PC.

### Recover multi-instance from the tray without restarting

If multi-instance dropped into an error state (the mutex got lost — usually because Roblox was already open before RoRoRo), you used to have to restart the whole app to get it back. Now the tray toggle reads **"Multi-Instance: ERROR — click to reload"** and re-grabs it right there. One click, you're back.

### Under the hood

- **Mutex name is now config-driven.** If Roblox ever renames the singleton it uses to block multiple clients, we can fix it with a quick config push instead of shipping a whole new app build — so you're back up faster. Falls back safely if the config can't be reached.
- **For plugin authors:** two new plugin abilities (launch an alt into a share-link/follow target, and read your most-recent saved private server). Same plugin rules as always — you approve each one, it runs walled-off from RoRoRo, and the package is checksum-verified before it installs.
- Fixed a startup crash that could close the app before the window even showed, plus a fuller automated test gate so fewer things slip through.

## Compatibility

- **One new outbound check.** To know whether a Roblox update is pending, RoRoRo asks Roblox's own documented client-version address (`clientsettingscdn.roblox.com`) what the latest version is and compares it to what's installed. That's the only new thing that leaves your PC, it identifies itself honestly as RoRoRo (no pretending to be a browser), and if it fails for any reason it just assumes "no update" and launches anyway — it never blocks you.
- The new **Stop all Roblox instances** only force-closes your own running Roblox clients when you click it and confirm. No admin rights, nothing else on the machine, and RoRoRo never touches or modifies the Roblox client itself — no injection, no macros.
- Everything else is unchanged: saved accounts and DPAPI vault, the auth-ticket launch flow, presence, account export/import, plugins, games, themes.
- No new Windows permissions, no new dependencies, no telemetry.

## Other channels

- **Microsoft Store *(recommended)*** — [Install RoRoRo on the Microsoft Store](https://apps.microsoft.com/detail/9NMJCS390KWB). Signed by Microsoft, bypasses SmartScreen, auto-updates through the Store.
- **MSIX sideload** — `RORORO-Sideload.msix` + `dev-cert.cer` are attached if you'd rather install a package than run `Setup.exe`. Download both, right-click `dev-cert.cer` → Install → Local Machine → Trusted People, then open the `.msix`. Not sure? Just use `Setup.exe` above.

## Issues, ideas

- **Bug or suggestion:** [open an issue](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/new)
- **Privacy details:** [privacy policy](https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/)

A 626 Labs product.
