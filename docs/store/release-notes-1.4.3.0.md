# Release notes — v1.4.3.0

> Paste the block between the `---` markers below into the GitHub Release body.
> Tone: clan-facing (Pet Sim 99 audience). Plain, warm, no Microsoft-reviewer formality.
> Minor bump centered on the plugin experience — plugins install and run without restarting RoRoRo, the row gets a per-plugin Launch button, and the manifest learns three new fields plugin authors can use to make their installs cleaner.

---

## Download

[**rororo-win-Setup.exe**](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/rororo-win-Setup.exe) — single click, installs to your user profile, lands in the Start Menu.

If you've already got RoRoRo installed via the installer or the Microsoft Store, this update rolls out automatically — no second prompt.

## What changed

### Plugins install and run without a RoRoRo restart

If you installed a plugin on v1.4.2.0 with Autostart left off, the plugin EXE never started — even after a manual RoRoRo restart, because Autostart was off. You had to toggle Autostart on **and** restart RoRoRo. That gap is closed.

Three things compound the fix:

- **Install-and-run**: the moment you grant consent on the install dialog, the plugin's EXE spawns right then. No restart needed. Autostart still governs whether it ALSO launches on future RoRoRo starts.
- **Per-plugin Launch button**: every row in the Plugins window now has a Launch button right next to its Autostart checkbox. Click it to spawn the plugin on demand without touching Autostart. Greys out while the plugin is running, comes back when it exits.
- **Cleaner re-installs**: if a plugin process is still running (or an orphaned one from a previous RoRoRo session is lurking), re-installing now kills it cleanly before wiping the install directory. Previously you could hit "access denied" because Windows was holding a DLL open.

### Manifest learns three new fields for plugin authors

If you write plugins, three optional fields in `manifest.json` let you control install behavior more precisely:

- **`autostartDefault: "on"`** — declare that your plugin should autostart on a fresh install. Users can still flip it off later.
- **`minHostVersion: "1.4.3"`** — declare the minimum RoRoRo version your plugin needs. If a user on an older RoRoRo tries to install, they'll get a clear "Update RoRoRo and try again" message instead of a downstream symptom.
- **`entrypoint: "your-launcher.exe"`** — declare your EXE filename if it's not just `<plugin-id>.exe`. Validated at install time, so you'll know immediately if your release archive ships the wrong file.

All three are optional. Manifests without them keep working unchanged.

### `system.read-screen` capability

Plugins that need to read pixels from your screen (OCR, screen-reader-style flows, color sampling, etc.) can now declare `system.read-screen` in their manifest's `capabilities` list. Disclosure-only — RoRoRo can't sandbox a plugin's process, so the capability tells you what the plugin will do, and you decide whether to grant it on the consent sheet.

### Plugin authors: `ROROROblox.PluginContract` on nuget.org

The shared NuGet package plugin authors build against is now publishing release metadata properly — license, repository, README — so it shows up cleanly on nuget.org instead of as a bare package.

### Smaller polish

- **`RORORO` → `RoRoRo`** across in-app surfaces. The brand is RoRoRo (the dev nickname `RORORO` is just the repo name).
- **Cookie capture** shows a spinner overlay while WebView2 is initializing for the first time. Previously the window felt frozen for a beat.
- **DPAPI-corrupt modal** now leads with "Quit" as the safer default. "Start Fresh" wipes saved accounts, so it's no longer the primary button.
- **Roblox-not-installed modal** label is verb-first: "Open Bloxstrap setup" instead of "Roblox not installed."

## Compatibility

- No changes to **saved accounts**, **mutex handling**, or the **auth-ticket flow**. Saved accounts, favorites, private servers, default-game widget, renames, themes — all unchanged from v1.4.2.
- **Plugin contract** unchanged at the wire level. `ROROROblox.PluginContract` v0.1.0 stays correct; manifest schema picks up three optional fields but `schemaVersion` stays at `1` (back-compat both ways). Plugins built against v1.4.0.0's contract keep working.
- **One new capability vocabulary entry** — `system.read-screen`. No plugin can use it without your explicit consent on the install sheet. No code in RoRoRo grants new Windows permissions; this is a disclosure scope for plugin authors who already have the user's screen accessible via their own EXE.
- No new Windows permissions. No new outbound network calls. No new dependencies.

## Other channels

- **Microsoft Store *(recommended)*** — [Install RoRoRo on the Microsoft Store](https://apps.microsoft.com/detail/9NMJCS390KWB). Signed by Microsoft, bypasses SmartScreen, auto-updates through the Store.
- **MSIX sideload** — `RORORO-Sideload.msix` + `dev-cert.cer` are also attached if you'd rather install a package than run `Setup.exe`. Download both, right-click `dev-cert.cer` → Install → Local Machine → Trusted People, then open the `.msix`. Not sure? Just use `Setup.exe` above; it needs none of this.

## Issues, ideas

- **Bug or suggestion:** [open an issue](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/new)
- **Privacy details:** [privacy policy](https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/)
- **Plugin authors:** [author guide](https://github.com/estevanhernandez-stack-ed/ROROROblox/blob/main/docs/plugins/AUTHOR_GUIDE.md)

A 626 Labs product.
