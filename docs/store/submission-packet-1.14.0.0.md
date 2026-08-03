# Submission packet — v1.14.0.0

> Everything for the Partner Center submission in one place, in the order you'll need it.
> Built and verified 2026-08-03. **The submission click is yours; everything before it is done.**
>
> Companion docs: [`reviewer-letter-1.14.0.0.md`](reviewer-letter-1.14.0.0.md) ·
> [`release-notes-1.14.0.0.md`](release-notes-1.14.0.0.md) · [`listing-copy.md`](listing-copy.md) ·
> [`submission-checklist.md`](submission-checklist.md) (the full standing procedure — this packet
> is the v1.14 instance of it)

## 1. Packages to upload

| Architecture | File | Size |
| --- | --- | --- |
| x64 | `dist/RORORO-Store-x64-1.14.0.0.msix` | 90.1 MB |
| arm64 | `dist/RORORO-Store-arm64-1.14.0.0.msix` | 85.0 MB |

Both are **unsigned** — Partner Center re-signs with the Store certificate. That is expected and
correct; do not sign them locally. The sideload cert and the Store identity are never the same key.

Built with `scripts/finalize-store-build.ps1` at `-Version 1.14.0.0`,
`-IdentityName 626LabsLLC.RoRoRoBlox`, `-PublisherCN "CN=177BCE59-0966-4975-9962-10E36652141F"`,
`-PublisherDisplayName "626Labs LLC"`. The working-tree manifest was restored afterwards
(`git status` clean).

## 2. Notes for certification

Paste the fenced block from [`reviewer-letter-1.14.0.0.md`](reviewer-letter-1.14.0.0.md).

**One thing to check before you paste.** The letter opens against a baseline of v1.12.0.0, on the
assumption that v1.12 is what Partner Center currently has approved. I can't see Partner Center.
Open the app's submission history first — if the live version is older than 1.12, change that
reference. A letter that claims a baseline the reviewer cannot see is the wrong way to start.

## 3. Store listing fields

- **What's new in this version** — the `v1.14.0.0:` block in
  [`listing-copy.md`](listing-copy.md) § *What's new*. Five bullets, no marketplace mention, no
  queue mechanics (accurate but reads as a defect warning in a listing field).
- **Description, features, copyright, trademark** — unchanged from the last submission. Nothing in
  this release touches them.
- **Age rating** — unchanged; [`age-rating.md`](age-rating.md) if the questionnaire is re-asked.
- **Privacy policy URL** — `https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/`

## 4. Checks already run (2026-08-03, on the built packages)

| Check | Result |
| --- | --- |
| `Package.appxmanifest` Version | `1.14.0.0`, 4th component zero |
| `ROROROblox.App.csproj` `<Version>` | `1.14.0.0` — matches the manifest |
| Identity inside the built x64 MSIX | `626LabsLLC.RoRoRoBlox`, `CN=177BCE59-…`, `1.14.0.0`, `x64` |
| `PublisherDisplayName` | `626Labs LLC` (no space in 626Labs — the spaced form fails validation) |
| `TargetDeviceFamily MinVersion` | `10.0.19045.0` (Windows 10 22H2), unchanged |
| ReadProcessMemory / WriteProcessMemory / VirtualQueryEx / SetWindowsHookEx / RegisterRawInputDevices | **0 hits** across all source — this is the evidence behind the letter's memory paragraph |
| `RequestGameJob` construction sites | One: `RobloxLauncher.BuildPlaceLauncherUrl` (plus the doc comment on `LaunchTarget.GameJob` and three tests). No second site |
| Plugin EXE inside the package | **0** — marketplace stays compiled out |
| Full solution tests on the tagged commit | 1079 unit + 18 integration, green |

## 5. Open items — yours

1. **Confirm the approved baseline version** in Partner Center and fix the letter's "since
   v1.12.0.0" reference if it isn't v1.12. (§2 above.)
2. **Two screenshots are stale.** `01-accounts-streamer-mode.png` and `06-squad-launch.png` were
   captured 2026-08-01 and both show the toolbar button labelled **Private server**, which this
   release renames to **Squad Launch**. The squad modal's body copy changed too. A reviewer
   comparing screenshots to the running app would see a button that no longer exists. Not a
   blocker — screenshots aren't required to change per submission — but it's the kind of small
   mismatch that invites a second look. Say the word and I'll recapture both; it needs streamer
   mode on so no real account names ship in a Store asset.
3. **The GitHub release for v1.14.0.0 is a draft** — review and publish it. Its body comes from
   [`release-notes-1.14.0.0.md`](release-notes-1.14.0.0.md).
4. **v1.13.0.0's draft release is still sitting there.** Its work ships inside v1.14, so it will
   never be published on its own. Delete the draft, or leave it as a record — either is fine, but
   don't publish it after v1.14 or the version order on the releases page will read backwards.

## 6. If it comes back rejected

[`reviewer-letter-1.14.0.0.md`](reviewer-letter-1.14.0.0.md) § *Defenses by clause* has the
prepared answer for each clause, including what extra evidence to offer. The one to watch this
cycle is **10.1.1** — asking Roblox for a specific server could read as gaming a matchmaker. The
short answer: it's the same documented endpoint family behind Roblox's own "join a friend" button,
and a full server queues us like anyone else. That last point is field-verified, not asserted.
