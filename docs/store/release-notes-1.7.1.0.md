# Release notes — v1.7.1.0

> Paste the block between the `---` markers below into the GitHub Release body.
> Tone: clan-facing (Pet Sim 99 audience). Plain, warm, no Microsoft-reviewer formality.
> Patch release: reliability + privacy fixes from a full-app audit. No new features.
> NOTE: do NOT claim Setup.exe installs auto-update — the in-app updater is check-only
> until v1.8 wires download+apply. Store installs update via the Store.

---

## Download

[**rororo-win-Setup.exe**](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/rororo-win-Setup.exe) — single click, installs to your user profile, lands in the Start Menu. Reinstalling over an old version keeps your saved accounts.

On the Microsoft Store version? It updates by itself through the Store. Installed with `Setup.exe`? Grab the new one above — takes a few seconds.

## What changed

This one's a tune-up, not a feature drop. We ran a deep audit on the whole app and fixed the sharpest things it found.

### Account rows stay live for the whole session

The status rows ("In Pet Simulator 99", "At Roblox home") are driven by a background check that runs every 25 seconds. Before this fix, one bad moment — removing an account at exactly the wrong time, or your antivirus briefly locking a file — could quietly stop that check for the rest of the session. Rows would freeze at whatever they last showed, and a closed client could read as still running. Now a bad moment costs one cycle, the next one runs, and the rows keep telling the truth.

### Fast-closing Roblox clients can't get stuck as "running"

When Roblox closes a client almost the instant it opens (its own updater does this routinely), RoRoRo could miss the exit and leave that account stuck as "running" — which also kept it out of batch launches until you restarted the app. Fixed at the root: the exit can't be missed anymore.

### Your login session is cleaned up the moment you finish adding an account

Adding an account opens a small Roblox login window. Before this fix, the data from your most recent login stuck around on your PC until the next time you added someone. Now it's wiped the instant that window closes — and removing an account removes everything that belongs to it. Nothing leaves your PC either way; this is about not leaving things lying around on it.

### Smaller fixes

- Plugin installs now require a secure (https) address — plain http is refused before anything downloads.
- The Launch multiple tooltip now tells the truth: launches go out about 5 seconds apart so each window lands on the right account. (It said 1.5 seconds before — the app wasn't hung, the label was just old.)
- The first-run welcome now names the actual button ("Launch multiple" — there is no "Launch All").
- Errors that used to disappear without a trace now land in the diagnostics log, so when something goes wrong we can actually find it.

## Compatibility

- Nothing new leaves your PC. No new addresses, no new Windows permissions, no telemetry — this release only fixes things.
- Saved accounts, the launch flow, presence, export/import, plugins, games, themes: all unchanged.

## Other channels

- **Microsoft Store *(recommended)*** — [Install RoRoRo on the Microsoft Store](https://apps.microsoft.com/detail/9NMJCS390KWB). Signed by Microsoft, bypasses SmartScreen, auto-updates through the Store.
- **MSIX sideload** — `RORORO-Sideload.msix` + `dev-cert.cer` are attached if you'd rather install a package than run `Setup.exe`. Download both, right-click `dev-cert.cer` → Install → Local Machine → Trusted People, then open the `.msix`. Not sure? Just use `Setup.exe` above.

## Issues, ideas

- **Bug or suggestion:** [open an issue](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/new)
- **Privacy details:** [privacy policy](https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/)

A 626 Labs product.
