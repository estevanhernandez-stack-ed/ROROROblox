# Submission packet — v1.38.0.0

Everything Partner Center asks for on this submission, in the order it asks. Certification last saw
**v1.37.0.0**, so this is a single version's delta, and a small one: 48 files and +1,888/−126 across the
repository, 26 files and +1,193/−50 under `src/` (measured `v1.37.0.0..main` at `b730459`, before the
release branch's own docs). Three changes, all from the 2026-11-10 evidence sweep
(`docs/cycles/2026-11-10-growth-and-quality/board.md`).

> **PUBLISHED 2026-11-15.** Submission `1152921505702405800` was committed from the console at 14:28 CST
> on 2026-11-14 on the owner's word, certified about 25 hours later with no report, and published by the owner on
> 2026-11-15. The public catalog served 1.38.0.0 at 15:54:56 CST, and the GitHub release went Latest at
> 18:24 CST the same day. See section 8.

---

## Read this first: nothing new at the system boundary

The reviewer letter says so in its own section rather than leaving a reviewer to look:
[`reviewer-letter-1.38.0.0.paste.txt`](reviewer-letter-1.38.0.0.paste.txt).

**Measured against `v1.37.0.0`, not assumed** (2026-11-14, at `main` b730459):

- `Package.appxmanifest`: no change on main (`git diff --stat` empty). `finalize-store-build.ps1`
  stamps the Version attribute and nothing else; the release branch's diff is that one attribute.
- `plugin_contract.proto`: unchanged (`git diff --stat v1.37.0.0..main -- '*.proto'` is empty).
- Interop and input synthesis in added source lines under `src/ROROROblox.App` and
  `src/ROROROblox.Core`: **zero** matches for `SendInput`, `keybd_event`, `mouse_event`,
  `SetWindowsHookEx`, `PostMessage`, `SendMessage(`, `WriteProcessMemory`, `OpenProcess`,
  `LibraryImport`, `DllImport`, `RegisterRawInputDevices`.
- New URLs or link schemes in added source lines: **none**. The grep's only matches are
  `support@626labs.dev` inside the crash notice's text, in seven resx files. It is shown as plain
  text; the row has two buttons and no hyperlink (`MainWindow.xaml`, the crash-notice row).
- Registry access in added source lines: **none**.
- New files written: **none**. The crash notice reads `last-exit.json`, written since v1.35.
- New network hosts: **none**. `RobloxApi` changed which exception type two existing failure paths
  throw; the requests are the same.

## 1. Version

**1.38.0.0.** A MINOR bump per the playbook's table: one user-visible feature (the crash notice) and
two user-visible fixes (launch errors by kind, the first-run row).

## 2. Listing

**Listing audited against the v1.38 release notes on 2026-11-14: short description, long description,
product features and the hub page are unchanged.**

| Surface | Checked for | Outcome |
|---|---|---|
| Short description | Whether any of its claims (games, FPS caps, phone alerts, themes, seven languages, alts) is touched | None is. Unchanged. |
| Long description | The privacy and security paragraph; "Speaks your language … and the messages RoRoRo writes as you use it" | The crash notice sends nothing, so the privacy paragraph still holds. The language claim is more true than before: four launch failures that showed English now show a translated sentence. Unchanged. |
| Product features (20/20) | Whether the crash notice, the launch messages or the row fix earn an entry | None is a selling claim, and the list is at its cap. Unchanged. |
| Hub page (`docs/index.md`) | The "What you get" list, re-read this release (it was skipped at v1.37) | Thirteen entries, none touched. "No telemetry" in the hero still holds. Unchanged. |

### The product name

The English name is under test and is **not part of this submission**:
`RoRoRo — Multi-Instance Launcher & Account Manager`, live since 2026-11-12 on a hand-made,
listing-only submission (`1152921505702382810`). This submission is created by the API from the
published one, so it carries that name as published; the API never writes a product name. The
test's second read is due 2026-11-16. **If the owner decides to revert the name, that is another
hand-made submission, and it cannot be started while this one is in certification.** Board item 12
has the baseline and the first read.

## 3. What's new in this version

[`whats-new-1.38.0.0.md`](whats-new-1.38.0.0.md), seven blocks, each under the 1,500-character cap
(725 to 911 characters). The headline and three bullets are new text; the two closing paragraphs in
every language are v1.36's, unchanged. The six language sheets carry the same blocks, stepped
verbatim by script.

**Listing verifier:** recorded in section 7.

## 4. Notes for certification

[`reviewer-letter-1.38.0.0.paste.txt`](reviewer-letter-1.38.0.0.paste.txt), 68 lines, 3,203
characters. Partner Center only; `submit_write.py` never sends it.

**The box moved and is not empty.** Since 2026-11-11 the notes live on a product-level page,
Additional Testing Information, and the box opens with a listing-only paragraph from the name test
followed by the v1.37.0.0 letter. Replace the whole box with this letter.

## 5. Age rating, privacy, screenshots

- **Age rating:** unchanged. No new content and no user-to-user communication.
- **Privacy:** `docs/PRIVACY.md` is unchanged. `last-exit.json` has been in its stored-files table
  since v1.37's packet added it, and its row already says nothing is transmitted.
- **Screenshots:** unchanged. The crash line shows only after a crash, and a failed launch is not a
  carousel image.

## 6. Packages

Built 2026-11-14 on Dunder from the release branch, code identical to `main` at b730459, with
`scripts/finalize-store-build.ps1` (arm64, then x64) and `scripts/build-msix.ps1 -Sideload`.

| File | Size | For |
|---|---|---|
| `dist/RORORO-Store-x64-1.38.0.0.msix` | 112,380,414 bytes | Partner Center, unsigned |
| `dist/RORORO-Store-arm64-1.38.0.0.msix` | 105,892,314 bytes | Partner Center, unsigned |
| `dist/RORORO-Sideload-x64-1.38.0.0.msix` | 112,403,077 bytes | GitHub release, signed |

The sideload package's signer is `CN=177BCE59-0966-4975-9962-10E36652141F`, SHA-1
`8232F92D70CE24B9EF2CCEB4B458F8C26D94AB87`, the unrotated dev certificate and the same thumbprint as
`dev-cert.pfx` (compared 2026-11-14). The repo-root `dev-cert.cer` on Dunder is still the stray
(`4D745C22…`); attach the matching `.cer` from the v1.37.0.0 release, never that one.

## 7. Verification

- **App strings, 2026-11-14.** Twelve changed keys in six languages (72 pairs) through the hosted
  translation verifier at commit `ba1b49e`, both shapes: 58 approve, 14 revise, no string changed.
  Nine revise verdicts are Rule 3 on Library and Diagnostics, overruled as the known false positive:
  the interface is localized and each flagged word matches the app's own label in that language. Five
  are the empty-state hint's dropped verb in fr, de, ru, pt-BR and es: accurate, deliberate (#279, the
  row's width), and the owner ruled on 2026-11-14 to keep the short hints. Detail in PR #282.
- **Listing verifier on What's new, 2026-11-14.** Six languages at commit `a476fa7`. French approves.
  The other five each drew the same two findings and nothing else: Rule 3 wants Diagnostics and the
  tray menu's Quit left in English. Overruled, as at v1.36 and v1.37, for the same reason; the Quit
  sentence is v1.36's text. The new headline and the three new bullets drew no other finding in any
  language. The first call returned a 502 and the second timed out at the client after judging all
  six, so the language-by-language pass is complete and the field-by-field second pass is not
  confirmed.
- **Tests.** 3,528 unit and 27 harness pass on the release branch with the version stamped (Release,
  2026-11-14, Dunder); 11 unit and 1 harness skip by design. `ci.yml` green on x64 and arm64 for every
  merged PR (#276, #277, #278, #279) and on `main` at b730459.
- **Mutation checks,** from the feature PRs: the crash-notice rules (treating `unknown` as a crash, not
  suppressing the rating ask, leaving the row out of the strip's collapse) and the launch-error
  classification (dropping the network case, removing either status throw, putting the detail back on
  a named sentence) were each broken on purpose and seen to fail.
- **Pre-release agents, 2026-11-14.** Cookie exposure audit over `git diff v1.37.0.0..origin/main --
  src`: clean. `RobloxApiStatusException` carries a status code and a message built from literals; the
  four named launch sentences show no detail; the crash notice binds a static string and nothing from
  the crash record. One pre-existing path noted and unchanged since v1.37: `ProcessStartFailed` shows
  the exception message from `Process.Start`, which is handed the launch link; no .NET behaviour that
  echoes the link was found. MSIX assets: pass by identity, manifest and `Package/Logos` unchanged
  since v1.37.0.0. The auth-ticket flow validator was not run: it needs a test cookie that is not set.
- **Plugin catalog drift:** clean, five entries match their live releases
  (`scripts/check-plugin-catalog-drift.ps1`, exit 0).

**Not verified, and said so in the release notes:** the crash line after a real crash; the launch
messages against a live Roblox failure; and, carried from v1.37, the launch check on a PC whose only
Roblox is the Store edition, the rating line on a real Store install, and the 75/100/180 caps on a
live client.

**Found while writing this, not fixed here:** in German and Polish the Diagnostics page's button says
"Support-Bündel" and "pakiet pomocy" while the save dialog's title (`Diag_SaveBundleTitle`) says
"Support-Bundle" and "pakiet wsparcia". Both predate this release; the crash notice and the What's
new use the button's wording.

## 8. Shipped

| Step | When (2026-11-14, CST) | Evidence |
|---|---|---|
| Release PR #283 merged | 13:40 | `main` at `08c4f5f`; `ci.yml` green on x64 and arm64, on the PR head and again on main |
| Store submission created | about 13:42, `apply`, on the owner's word to ship | `1152921505702405800`, cloned from the published name-test submission; ten `releaseNotes` fields written and read back; captions already matched |
| Verified before the owner's visit | `verify` | 180 listing fields and captions served; status `PendingCommit` |
| Tag `v1.38.0.0` pushed | 13:45, after CI went green on main | `release.yml` run 43633796518, success at 13:51 |
| GitHub release drafted | same run, then by hand | 13 assets: Setup.exe, the two nupkgs, Portable.zip, the signed sideload MSIX, `dev-cert.cer` (v1.37's copy, thumbprint `8232F92D…`, matching the sideload signer), the signed feeds. Notes set from the release notes, below the edit log. Held as a draft until the Store was live; see the last row. |
| Owner's two pages | before 14:27 | Packages (x64 and arm64) and the reviewer letter, replacing the whole Additional Testing Information box; done by the owner in Partner Center |
| Verified after the visit | 14:27, `verify` | status `PendingCommit`, text intact (180 lines); packages still read 1.37, as expected before a commit |
| Committed | 14:28:02, on the owner's word | `CommitStarted` |
| Packages visible | 14:31:56, the sixth 30-second poll, about four minutes | status `Certification`; `RORORO-Store-x64-1.38.0.0.msix` and `RORORO-Store-arm64-1.38.0.0.msix`, both `Uploaded` |
| Still certifying | 19:01 | `verify` read status `Certification`; the public catalog served 1.37.0.0 |
| Certified | 2026-11-15, shortly before 15:48, by the owner's account | The owner had been checking Partner Center through the day and reported it in session at about 15:48, so the wait was about 25 hours: the longest RoRoRo certification on record, past v1.37's 23. No certification report. Not a logged timestamp; the last `Certification` read from here was 19:01 the evening before |
| Publish now | 2026-11-15, about 15:48, the owner's click in Partner Center | `verify` read status `Release` at 15:48; the catalog served 1.37.0.0 at 15:48:54 |
| Live on the Store | 2026-11-15, 15:54:56 | The public catalog served `626LabsLLC.RoRoRoBlox_1.38.0.0` for x64 and arm64, on the fourth two-minute poll; `verify` read status `Published` at 15:55 |
| GitHub release published as Latest | 2026-11-15, 18:24 (2026-11-16 00:24:22 UTC), on the owner's word | `gh release edit v1.38.0.0 --draft=false --latest`; `releases/latest` answers `v1.38.0.0` with 13 assets. The catalog was read again first and still served 1.38.0.0 |

**Nothing is left open.** Both channels are published. There is no clan post file this release, on the
owner's word.

**Console:** worktree `_scratch/console-rororo-137`, branch `copy/rororo-1.38`, pinned at `41d304a`.
Every command named `--payload out/rororo-live-listing.json`, `commit` included.
