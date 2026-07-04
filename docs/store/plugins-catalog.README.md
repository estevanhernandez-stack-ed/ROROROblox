# Plugin marketplace catalog — source + upload

[`plugins-catalog.json`](plugins-catalog.json) is the source of truth for the RoRoRo plugin marketplace's **Available** section and update badges. The app fetches it at:

```
https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/plugins-catalog.json
```

— so it is served as a **release asset named `plugins-catalog.json` on the main repo's latest GitHub Release**, the same pattern as `roblox-compat.json`. Editing it is re-uploading that asset to the latest release; it does **not** require shipping a new app version.

The marketplace is **unpackaged-only** (direct-download / dev builds), so this catalog only ever reaches direct-download users — never the Store binary.

## Upload

1. Edit `plugins-catalog.json` here, commit it (version-controlled source).
2. Upload the same file as an asset named `plugins-catalog.json` to the **latest** GitHub Release of the main `ROROROblox` repo (Partner Center / Store is unaffected — this is a direct-build feature).
3. The next time a direct-download user opens the Plugins window, the Available section fills in.

Until an asset exists, the app fetches nothing (404 → empty catalog → the Available section is header-only). The marketplace ships **dark** and is harmless until the catalog is uploaded.

## Entry shape

Each array entry: `id`, `name`, `description`, `publisher`, `latestVersion`, `installUrl` are required; `iconUrl`, `minHostVersion` are optional. The catalog carries **metadata + an install URL only** — never code or hashes. Install stays SHA-verified through `PluginInstaller` against each release's own `manifest.sha256`.

- **`id`** must equal the plugin's `manifest.id` (reverse-DNS) — that's the key the update matcher uses.
- **`installUrl`** is the base URL the installer appends `manifest.json` / `manifest.sha256` / `plugin.zip` to. The `/releases/latest/download/` form always points at that plugin's newest release, so an install always gets the latest build. **Each plugin's latest release must publish those three assets.**
- **`latestVersion`** drives the update badge: an installed plugin whose version parses older than this gets "Update available." **It must match the `version` in the manifest served at `installUrl`** (i.e. the plugin's latest release), or the badge lies. Bump it here whenever a plugin cuts a new release.

## Authoring discipline (verify before each upload)

- **`latestVersion` == the plugin's latest-release `manifest.version`.** A stale value here fires a spurious badge (too high) or hides a real update (too low).
- **⚠️ Ur AFK version check.** This catalog lists `626labs.ur-afk` at `0.1.1` (its working-tree manifest), but the repo's latest tag at authoring time was `v0.1.0`. Before uploading: confirm what `rororo-ur-afk`'s **latest release** actually serves. If it's `v0.1.0`, either cut/tag `v0.1.1` or set `latestVersion` here to `0.1.0` so the badge matches reality.
- Version compare normalizes component counts (`1.0.0` == `1.0.0.0`) and tolerates a pre-release tag (`1.4.3-beta` → `1.4.3`), so 3-part vs 4-part strings won't fire a false badge.
- `minHostVersion` gates the Install button ("Needs RoRoRo X+") for users on an older host. Keep it in sync with the plugin's own `manifest.minHostVersion`.
