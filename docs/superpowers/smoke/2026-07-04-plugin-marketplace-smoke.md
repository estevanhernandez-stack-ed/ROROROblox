# Smoke checklist — plugin marketplace

**Branch:** `feat/plugin-marketplace` · **Spec:** [`../specs/2026-07-04-plugin-marketplace-design.md`](../specs/2026-07-04-plugin-marketplace-design.md)

The gate (`IsPackaged`), catalog client, and update-decision are unit-tested; the WPF window is manual-smoke by house convention. The **compliance check (packaged hides everything)** is the one that must not be skipped — it's what keeps the Store build inside policy 10.2.2.

## Two phases

The Available section only fully lights up once a real `plugins-catalog.json` is uploaded to the repo's **latest GitHub Release**. Until then, unpackaged shows the section header above an empty list (by design). So:

- **Phase A (now, no catalog):** verify the gate + that nothing breaks with an empty/absent catalog.
- **Phase B (after you upload `plugins-catalog.json`):** verify browse / install / update end to end. (Ask me to draft the initial catalog for the Ur family when you're ready.)

## Setup

- [ ] Quit the installed RoRoRo from the tray first (single-instance guard).

## Phase A — the compliance gate (do this first, it's the load-bearing one)

**Unpackaged (marketplace present):** run the unpackaged dev build — `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" run --project src/ROROROblox.App` (a `dotnet run` is unpackaged). Open the Plugins window (tray → Plugins).
- [ ] The window opens; the installed-plugins list and the "Install from URL" box work exactly as before.
- [ ] An **"Available"** section header is present below the installed list (the list under it is empty until a catalog ships — expected).
- [ ] No error, no hang: the installed list appears promptly even with no network / no catalog (the fetch is bounded to 8s and fails safe to empty).

**Packaged (marketplace absent — THE compliance check):** build + install the **sideload MSIX** (`scripts/build-msix.ps1 -Sideload -CertPath dev-cert.pfx -CertPassword <pwd>`), launch it, open Plugins.
- [ ] **No "Available" section at all.** No update badges. The window is byte-for-byte the pre-marketplace behavior: installed list + paste-URL only.
- [ ] Paste-URL install still works (install a plugin by URL as before).

> If the packaged build shows ANY marketplace UI (an Available section, an update badge), stop — that's the 10.2.2 gate leaking and must not ship to the Store. It should be impossible (the gate is a runtime `IsPackaged()` check), but this is the check that proves it.

## Phase B — after uploading `plugins-catalog.json` to the latest release (unpackaged only)

- [ ] **Browse:** the Available section lists catalog plugins you don't have — name, publisher, version, description, an **Install** button.
- [ ] **Install from catalog:** click Install → the consent sheet appears (same as paste-URL install) → the plugin installs (SHA-verified) and moves to the Installed list.
- [ ] **Update badge:** with an installed plugin whose catalog `latestVersion` is newer than installed, its row shows a magenta **Update available (x → y)** badge + an **Update** button.
- [ ] **Update:** click Update → it re-installs the new version. A plugin that was **running** relaunches on the new version; a plugin that was **not running** stays stopped (no surprise force-launch).
- [ ] **minHostVersion gate:** a catalog entry needing a newer RoRoRo shows **"Needs RoRoRo X+"**, Install disabled.
- [ ] **No double-run:** the Update / Install buttons disable while an install is in flight (can't kick off two at once).
- [ ] **Offline / bad catalog URL:** disconnect → the Available section is empty, no error, installed list + paste-URL still work.

## Known / by-design (not bugs)

- The "Available" header shows above an empty list until a catalog ships (pre-authorized cosmetic).
- Updating a plugin does **not** re-prompt the consent sheet — a new capability in an updated manifest carries the originally-granted set and is denied by the capability interceptor until re-granted (design §8; catalog is 626-authored so 626 controls what an update adds). Deferred follow-up.

## Result

- [ ] Phase A passes (esp. the packaged-hides-everything check) → the branch is safe to merge; the marketplace ships dark (no catalog) until you upload one.
- [ ] Phase B passes after the catalog upload → the marketplace is live for the clan.
- [ ] Anything off → note the check + what you saw; it comes back for a fix before merge.
