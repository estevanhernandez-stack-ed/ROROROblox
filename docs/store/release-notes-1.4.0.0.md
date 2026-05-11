# Release notes — v1.4.0.0

> Paste the block between the `---` markers below into the GitHub Release body.
> Tone: clan-facing (Pet Sim 99 audience). Plain, warm, no Microsoft-reviewer formality.
> v1.4 is the biggest cycle to date — the plugin system. Lead with what it means for the
> clan: extras you can opt into, written by anyone, contained safely.

---

## Download

[**rororo-win-Setup.exe**](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/rororo-win-Setup.exe) — single click, installs to your user profile, lands in the Start Menu.

If you've already got RoRoRo installed via the installer or the Microsoft Store, this update rolls out automatically — no second prompt.

## What's new

### Plugins are here

RoRoRo now supports plugins — third-party add-ons that extend what RoRoRo can do without touching the core. The first one is **RoRoRo Ur Task** (record and play back keyboard + mouse macros, one per alt), built by 626 Labs and shipping alongside this release.

How it works in plain English:

- Each plugin runs in its **own process**, separate from RoRoRo. If a plugin crashes, RoRoRo keeps going.
- Each plugin can **only see what you grant it.** When you install one, you get a consent sheet listing every capability it wants — events from RoRoRo, the ability to send keystrokes, the ability to start launches on your behalf — and you can toggle each one before it gets access.
- Plugins are **distributed separately.** RoRoRo never bundles or auto-fetches plugin code. You paste a GitHub release URL into the Plugins page, RoRoRo verifies the SHA, shows you the consent sheet, and only then unpacks.
- The plugin system uses a **per-user named pipe** for communication — only your Windows account can talk to RoRoRo's plugin host.

Open **Plugins** from the main window nav (new in this release) or the tray menu to get started.

### Plugins entry on the main window

Previously the Plugins page was tucked away in the tray menu. Now there's a **Plugins** button in the main window's nav bar next to Settings / About / History / Diagnostics / Games. One click.

### Install flow polish

- The Install button on the Plugins page now dims on hover instead of darkening — the old hover state read as "disabled."
- After a successful install, you get a **Restart RoRoRo** prompt — plugins start when RoRoRo launches, so a fresh install doesn't run until you restart.
- The consent sheet now tells you up front that the plugin starts on next launch, so the restart prompt isn't a surprise.

### Writing plugins

If you want to build one yourself, the [plugin author guide](https://github.com/estevanhernandez-stack-ed/ROROROblox/blob/main/docs/plugins/AUTHOR_GUIDE.md) covers the contract, capability vocabulary, manifest format, and how to ship a GitHub release that RoRoRo can install. The contract is shipped as a NuGet package (`ROROROblox.PluginContract`) so authors don't have to copy `.proto` files around.

## Compatibility

- No changes to **launcher behavior**, **saved accounts**, **mutex handling**, or **auth-ticket flow.** Multi-instance launches behave identically to v1.3.4.
- No new Windows permissions in the manifest. No new always-on network calls. Plugins only run if you install them.
- No new bundled code: plugins are user-initiated installs from GitHub URLs, never auto-fetched.
- Saved accounts, favorites, private servers, default-game widget, renames, themes — all unchanged from v1.3.4.

## Other channels

- **Microsoft Store *(recommended)*** — [Install RoRoRo on the Microsoft Store](https://apps.microsoft.com/detail/9NMJCS390KWB). Signed by Microsoft, bypasses SmartScreen, auto-updates through the Store.
- **MSIX sideload** — `RoRoRo-Sideload.msix` + `dev-cert.cer` are also attached if you prefer the cert-import flow over `Setup.exe`. Same dev-cert as v1.3.x — if you've already imported it once, no re-import needed.

## Issues, ideas

- **Bug or suggestion:** [open an issue](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/new)
- **Privacy details:** [privacy policy](https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/)
- **Plugin authors:** [author guide](https://github.com/estevanhernandez-stack-ed/ROROROblox/blob/main/docs/plugins/AUTHOR_GUIDE.md)

A 626 Labs product.
