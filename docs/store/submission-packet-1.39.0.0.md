# Submission packet — v1.39.0.0

Everything Partner Center asks for on this submission, in the order it asks. Certification last saw
**v1.38.0.0**, so this is a single version's delta: 40 files and +3,150/−175 across the repository,
19 files and +2,365/−34 under `src/`, of which the shipped code is 10 files and +628/−34 (measured
`v1.38.0.0..31df6f7` on the release branch, before the version stamp). Six reliability fixes and no
new feature, all from the 2026-11-16 quality and stability sweep
(`docs/cycles/2026-11-16-quality-and-stability/board.md`).

> **PUBLISHED 2026-11-16, both channels.** Submission `1152921505702420821` was committed from the
> console at 11:56 CST on the owner's word, left Certification within 36 minutes with no report, and
> was published by the owner; the public catalog served 1.39.0.0 at 13:00:02. The GitHub release
> went Latest at 13:27 CST. See section 8.

---

## Read this first: one new file and one new observation, both disclosed

The reviewer letter names both in its own section rather than leaving a reviewer to find them:
[`reviewer-letter-1.39.0.0.paste.txt`](reviewer-letter-1.39.0.0.paste.txt).

1. **A new file, `accounts.dat.bak`.** The previous good copy of the accounts file, in the same
   folder, under the same DPAPI envelope. It holds session cookies. `docs/PRIVACY.md` gained a row.
2. **The app watches its own window's queued messages.** `ExternalCloseWatcher` subscribes to WPF's
   `ComponentDispatcher.ThreadFilterMessage` on its own UI thread to tell a close posted by another
   program from the user's own. It is a managed event, not a Windows hook, and it sends nothing.

**Measured against `v1.38.0.0`, not assumed** (2026-11-16):

- `Package.appxmanifest`: no committed change. `finalize-store-build.ps1` stamps the Version
  attribute and nothing else.
- `plugin_contract.proto`: unchanged (`git diff --stat v1.38.0.0..HEAD -- '*.proto'` is empty).
- `NativeMethods.txt`: unchanged, so no new CsWin32 import.
- Interop and input synthesis in added source lines under `src/ROROROblox.App` and
  `src/ROROROblox.Core`: **zero** matches for `SendInput`, `keybd_event`, `mouse_event`,
  `SetWindowsHookEx`, `SendMessage`, `WriteProcessMemory`, `OpenProcess`, `LibraryImport`,
  `DllImport`, `RegisterRawInputDevices`. `PostMessage` matches once, in a doc comment in
  `ExternalCloseWatcher.cs` describing how another program delivers `WM_CLOSE`.
- New URLs, link schemes or addresses in added source lines: **none**.
- Registry access in added source lines: **none**.
- New network hosts: **none**.
- UI strings: **none changed** (no `.resx` and no `docs/store/translations` file in the diff).

## 1. Version

**1.39.0.0.** A MINOR bump. The playbook's table allows BUILD for a release of fixes; MINOR was
chosen because two changes are behaviour a user can see and may rely on (a close posted by another
program now quits; a second accounts file appears on disk). **The owner agreed to the number in
session; say so before the tag if 1.38.1.0 is preferred.** Nothing else in the release depends on
which it is, apart from the file names.

## 2. Listing

**Listing audited against the v1.39 release notes on 2026-11-16: short description, long
description, product features and the hub page are unchanged.**

| Surface | Checked for | Outcome |
|---|---|---|
| Short description | Whether any of its claims is touched | None is. Unchanged. |
| Long description | The vault bullet ("a copy of the vault file moved to another PC will not decrypt") and the privacy paragraph ("kept only in the same encrypted vault"; "Nothing about you leaves your machine except…") | Both still hold. The backup is the vault's previous copy under the same encryption, in the same folder, and is never sent. **A judgment call, recorded:** "the same encrypted vault" now means two files, not one. The claim's substance (encrypted at rest, nowhere else) is unchanged, so the text stays; the privacy policy carries the detail. Unchanged. |
| Product features (20/20) | Whether the backup earns an entry | It is a trust point, but the list is at its cap and no entry is weaker than it would be. Unchanged. |
| Hub page (`docs/index.md`) | "Encrypted account vault", "Memory watchdog", "No telemetry" | All hold. Unchanged. |

### The product name

The English name `RoRoRo — Multi-Instance Launcher & Account Manager` has been live since
2026-11-12, and the owner ruled on 2026-11-16 to hold it. This submission is created by the API
from the published one, so it carries that name; the API never writes a product name. Board item 12
of `docs/cycles/2026-11-10-growth-and-quality/board.md` has the reads.

## 3. What's new in this version

[`whats-new-1.39.0.0.md`](whats-new-1.39.0.0.md), seven blocks, each under the 1,500-character cap
(839 to 1,070 characters). The headline and five bullets are new text; the two closing paragraphs in
every language are v1.36's, unchanged. The six language sheets carry the same blocks, stepped
verbatim by script. The fourth bullet says "another program", not Task Manager: `taskkill` without
`/F` was measured, End task was not.

**Listing verifier:** recorded in section 7.

## 4. Notes for certification

[`reviewer-letter-1.39.0.0.paste.txt`](reviewer-letter-1.39.0.0.paste.txt), 95 lines, 4,586
characters (v1.33's was 4,584 and v1.29's 5,781). Partner Center only; `submit_write.py` never
sends it.

**Replace the whole box.** The notes live on the product-level page Additional Testing Information,
and the box holds the v1.38.0.0 letter. Select everything and paste this one over it.

## 5. Age rating, privacy, screenshots

- **Age rating:** unchanged. No new content and no user-to-user communication.
- **Privacy:** `docs/PRIVACY.md` gains one row in the stored-files table, for `accounts.dat.bak`.
  The hosts table is unchanged. The privacy policy URL on the listing is unchanged.
- **Screenshots:** unchanged. Nothing on screen changed.

## 6. Packages

Built 2026-11-16 on Dunder from the release branch, with `scripts/finalize-store-build.ps1` (arm64,
then x64) and `scripts/build-msix.ps1 -Sideload`. Built twice: first ahead of the smoke, then again
at 11:18 to 11:20 CST after the comment correction the smoke led to, so the packages match the tree
this branch ends on. The sizes below are the second build's. **If any code changes again, rebuild
all three.**

| File | Size | For |
|---|---|---|
| `dist/RORORO-Store-x64-1.39.0.0.msix` | 112,390,820 bytes | Partner Center, unsigned |
| `dist/RORORO-Store-arm64-1.39.0.0.msix` | 105,901,719 bytes | Partner Center, unsigned |
| `dist/RORORO-Sideload-x64-1.39.0.0.msix` | 112,413,479 bytes | GitHub release, signed |

The sideload package's signer is `CN=177BCE59-0966-4975-9962-10E36652141F`, SHA-1
`8232F92D70CE24B9EF2CCEB4B458F8C26D94AB87`, the unrotated dev certificate; the copy of
`dev-cert.cer` from the v1.37.0.0 release has the same thumbprint (compared 2026-11-16). The
repo-root `dev-cert.cer` on Dunder is still the stray (`4D745C22…`); attach the matching one, never
that.

## 7. Verification

- **App strings.** No string changed, so there is no UI translation delta to verify.
- **Listing verifier on What's new, 2026-11-16.** Six languages at commit `31df6f7`, both shapes
  (by language and by field), all six judged in both. Every language drew one finding and nothing
  else: Rule 3 wants the tray menu's Quit left in English. Overruled, as at v1.36, v1.37 and v1.38,
  for the same reason: the interface is localized, each word is the app's own label, and the
  sentence is v1.36's text. The new headline and the five new bullets drew no finding in any
  language. The first call returned a 502 and the second timed out at the client; the status read
  afterwards showed all six pairs judged in both shapes.
- **Tests.** 3,596 unit and 27 harness pass on the release branch with the version stamped
  (Release, 2026-11-16, Dunder); 11 unit and 1 harness skip by design. Every fix has a test that
  was seen to fail with the fix removed (`docs/cart-runs/2026-11-16-v139-stability/report.md`).
  `ci.yml` on the two pull requests is not recorded here; check it before merging.
- **Measured during the build.** `taskkill` without `/F` posts `WM_CLOSE` through the message
  queue to a visible and to a hidden window (a throwaway probe window, not RoRoRo). The WPF-UI
  4.3.0 title bar's X calls `Window.Close()` (read from the IL; a test drives a real title bar).
  `Dispatcher.Invoke` throws `TaskCanceledException` when shutdown aborts a queued call (a test
  reproduces it).
- **Pre-release agents, 2026-11-16.** Cookie exposure audit over `git diff v1.38.0.0..HEAD -- src`:
  clean. The new log lines in `AccountStore` carry file names, a count and the I/O exception, never
  contents; both temp files are written only from encrypted bytes; the Diagnostics support bundle
  takes `snapshot.txt` and the three newest `*.log` files and never enumerates the data folder, so
  the backup is not picked up. Two notes, both inside the DPAPI blob: the old cookie stays in the
  backup for one save after a re-login, and if the scrub copy and the delete both fail after a
  removal the stale backup stays and only a warning says so. Acting on its last note, `.gitignore`
  and the keystone's never-commit rule now name `accounts.dat.bak` and the two temp files. MSIX
  assets: pass by identity with v1.38.0.0 (the manifest differs by its Version attribute alone, no
  file under `Package/` changed) plus a reference check of all six logos; it noted the square logos
  read magenta on navy with cyan only in the wide and splash assets, which is how they have
  shipped. The auth-ticket flow validator was not run: it needs a test cookie that is not set.
- **Plugin catalog drift:** clean, five entries match their live releases
  (`scripts/check-plugin-catalog-drift.ps1`, exit 0).

**Not verified, and said so in the release notes:** the fixes the live smoke below did not reach;
and, carried, the crash line after a real crash, the v1.38 launch messages against a live Roblox
failure, the launch check on a PC whose only Roblox is the Store edition, the rating line on a real
Store install, and the 75/100/180 caps on a live client.

### Live smoke, 2026-11-16 (the owner, a Release dev build on Dunder, his real data folder)

The first live run of this code. Times and lines are from `rororoblox-20261116.log`; what the owner
saw is marked as his report.

| Check | Result | Evidence |
|---|---|---|
| The X, Alt+F4 and the taskbar's Close hide the window | Pass | The owner's report |
| Task Manager's End task ends RoRoRo | **Pass, and now measured** | 11:12:26.979 "An external close request arrived; RoRoRo is quitting.", exit code 0 ten milliseconds later |
| No crash notice on the start after it | Pass | 11:12:45 "Previous session ended: clean" |
| The backup appears | Pass | After a launch: `accounts.dat.bak` written 11:14:55.018 and `accounts.dat` 11:14:55.500, same size, not byte-identical (compared by hash), and no swap warning in the log. The backup is one save behind, as designed |
| Quit from the tray with an account running | Pass, and it answers the open question | The owner's report: the Roblox client stayed up. One `RobloxPlayerBeta` was still running afterwards; exit code 0 at 11:15:18; `last-exit.json` reads `clean` |
| No memory badge with no client | **Not exercised** | The owner saw no badge, but free memory was 4.9 to 6.3 GB, above any reserve the default can produce (at most 4 GB), so the gate had nothing to gate |

Nothing in the log came from the new code as a warning or an error. The one warning was plugin
processes left over from the previous session, which is expected after a PC shutdown.

**Seen again, and not part of this release:** both exits logged "ServiceProvider.DisposeAsync did
not complete within 2s; abandoning." That is board item 6, now two of two exits on v1.39.

**Settled by the smoke:** quitting RoRoRo does not close the Roblox clients. The two comments that
said it did (`App.RequestShutdown`, `MainViewModel.ExpectCloseForAll`) are corrected in this branch,
with the date. A comment-only change; the packages were rebuilt after it so they match the tree.

**Still not run live:** a PC shutdown or logoff with a client open; the backup rescuing a damaged
file; a relaunched account whose older client is then closed; a session expiring mid-game;
`taskkill` without `/F` against RoRoRo itself (measured against a probe window only); and the
memory gate on a PC that is actually short of memory.

## 8. Shipped

Filled in as each step happens.

| Step | When (2026-11-16, CST) | Evidence |
|---|---|---|
| Owner's smoke | 11:11 to 11:15 | Section 7, "Live smoke" |
| PR #290 merged (the fixes), then #291 (release documents and the version bump) | about 11:33 | `main` at `ec952ec`; four green checks on each head before the merge; `ci.yml` green on `ec952ec` by 11:55 |
| Store submission created | about 11:35, `apply`, on the owner's word ("do the first part of partner center") | `1152921505702420821`, cloned from the published v1.38 submission; the plan showed ten `releaseNotes` fields changing and nothing else; written and read back; captions already matched |
| Verified before the owner's visit | 11:36, `verify` | 180 listing fields and captions served; status `PendingCommit` |
| Owner's two pages | before 11:55 | Packages (x64 and arm64) and the reviewer letter replacing the whole Additional Testing Information box; done by the owner in Partner Center |
| Verified after the visit | 11:55, `verify` | status `PendingCommit`, text intact (180 lines); packages still read 1.38, as expected before a commit |
| Committed | 11:56:09, on the owner's word ("my side is done") | `CommitStarted` |
| Packages visible | 12:00:03, the sixth 30-second poll, about four minutes | status `Certification`; `RORORO-Store-x64-1.39.0.0.msix` and `RORORO-Store-arm64-1.39.0.0.msix`, both `Uploaded` |
| Certified | between 12:22 and 12:32 | A ten-minute status watcher read `Certification` at 12:22 and `Release` at 12:32. No certification report. Twenty-six to thirty-six minutes after the commit |
| Publish now | about 12:51, the owner's click in Partner Center | The owner's word in session; `verify` read status `Release` at 12:52 and the catalog still served 1.38.0.0 |
| Tag `v1.39.0.0` pushed | 12:54:41, on the owner's word | On `ec952ec`, with `ci.yml` green there; `release.yml` run 43933778423, success at 12:59 |
| GitHub release drafted | same run, then by hand | 13 assets: Setup.exe, the two nupkgs, Portable.zip, the signed sideload MSIX, `dev-cert.cer` (v1.37's copy, thumbprint `8232F92D…`, matching the sideload signer), the signed feeds. Notes set from the release notes, below the edit log |
| Live on the Store | 13:00:02 | The public catalog served `626LabsLLC.RoRoRoBlox_1.38.0.0` at 12:55:17 and `…_1.39.0.0` for x64 and arm64 at 13:00:02; `verify` read status `Publishing` then |
| GitHub release published as Latest | 13:27 (2026-11-16 19:27:17 UTC), on the owner's word | `gh release edit v1.39.0.0 --draft=false --latest`; `releases/latest` answers `v1.39.0.0` with 13 assets; the update feed serves 1.39.0. The catalog was read again first and still served 1.39.0.0 |

**Nothing is left open.** Both channels are published. There is no clan post file this release.
The tag came after the Store commit this time, not before it as at v1.38; the order matters to
neither channel.

**Console:** worktree `_scratch/console-rororo-137`, branch `copy/rororo-1.39`, pinned at `ec952ec`.
Every command named `--payload out/rororo-live-listing.json`, `commit` included.
