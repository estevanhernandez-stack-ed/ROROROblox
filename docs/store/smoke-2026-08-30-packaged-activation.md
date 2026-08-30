# Live smoke — packaged activation (v1.24 feature, spec §4)

**Date:** 2026-08-30, ~15:45–15:55 local
**Build under test:** `main` at `20a9772` (PR #149), assembly v1.23.0 (version bump happens at release)
**Spec:** `docs/superpowers/specs/2026-08-30-packaged-activation-design.md`
**Verdict: PASS** — run-on-login state wiring (enable/read-back/disable) and both cold-start URI protocols work on a packaged install; teardown complete. The task firing at an actual sign-in was not observed (see residuals).

## How the package was installed (deviation from the plan, on the record)

The plan was `scripts/build-msix.ps1 -Sideload` + install the signed MSIX. The MSIX built and signed clean, and its packed `AppxManifest.xml` carries all three new extensions — but this machine's `LocalMachine\TrustedPeople` holds only the pre-2026-05-14 dev cert, not the current one (`CN=177BCE59-…`, thumbprint `8232F92D…`), and importing a machine-store cert needs an elevation that wasn't available in-session. Instead: the signed MSIX's payload was extracted and registered with `Add-AppxPackage -Register` (Developer Mode loose registration). That yields the **same `PackageFullName`** (`626LabsLLC.RoRoRoBlox_1.23.0.0_x64__wz1chhb2h2v4a`), the same manifest ingestion, the same registry virtualization, and the same activation routing — the mechanisms under test are identity-driven, not path-driven. The Store package was removed first and reinstalled after.

Residual for a future pass: an end-to-end run of the *signed* sideload install on a box that trusts the dev cert (testers' flow), and an actual sign-out/sign-in to watch the StartupTask fire — not exercisable from the session running the smoke.

## Environment controls

- The dev build was quit for the duration (its process was the only RoRoRo running; zero Roblox clients).
- The dev build's classic `HKCU\Software\Classes` scheme registrations were **deleted before the URI legs**, so activation could only resolve through the manifest protocols — a clean-Store-box simulation. (The dev build re-registers both keys on relaunch; verified restored after.)

## Results

| # | Leg | Evidence | Result |
|---|-----|----------|--------|
| 1 | Package registers with the new manifest | `Get-AppxPackage`: same PackageFullName as the Store install; `…AppModel\SystemAppData\<PackageFamilyName>\RoRoRo` key exists on registration (task ingested), `State=0` (disabled by default, as declared) | pass |
| 2 | Packaged app skips the registry writes | Log (`[DBG]`, file sink runs at Debug): `Packaged install: join URI schemes are declared in the manifest; skipping registry registration.` and `Packaged install: skipping Discord scheme command fixup; the manifest protocol owns the scheme.` Discord IPC still initialized, scheme registered, **Join subscription active** (Lachee's internal flag path intact). Full-window log sweep: zero errors; the only warning is the known-benign F-101 orphaned-plugin sweep | pass |
| 3 | Run-on-login enable via the app's real UI | Space-press on the focused `RunOnLoginToggle` (real `Click`, real handler) → checkbox On, Windows-side `State=2` (Enabled), `UserEnabledStartupOnce=1` | pass |
| 4 | `IsEnabled` read-back survives restart | App killed and cold-relaunched → Settings shows the toggle **On** (read through `PackagedStartupRegistration`, not a dead registry value — the v1.23 "toggle lies" bug is gone) | pass |
| 5 | Disable via the app's real UI | Second space-press → checkbox Off, Windows-side `State=0` | pass |
| 6 | Cold-start `roblox-rororo:` via manifest protocol | With the app stopped and no classic HKCU key: `Start-Process "roblox-rororo://join/<synthetic>"` → app launched from the package location; log: `Discord join URI failed to parse (cold start); ignoring.` — the URI reached `JoinUriParser` as plain argv; the synthetic secret failing to parse is the expected outcome | pass |
| 7 | Cold-start `discord-<appid>:` via manifest protocol | Same setup → fresh startup banner, app launched from the package location | pass |

## UI-automation footnote (for the next smoke)

`TogglePattern.Toggle()` on a WPF CheckBox flips `IsChecked` **without raising `Click`** — the first attempt turned the checkbox on while `Enable()` never ran (Windows state stayed 0). A real focused space-press goes through `ButtonBase.OnClick` and runs the handler. Any future scripted smoke of a `Click`-wired control must send real input, not the automation pattern.

## Teardown (verified)

Loose registration removed; extracted layout deleted; Store install restored via `winget install 9NMJCS390KWB`; dev build relaunched and both classic scheme keys re-registered to it; `accounts.dat` byte-identical timestamp (vault untouched throughout — the data folder is shared and real, per the 2026-08-30 virtualization findings).
