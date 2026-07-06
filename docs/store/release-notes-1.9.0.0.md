# Release notes — v1.9.0.0 (testing pre-release)

> Paste the block between the `---` markers below into the GitHub Release body.
> **This is a TESTING PRE-RELEASE — Velopack/direct-download only.** Publish with
> `--prerelease`, NOT `--latest`: v1.8.0.0 stays "Latest" so existing installs'
> update check, compat fetch, and catalog fetch keep pointing at stable. Testers
> get the direct link below. The Store submission follows after testing proves it.
> Also bundles the v1.8.1.0 patch-lane fixes that were tagged but never published.
> NOTE: updater is still check-only — download+apply moved with this bump (now
> targeting the next version).

---

## This is a test build

You're looking at an early build for testers — it won't be offered to existing installs automatically, and the Microsoft Store version comes later once this round proves out. If you just want stable, grab [v1.8.0.0](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/tag/v1.8.0.0) instead.

## Download

[**rororo-win-Setup.exe**](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/download/v1.9.0.0/rororo-win-Setup.exe) — single click, installs to your user profile. Installing over v1.8 keeps your saved accounts.

## What's new to test

### The plugin marketplace

The Plugins window now has an **Available** section: browse the Ur family (Ur Task, Ur OCR, Ur AFK), install with one click, and see an "Update available" badge when a plugin you have is behind. No more pasting GitHub URLs. Every install still goes through the same integrity check (SHA-verified against the plugin's published hash) and the same consent sheet as before.

*Direct-download builds only* — this section doesn't exist in the Store version.

**What to poke at:** browse, install something fresh, update something old, try to break the buttons mid-install.

### Launch alts from your main's friends list

The friends picker can now browse **your main account's friends** and launch the alt you have open straight to them. Before, the picker only knew about accounts saved in RoRoRo.

**What to poke at:** switch between sources in the picker, follow a friend who's in a game, follow one who's offline, and watch which account actually launches.

### The "reauth tag that wouldn't go away" is actually fixed

If you saw an account stuck showing it needed re-login even after you re-logged it — that's gone. The first fix (in the unreleased 1.8.1) treated a symptom; this build fixes the actual race where the background status check re-flagged the account right after you cleared it. Re-login also now tells you how it went, every time, including when Roblox asks for 2FA mid-flow.

### Smaller fixes riding along (from the unpublished 1.8.1 patch)

- "Close Roblox for me" now waits for Roblox to actually exit before retrying the lock — no more racing it.
- Plugins get a consent sheet on first launch if you installed them without one.
- Plugin start/stop is logged properly and shutdown cleans up after itself.
- Log noise cut down so the diagnostics log is actually readable.

## Compatibility

- One new outbound fetch: the plugin catalog, from this repo's GitHub Releases — the same place the app already checks its Roblox-compatibility config. Nothing else new leaves your PC; the catalog carries plugin names and download links only.
- Saved accounts, launch flow, presence, themes, existing plugins: all unchanged.

## Found something?

That's the point of this build — [open an issue](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/new) or ping the Discord. Logs live at `%LOCALAPPDATA%\ROROROblox\logs\` (see the how-to-grab-your-logs doc in the repo).

A 626 Labs product.
