# RoRoRo v1.8.0.0 — Release Runbook

> **⚠️ The recurring release loop is [`release-playbook.md`](release-playbook.md) — read it FIRST.** This runbook was written before consulting it (a process miss, caught mid-release 2026-07-02) and duplicates phases the playbook already owns, with two errors the playbook would have prevented: the spaced `PublisherDisplayName` (Partner Center rejected the package — the playbook Phase 3 identity constants are verbatim, `626Labs LLC` no space) and the skipped Phase 4 sideload build. This file stays as the v1.8-specific record (scope decision, compat values, links); for future releases the playbook governs, and a per-version runbook should hold only what's version-specific.
>
> **Status:** DRAFT — for review before execution. Written 2026-07-02, after PR #32 (tray-residence gate) merged to `main` (`d4ea9da`).
> **What v1.8.0.0 bundles:** three merged branches since v1.7.1.0 — **#30 Limited-session handling**, **#31 activity awareness** (the consent-gated `GetAccountActivity` plugin query), **#32 tray-residence gate + runtime lock awareness**.
> **Current state:** app `1.7.1.0` (latest tag `v1.7.1.0`), `PluginContract` at `0.3.0` (unpublished), `roblox-compat.json` known-good max `0.722.0.7221024`.

---

## ⚠️ Phase 0 — Scope decision (BLOCKING, decide before anything else)

**The v1.7.1.0 release notes made a promise:** *"the in-app updater is check-only until v1.8 wires download+apply."* The three merged branches do **not** touch the updater — `UpdateChecker.CheckForUpdatesAsync()` still only logs "Update available" with no download/apply UI ([Updates/UpdateChecker.cs](../../src/ROROROblox.App/Updates/UpdateChecker.cs)). So before cutting 1.8.0.0:

- **Option A — Ship the promise.** Build the Velopack download+apply wiring (tray "Update available → download → apply/restart") as one more branch first, then cut 1.8.0.0 with it included. Larger scope; honors the documented commitment; the reviewer letter/release notes then legitimately say "auto-update now works."
- **Option B — Slip it, cut 1.8.0.0 as-is.** Release the three merged branches now; the updater stays check-only; the release notes keep the "check-only" caveat and the download+apply promise moves to v1.9. Smaller, faster; the three branches are all real user value on their own.

**Recommendation:** **Option B, but rename expectations honestly** — cut 1.8.0.0 now on the three branches (each is shippable, smoke-verified value), and re-scope the updater download+apply to a clean v1.9 branch so it gets its own spec/plan/review rather than being rushed into a bundle cut. The only cost is the caveat line stays one more release. Confirm A or B before Phase 1.

> **DECIDED 2026-07-02: Option B.** v1.8.0.0 ships the three merged branches; the updater stays check-only; download+apply is scoped to v1.9. Release notes and reviewer letter carry the caveat.
>
> **Compat correction found during Phase 1:** the installed client on the smoke box is **0.728.0.7280895** — Roblox moved past 0.727 before the cut, and the spec §8 smoke actually ran against 0.728. `knownGoodVersionMax` is set to `0.728.0.7280895` (not a 0.727 value).

**Second decision — Store submission timing:** submit to Partner Center **with** this release, or ship the GitHub/Velopack release to the clan first and Store-submit after a soak? (The clan gets it via Velopack/Setup.exe regardless; Store is the broader-reach channel.)

---

## Dependency graph (what blocks what)

```
Phase 1 (version bump) ─┬─> Phase 4 (tag → Velopack CI release)
                        └─> Phase 3 (MSIX builds) ──> Phase 5 (Partner Center)
Phase 2 (PluginContract NuGet) — independent, can run any time
```

- Version bump gates the Velopack build (About-box/assembly version) and the MSIX build.
- The NuGet publish is independent of the app release (do it whenever) — but ur-AFK also needs the **1.8 host at runtime**, so its actual use still waits on the app release.
- Partner Center needs the Store-signed MSIX, which needs the version bump.

---

## Phase 1 — In-repo prep (safe, reversible, one PR)

All reversible; lands on a `release/v1.8.0.0` branch and PR's for review. Nothing here is outward-facing.

### 1.1 Version bump 1.7.1.0 → 1.8.0.0

Two load-bearing files (the ~30 other files matching `1.7.1.0` are historical docs — **do not** mass-edit):

- [src/ROROROblox.App/ROROROblox.App.csproj](../../src/ROROROblox.App/ROROROblox.App.csproj) — `<Version>1.7.1.0</Version>` → `<Version>1.8.0.0</Version>`
- [src/ROROROblox.App/Package.appxmanifest](../../src/ROROROblox.App/Package.appxmanifest) — `<Identity ... Version="1.7.1.0" ... />` → `Version="1.8.0.0"`

`scripts/finalize-store-build.ps1` re-asserts both from its `-Version` arg at Store-build time (and enforces the `.0` 4th-segment rule), so the hand-edit is just to keep the in-repo source honest for the Velopack build. Preview with:

```powershell
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build ROROROblox.slnx  # confirm 1.8.0.0 in About box path
```

### 1.2 roblox-compat.json — add 0.727 to the known-good range

[roblox-compat.json](../../roblox-compat.json) currently:

```json
{ "knownGoodVersionMin": "0.700.0.0", "knownGoodVersionMax": "0.722.0.7221024",
  "mutexName": "Local\\ROBLOX_singletonEvent", "generatedAt": "2026-05-28T00:00:00Z" }
```

- Bump `knownGoodVersionMax` to cover the installed 0.727 build. Get the exact build string from the client on this box:
  ```powershell
  (Get-Item "$env:LOCALAPPDATA\Roblox\Versions\version-*\RobloxPlayerBeta.exe").VersionInfo.FileVersion
  ```
  (Format matches the existing `0.722.0.7221024` shape — e.g. `0.727.0.7270xxx`.)
- Update `generatedAt` to the release date.
- **Leave `mutexName` untouched** — `Local\ROBLOX_singletonEvent` is unchanged in 0.727 (verified by the smoke: multi-instance works, so the mutex name still resolves).
- This file is attached to the GitHub Release by CI (`gh release upload ... roblox-compat.json --clobber`) and fetched at startup for the drift banner + mutex name.

### 1.3 Release notes — [docs/store/release-notes-1.8.0.0.md](release-notes-1.8.0.0.md) (new)

Clan-facing (Pet Sim 99 audience), mirror [release-notes-1.7.1.0.md](release-notes-1.7.1.0.md) shape: maintainer note block → pasteable `---` body with `## Download`, `## What changed` (three `###` sub-sections — Limited-session handling, the "stay active" activity awareness, and the tray-residence fix in plain terms: *"RoRoRo now knows when Roblox is hiding in your system tray, and gets you running without restarting anything"*), `## Compatibility` (Roblox 0.727, Windows 11), `## Other channels`, `## Issues, ideas`. **Keep the "auto-update is check-only" caveat** unless Phase 0 chose Option A.

### 1.4 Reviewer letter — [docs/store/reviewer-letter-1.8.0.0.md](reviewer-letter-1.8.0.0.md) (new)

Use the **fuller [reviewer-letter-1.4.0.0.md](reviewer-letter-1.4.0.0.md) shape**, not the minimal 1.7.1.0 one — v1.8 has a real disclosure-surface delta (activity awareness added the consent-gated `GetAccountActivity` plugin query). Lead with the policy-10.2.2 framing in the first 30 seconds (per the 1.4 letter's own rule), then `WHAT'S NEW IN v1.8` covering all three branches, emphasizing: activity awareness exposes only *idle-time* to consenting plugins (never keystrokes/content — the Part A wall), and the tray gate is pure local process/mutex handling (no new network, no new data). Copy the trademark/privacy boilerplate forward.

### 1.5 CLAUDE.md MSIX fix (fold into this PR)

CLAUDE.md's "Common tasks" table points MSIX builds at `src/RORORO.Package/RORORO.Package.wapproj` — **that project doesn't exist**. Correct the two rows to the real scripts:
- Sideload → `powershell -File scripts/build-msix.ps1 -Sideload -CertPath dev-cert.pfx -CertPassword <pwd>`
- Store → `powershell -File scripts/finalize-store-build.ps1 -Version 1.8.0.0 -IdentityName 626LabsLLC.RoRoRoBlox -PublisherCN "CN=177BCE59-0966-4975-9962-10E36652141F" -PublisherDisplayName "626Labs LLC"`

---

## Phase 2 — Publish PluginContract 0.3.0 to NuGet (outward-facing — needs API key)

Independent of the app release. Unblocks ur-AFK's `dotnet restore`. [PluginContract csproj](../../src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj) is at `0.3.0`, PackageId `ROROROblox.PluginContract`, README + MIT license already set. No CI for this — manual per [the 2026-05-14 publish plan](../superpowers/plans/2026-05-14-plugincontract-nuget-publish.md) (plan text says 0.1.0; substitute 0.3.0):

```powershell
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" pack src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj -c Release -o artifacts/nuget
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" nuget push artifacts/nuget/ROROROblox.PluginContract.0.3.0.nupkg --api-key <NUGET_ORG_API_KEY> --source https://api.nuget.org/v3/index.json
```

**You provide:** the nuget.org API key (never commit it). Verify the pushed package resolves before telling ur-AFK it's unblocked.

---

## Phase 3 — MSIX builds (outward-facing — needs certs; manual/local, NOT in CI)

`release.yml` builds **Velopack only** — MSIX is a separate local step. The manifest carries **one identity at a time**, so order matters: Store build first, restore, then sideload.

### 3.1 Store-signed MSIX (for Partner Center)

```powershell
powershell -ExecutionPolicy Bypass -File scripts/finalize-store-build.ps1 `
  -Version 1.8.0.0 `
  -IdentityName "626LabsLLC.RoRoRoBlox" `
  -PublisherCN "CN=177BCE59-0966-4975-9962-10E36652141F" `
  -PublisherDisplayName "626Labs LLC"
```

Produces `dist/RORORO-Store.msix` — **unsigned** (Partner Center signs on upload). Then roll the manifest back before the sideload build:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/finalize-store-build.ps1 -RestoreManifest
```

### 3.2 Self-signed sideload MSIX (for direct-install clan members / testing)

Regenerate the dev cert per box if missing (`dev-cert.pfx` is gitignored), then:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/generate-dev-cert.ps1   # if dev-cert.pfx absent
powershell -ExecutionPolicy Bypass -File scripts/build-msix.ps1 -Sideload -CertPath dev-cert.pfx -CertPassword <pwd>
```

Produces `dist/RORORO-Sideload.msix`, signed with the self-signed cert. **The Store cert and this sideload cert are never the same key** (CLAUDE.md hard rule).

---

## Phase 4 — Tag → Velopack release via CI (the clan-facing channel)

`release.yml` triggers on `v*.*.*.*` tag push → runs `dotnet test ROROROblox.slnx` → `scripts/build-velopack-release.ps1` (packs `RORORO` Setup.exe + full/delta nupkg via `vpk pack`) → uploads a **DRAFT** GitHub Release → attaches `roblox-compat.json`.

```powershell
git tag v1.8.0.0
git push origin v1.8.0.0
```

Then: watch the Actions run green, review the **draft** release (Setup.exe + `releases.win.json` + `roblox-compat.json` present, release-notes body pasted from 1.3), and **publish** the draft. (Or use the `workflow_dispatch` form with `publish: true` to skip the draft step — not recommended for a bundle cut; review the draft first.)

---

## Phase 5 — Partner Center submission (Store channel)

1. Partner Center → RoRoRo (Store ID `9NMJCS390KWB`) → new submission.
2. Upload `dist/RORORO-Store.msix`.
3. Paste [reviewer-letter-1.8.0.0.md](reviewer-letter-1.8.0.0.md) (the pasteable block) into **Notes for certification**.
4. Paste [release-notes-1.8.0.0.md](release-notes-1.8.0.0.md) into the listing's release-notes field.
5. Confirm declared capabilities unchanged (`runFullTrust` only — no new capability from any of the three branches; the tray gate is local mutex/process, activity awareness is a plugin-facing query gated by existing consent).
6. Submit. Log the outcome (accept/reject/cert-request) as a distribution decision.

---

## Phase 6 — Post-release

- Verify the Velopack feed serves 1.8.0.0 to an existing install (once download+apply lands — check-only until then per Phase 0).
- Tell ur-AFK it's unblocked: PluginContract 0.3.0 on NuGet **and** the 1.8 host is live (its README floor "RoRoRo 1.8 or later" is now met).
- Log the release to the 626 dashboard (distribution decision + Store outcome).
- **Carry-forward follow-ups from the tray gate** (filed in the SDD ledger, non-blocking): `MutexContestedWatcher.Reset()` to close the sub-5s re-arm race; a try/catch on `TryRecoverMultiInstance`'s probe for fail-open symmetry with the gate. Fold into a small v1.8.1 hygiene branch or the v1.9 work.

---

## Credentials/assets YOU hold (I can't drive these)

| Step | You provide |
|---|---|
| PluginContract NuGet push | nuget.org API key |
| Store MSIX build | Partner Center reservation identity (in the finalize command above) |
| Sideload MSIX | `dev-cert.pfx` password (cert regenerated locally) |
| Partner Center submission | Partner Center login + the submission itself |
| Publish the draft GitHub Release | GitHub (the tag push triggers CI; you click publish) |

Everything in **Phase 1** I can do now on a branch and PR for your review. Phases 2–5 I prep + guide; you supply keys and click the outward-facing buttons.
