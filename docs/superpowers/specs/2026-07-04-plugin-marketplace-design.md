# Plugin marketplace — design

**Date:** 2026-07-04
**Status:** Approved design, pre-plan.
**Origin:** Backlog idea (a plugin marketplace with update-available indicators), raised alongside "session stats." Scope-decomposed during brainstorming: this spec is the **marketplace** only; aggregate session stats is a separate deferred spec.

---

## 1. What this is

A **plugin marketplace** inside the existing Plugins window: a remote **catalog** is the single source of truth for both *what you can install* and *whether an installed plugin has an update*. The Plugins window gains two sections — **Installed** (each row badges an available update + a one-click Update) and **Available** (catalog plugins you don't have, each with Install). The existing paste-a-GitHub-URL install stays as the off-catalog escape hatch.

**Active only in unpackaged builds** (direct-download Velopack + dev runs). In a packaged MSIX build (Microsoft Store or the self-signed sideload flavor) the marketplace does not exist — the Plugins window is byte-for-byte today's behavior. See §3, the compliance gate — this is the load-bearing decision of the whole design.

### Decomposition (settled during brainstorming)

- **This spec:** update detection (A1) + browsable catalog (A2), unified.
- **Deferred to its own spec:** aggregate session/historical stats (B) — an independent subsystem over the existing `SessionHistoryStore`; shares no code with the marketplace.

---

## 2. Why gated to unpackaged builds — the compliance backbone

RoRoRo ships two ways: the **Store MSIX** and the **direct Velopack download** (Setup.exe) the clan actually uses. The plugin system was certified under Microsoft Store policy **10.2.2**, and the v1.4 reviewer letter ([`docs/store/reviewer-letter-1.4.0.0.md`](../../store/reviewer-letter-1.4.0.0.md)) makes three explicit written commitments:

> "Plugin install is user-initiated. RoRoRo **never auto-fetches plugins, never polls for new plugins, never reads a curated list from a server**. The user pastes a GitHub release URL into the Plugins window and clicks Install."

A marketplace fetches a curated list from a server, polls for updates, and offers browse-and-install — it does all three things the certified letter promised never to do. So the marketplace **cannot ship in the Store build** without reversing certified representations (a re-certification gamble whose outcome is Microsoft's call, not ours to predict).

Resolution: the marketplace is a **direct-download-build-only** feature. Its whole audience is plugin users, who are the direct-download clan — so gating it off in the Store MSIX costs ~nothing in reach while keeping the Store binary's 10.2.2 posture true word-for-word.

**The gate is runtime, not a build flag** (see §3.1) — chosen specifically so it cannot silently regress across future releases. A build-time `#define` or packaging-script flag is a compliance-critical thing you can forget; a runtime packaged-check gates itself because the Store MSIX *is* packaged by definition.

---

## 3. Architecture

Four units, each understandable and testable in isolation.

### 3.1 `DistributionMode` — the gate (single source of truth)

A small helper exposing `IsPackaged()` — true when running as an MSIX-packaged app (Store or sideload), false when unpackaged (Velopack direct download, `dotnet run`, F5). Implemented via CsWin32 `GetCurrentPackageFullName` (returns the `APPMODEL_ERROR_NO_PACKAGE` code when unpackaged; the plan pins the exact constant/handling) — the same Win32-source-generator path RoRoRo already uses.

- **Injectable seam:** the packaged check is behind a `Func<bool>` (or a one-method interface) so the marketplace VM logic is unit-testable in both modes without a real MSIX identity.
- **The marketplace is "on" iff `!IsPackaged()`.** Every marketplace entry point — the catalog fetch, the Available section, the update badges — reads this one source of truth. Nothing scattered.
- **Tamper-evidence:** disabling the gate is a visible code change a diff/review catches, not a forgotten flag. That's the property that makes "keep it gated across releases" free.

Note: dev runs (`dotnet run` / F5) are unpackaged, so developers see the marketplace by default; only the MSIX flavors hide it. Testing the marketplace = run the unpackaged build (which is also where its users are).

### 3.2 `PluginCatalogClient` — fetch + parse the catalog

Fetches `catalog.json` over HTTPS at Plugins-window open (only when `!IsPackaged()`), parses it into a list of catalog entries. Fetch failure, non-200, or malformed JSON → **empty catalog** (never throws to the UI). Modeled on the existing remote-config fetch (`RobloxCompatChecker` fetching `roblox-compat.json`).

Catalog entry shape (`catalog.json` is an array of these):

| Field | Meaning |
|---|---|
| `id` | reverse-DNS plugin id, matches the plugin's `manifest.id` |
| `name` | display name |
| `description` | one-line summary |
| `publisher` | e.g. "626 Labs" |
| `iconUrl` | HTTPS icon URL (best-effort render) |
| `latestVersion` | the current published version — the update-detection comparand |
| `installUrl` | the GitHub-release base URL the existing `PluginInstaller` already consumes (manifest.json + manifest.sha256 + plugin.zip live there) |
| `minHostVersion` | minimum RoRoRo version required (nullable) |

The catalog carries **metadata + install URL only**. The actual install stays SHA-verified through `PluginInstaller` against the release's own `manifest.sha256` — the catalog never carries code or hashes.

### 3.3 Update-decision logic (pure)

Given the installed plugins (each with its `Version` from its installed manifest) and the catalog (each entry with `latestVersion`), produce:

- per **installed** plugin: `UpToDate` or `UpdateAvailable(from, to)` — matched by `id`; "update available" when the catalog's `latestVersion` parses as a strictly-newer `System.Version` than the installed `Version` (reuse the numeric-head parse that `MinHostVersion` already tolerates for pre-release tags).
- the **available** list: catalog entries whose `id` is not installed.
- per available entry: an `installable` flag — false when its `minHostVersion` exceeds the running host (reuse the installer's existing minHostVersion comparison).

Pure function of (installed list, catalog, host version). Unit-tested — this is the marketplace's brain, and the one place version math lives.

### 3.4 `PluginsViewModel` + `PluginsWindow` (extend)

- **VM:** on load, if `!IsPackaged()`, fetch the catalog and derive the Installed-with-update-state + Available lists via §3.3. If packaged, skip all of it — the VM exposes only today's installed list + paste-URL state.
- **Window:** two sections in the existing single window — **Installed** (each row unchanged except a new update badge + Update button when an update is available) and **Available** (catalog entries not installed: icon, name, publisher, description, Install button, or "needs RoRoRo X+" disabled when not installable). Both sections are absent entirely when packaged. The paste-URL box stays.
- **Install** (Available) and **Update** (Installed) both route through the **existing SHA-verified `PluginInstaller`** and the existing `PluginProcessSupervisor` stop/install/restart mechanics. **No new install path** — the marketplace is a discovery + trigger surface over the install flow that already shipped and already passed certification.

---

## 4. Data flow

Open Plugins window → if packaged, render today's view and stop. Else → `PluginCatalogClient` fetches `catalog.json` → update-decision logic joins it against the installed list + host version → window renders Installed (with badges) + Available. User clicks **Update** on an installed row → supervisor stops the plugin → `PluginInstaller.InstallAsync(catalogEntry.installUrl)` (SHA-verified) → supervisor restarts. User clicks **Install** on an available entry → same installer path → consent sheet (existing) → running.

---

## 5. Edge cases

| Case | Behavior |
|---|---|
| **Packaged build (Store / sideload MSIX)** | No catalog fetch, no Available section, no update badges. Plugins window = today's behavior exactly. |
| **Catalog fetch fails / offline / malformed** | Empty catalog → no Available section, a muted "couldn't reach the plugin catalog" line; Installed list + paste-URL work as today. Never blocks. |
| **`minHostVersion` too high** | Available entry shows "needs RoRoRo X+", Install disabled (reuse installer's existing refusal). |
| **Catalog `latestVersion` vs installed drift** | Badge trusts the catalog's `latestVersion`; 626 keeps it in sync with the release's `manifest.version` (authoring discipline — noted here and in the catalog's own README). |
| **Off-catalog installed plugin** (dev-dropped / paste-URL from outside the catalog) | Shows in Installed with no update badge (no catalog match). |
| **Update install fails** | Same failure surface as a fresh install (existing installer error handling); the plugin stays at its old version. |

---

## 6. The catalog: hosting + trust

**Hosting (recommended):** `catalog.json` as a **release asset**, updated on the latest GitHub Release — the same mechanism and mental model as `roblox-compat.json`. Updating the catalog (add a plugin, bump a `latestVersion`) is re-uploading one asset to the latest release; it does **not** require shipping a new app version. The frictionless alternative is a raw repo file updated by commit — noted for the record; the release-asset pattern is chosen for consistency with the existing remote-config fetch.

**Trust:** the catalog is 626-controlled and fetched over HTTPS. A compromised catalog could point `installUrl` at a malicious-but-self-consistent release — SHA-verification protects the wire, not a bad source. Since the catalog lives at the same trust level as the app (626-owned), this is acceptable and identical to the `roblox-compat.json` posture. Named here so it's a conscious call, not an oversight.

---

## 7. Testing

- **`DistributionMode`** — mock the Win32 seam; assert the marketplace surfaces are present when unpackaged and absent when packaged. This is the compliance-critical test.
- **`PluginCatalogClient`** — parse canned `catalog.json` (well-formed, malformed, empty, non-200 → empty catalog).
- **Update-decision logic** — up-to-date, update-available (version compare incl. pre-release tags), not-installed filtering, minHostVersion-gated installable flag, off-catalog installed plugin (no match).
- **Window** — manual smoke per house convention (WPF). Smoke both modes: unpackaged (marketplace visible, browse/update work) and the packaged sideload MSIX (marketplace absent, paste-URL only).
- **No end-to-end against live GitHub** — the catalog client is tested against canned JSON; the install path is the already-tested `PluginInstaller`.

---

## 8. Out of scope (YAGNI)

- Aggregate session/historical stats (B) — separate deferred spec.
- User-added / third-party catalog sources — the catalog is the single baked 626 URL; the paste-URL box covers off-catalog installs.
- Wiring the per-plugin `UpdateFeed` manifest field — the unified catalog drives updates; `UpdateFeed` stays a documented future hook for off-catalog update detection.
- Store re-certification to allow the marketplace in the Store build — explicitly rejected in favor of the unpackaged-only gate.
- Any change to the install/consent/SHA-verify path — the marketplace is a surface over the existing installer, not a new one.

---

## 9. Decision log (to mirror to the dashboard on build)

- **Decompose:** marketplace and session-stats are independent subsystems; this spec is the marketplace only.
- **Unified catalog** drives both browse and update detection (one remote source of truth); per-plugin `UpdateFeed` deferred.
- **Two sections, one window** (Installed / Available) — least clicks, reuses the existing single-window shape.
- **Gate = runtime `IsPackaged()`, not a build flag** — self-enforcing across releases, tamper-evident, keeps the Store build's certified 10.2.2 promises true automatically.
- **Marketplace is unpackaged-only** — reversing the 10.2.2 written commitments in the Store build is a re-certification gamble; gating costs ~nothing in reach.
- **Catalog hosted as a release asset** (roblox-compat.json parity), metadata + install URL only, install stays SHA-verified.
