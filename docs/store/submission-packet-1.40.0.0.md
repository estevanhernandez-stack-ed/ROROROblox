# Submission packet — v1.40.0.0

> **PREPARED, NOT SHIPPED.** Drafted 2026-11-17 on `release/1.40.0.0`. Nothing here has been
> submitted, tagged, built or published, and the version is not stamped in the csproj or the manifest.
> Sections 6 and 8 are empty on purpose and are filled as each step happens.

Everything Partner Center asks for on this submission, in the order it asks. Certification last saw
**v1.39.0.0**, so this is a single version's delta, measured `v1.39.0.0..f1d0eeb` on the release
branch before any version stamp: 32 commits; 127 files and +8,633/-272 across the repository; 88
files and +7,336/-242 under `src/`, of which the shipped code and resources (outside
`src/ROROROblox.Tests`) are 57 files and +2,450/-236. One new feature (the update button) with one
new network call on Store installs, and a set of reliability fixes. Sources: the three Cart-run
reports under `docs/cart-runs/` (`2026-11-17-stability-part-two`, `2026-11-17-update-button`,
`2026-11-16-data-folder-override`) and `docs/decisions.md` (entries dated 2026-11-16 and 2026-11-17).

---

## Read this first: one new network call, one new URI, one changed observation, two new file names

The reviewer letter names all four in its own section rather than leaving a reviewer to find them:
[`reviewer-letter-1.40.0.0.paste.txt`](reviewer-letter-1.40.0.0.paste.txt).

1. **A new network call, on Store installs only.** `StoreContext.GetDefault()
   .GetAppAndOptionalStorePackageUpdatesAsync()` (`src/ROROROblox.App/Updates/StoreContextUpdateProbe.cs`),
   after startup and every six hours, 30-second timeout, only with package identity. Windows makes
   the query; RoRoRo reads a count. No `Request...` method is called (those show Store UI and need a
   window handle), and the app downloads and installs nothing. `docs/PRIVACY.md` has a new row.
   **Not run on a Store-installed build, and cannot be before a release that contains it.** The
   assumptions that ride on it, from the update-button report: the call works from an unelevated WPF
   app with package identity, needs no window handle, and returns one or more entries when an update
   is waiting. If any is wrong, no button shows and nothing else changes.
2. **A new URI, not a web address:** `ms-windows-store://pdp/?productid=9NMJCS390KWB`
   (`UpdateAvailability.StoreListingUri`). Launching it by hand opened the Microsoft Store app on the
   owner's machine; which page it landed on was not visible to the session.
3. **The close-request observation widened.** v1.39's `ExternalCloseWatcher` watched the main window;
   `ThreadCloseWatcher` hooks `ComponentDispatcher.ThreadFilterMessage` for the whole UI thread so a
   startup dialog can be closed by an outside request. Still a managed event, still no Windows hook.
4. **Two new file names,** only when Start Fresh is pressed on accounts that cannot be read:
   `accounts.dat.corrupt-<time>` and `accounts.dat.bak.corrupt-<time>` (a `-2`, `-3` suffix if the
   name is taken; `<time>` is `yyyyMMdd-HHmmss`, UTC). Same DPAPI envelope, same cookies, never read
   again and never deleted by the app. `docs/PRIVACY.md` lists both.

**The four reviewer-letter checks, run 2026-11-17 against `v1.39.0.0`** (full commands and results are
in the header of `reviewer-letter-1.40.0.0.md`):

- **Manifest:** `git diff --stat v1.39.0.0..HEAD -- src/ROROROblox.App/Package.appxmanifest`: empty.
  The version stamp is not applied yet; re-check after `finalize-store-build.ps1`.
- **`.proto`:** `git diff --stat v1.39.0.0..HEAD -- '*.proto'`: empty. Both `NativeMethods.txt`
  files: empty.
- **Input synthesis and interop** over added lines in `src/ROROROblox.App` and `src/ROROROblox.Core`
  (`.resx` excluded): **zero** for `SendInput`, `keybd_event`, `mouse_event`, `SetWindowsHookEx`,
  `SendMessage`, `PostMessage`, `WriteProcessMemory`, `OpenProcess`, `DllImport`, `LibraryImport`,
  `RegisterRawInputDevices`, `SetWinEventHook`. In the **test project** the added lines do declare
  `PostMessageW` through `DllImport` and post `WM_CLOSE` and `WM_SYSCOMMAND` to the tests' own
  windows; it does not ship. **A grep of the whole tree, not the diff,** also finds
  `PInvoke.PostMessage` in `RobloxProcessTracker.RequestClose` (a polite close request to a Roblox
  window the app launched, for Stop and Recycle). It is in v1.39.0.0 and this release edits only that
  method's `catch`; the letter says so.
- **New URLs and hosts** in added lines: one, the `ms-windows-store://` URI. No new `http(s)` host.
  **Registry** in added lines: no new key or value; the existing `roblox-rororo:` write, the Discord
  scheme fix-up and run-on-login are skipped under the dev-build folder override.
- **UI strings:** 17 new in `Strings.resx` and the six culture files (5 update button, 10 About-page
  shortcut list, 2 messages). The manifest's languages are unchanged.

## 1. Version

**1.40.0.0, confirmed by the owner on 2026-11-17** ("1.40.0.0 it is"). A MINOR bump from 1.39.0.0 by
the playbook's table: the update button is a new user-visible control. Three places must agree before
the tag: the csproj `<Version>`, `Package.appxmanifest` (both stamped by `finalize-store-build.ps1`)
and the tag.

## 2. Listing

**Listing audited against the v1.40 release notes on 2026-11-17. Short description and product
features: unchanged. Long description: one sentence added to the privacy paragraph, in all seven
sheets, and the auto-update bullet held. The added sentence is listing copy, so it is the owner's to
approve before any Store write.**

| Surface | Checked for | Outcome |
|---|---|---|
| Short description (184/200) | Whether any claim is touched | None is. Unchanged. |
| Long description, privacy paragraph | "Nothing about you leaves your machine except the Roblox-side calls during launch … RoRoRo also downloads its own signed files from its GitHub releases … and sends nothing about you to get them." | **Incomplete for a Store install** from 1.40 on, the same shape as the v1.25 case the playbook cites: the paragraph lists what goes over the network, and the Store check is not on the list. **One sentence added**, below. |
| Long description, auto-update bullet | "Auto-update via Velopack. Remote config tracks the current known-good Roblox version…" | Not untrue, and not made false by 1.40. **Held:** a line about the new button belongs here only once the Store check has been seen to work. Held text below. |
| Long description, vault bullet and the rest | "a copy of the vault file moved to another PC will not decrypt"; the DPAPI claims | Still hold. The set-aside copies are the same encrypted data, in the same folder, never sent. Unchanged. |
| Product features (20/20) | Whether the button earns an entry; whether the auto-update entry is incomplete | The field is full. The auto-update entry is the one to extend, and the same hold applies. Unchanged now. |
| Hub page (`docs/index.md`) | "Auto-update that survives Roblox renames", "auto-updates through the Store", "No telemetry" | All hold. Unchanged. |

**Added to the end of the privacy paragraph (English; the six sheets carry their own translations):**

> On a Store install, RoRoRo also asks the Microsoft Store, through Windows, whether an update is
> waiting, and sends nothing of its own.

**Held until the Store check is proven (English), long description bullet:**

> • Auto-update, with a button when one is waiting. The direct download updates itself and shows Quit
> to update once an update is downloaded; the Store edition updates through the Store and shows
> Update available when the Store has a newer version. Remote config tracks the current known-good
> Roblox version and mutex name, so a Roblox-side rename doesn't break you for long.

**Held, product feature entry (English, 126 characters):**

> Auto-update that keeps working when Roblox changes underneath, plus an Update available button when a newer version is waiting

### The product name

Unchanged from v1.39: the English name `RoRoRo — Multi-Instance Launcher & Account Manager` is live and
the owner holds it (`f1d0eeb`, "the owner holds the name after the third read"). A submission cloned
by the API carries the published name; the API never writes a product name.

## 3. What's new in this version

[`whats-new-1.40.0.0.md`](whats-new-1.40.0.0.md), seven blocks, each under the 1,500-character cap
however a newline is counted:

| Language | Newline as 1 | Newline as 2 |
|---|---|---|
| English | 1,152 | 1,178 |
| Deutsch | 1,381 | 1,409 |
| Français | 1,395 | 1,424 |
| Русский | 1,309 | 1,336 |
| Português (Brasil) | 1,334 | 1,361 |
| Polski | 1,295 | 1,322 |
| Español | 1,356 | 1,384 |

The headline and five bullets are new text; the two closing paragraphs in every language are
v1.36's, unchanged (compared by script against the v1.39 blocks). The six language sheets
(`listing-copy-<lang>.md`) carry the same blocks, written by the same script.

**Reworked in review, in all seven languages.** The first draft's update bullet read like a
disclosure form and carried a sentence about the direct download, which a Store listing does not
need. The bullet now says: the button appears when a newer RoRoRo is out, one click opens the Store
page, RoRoRo asks the Store through Windows after it starts and every six hours and sends nothing of
its own, and it starts working with the update after this one (a person updating to 1.40 from 1.39
did not have it). One sentence about the accounts file was awkward in every language ("goes to its
backup, never to an empty list") and now reads "is restored from its backup and is never read as
empty". The last bullet tied "dates" to the About page; dates follow the language across the app.
The third bullet says "another program", not Task Manager.

**Listing verifier:** see section 7.

## 4. Notes for certification

[`reviewer-letter-1.40.0.0.paste.txt`](reviewer-letter-1.40.0.0.paste.txt), 4,983 characters
(v1.39's was 4,681, v1.33's 4,584, v1.29's 5,781, the longest shipped). Partner Center only;
`submit_write.py` never sends it. **Replace the whole box.** The first draft was 8,175 characters;
it was cut to stay inside the length Partner Center is known to take. What was cut and what was
kept is in the letter file's header.

## 5. Age rating, privacy, screenshots

- **Age rating:** unchanged. No new content and no user-to-user communication.
- **Privacy:** `docs/PRIVACY.md` already carries both new rows: the Store query in the hosts table
  and the two set-aside names in the stored-files table. The privacy policy URL on the listing is
  unchanged. The `api.github.com` row still says a direct-download update is "applied when you next
  quit RoRoRo"; the release notes and the button's tooltip say "quit and open it again" because
  whether it applies at exit or at the next start is unmeasured. **That row's sentence predates this
  release and is stronger than the evidence;** worth softening in the same pass.
- **Screenshots:** unchanged. The button is absent in the normal state, which is what the ten
  screenshots show. A screenshot of the Update available state does not exist yet; the captures from
  the update-button run are in `docs/cart-runs/2026-11-17-update-button/captures/` and were drawn
  off screen by the rendering tests.

## 6. Packages

**Not built.** Build per the playbook, Phase 3 and 4, after the version is stamped: arm64 and x64
Store MSIX with `scripts/finalize-store-build.ps1`, and the sideload MSIX with
`scripts/build-msix.ps1 -Sideload`. Fill this table when they exist, with sizes and the signer.

| File | Size | For |
|---|---|---|
| `dist/RORORO-Store-x64-1.40.0.0.msix` | not built | Partner Center, unsigned |
| `dist/RORORO-Store-arm64-1.40.0.0.msix` | not built | Partner Center, unsigned |
| `dist/RORORO-Sideload-x64-1.40.0.0.msix` | not built | GitHub release, signed |

The sideload package's signer must be `CN=177BCE59-0966-4975-9962-10E36652141F`, SHA-1
`8232F92D70CE24B9EF2CCEB4B458F8C26D94AB87` (the unrotated dev certificate). The repo-root
`dev-cert.cer` on Dunder was a stray with a different key at v1.39; compare thumbprints before
attaching.

## 7. Verification

- **App strings.** 17 new keys in seven languages. The slice is committed
  (`docs/store/ui-translations-1.40-delta.json`, `5e0e751`). **This draft did not run the translation
  verifier on them** (the edit noted in section 1 says all 102 pairs were approved; confirm it). The stability-run report calls it a gate before any tag, and it needs a pushed
  commit.
- **Listing verifier on What's new.** Not run. Run it at the pushed commit on all six languages in
  both shapes, as for v1.39. Expect the same Rule 3 finding on the tray menu's Quit in the closing
  paragraph, which the owner overruled at v1.36 to v1.39. The new text is the headline and five
  bullets; check particularly the four disclosure and product-noun lines.
- **Tests.** `dotnet test src/ROROROblox.Tests/ -c Release --no-build` on the release branch at
  `f1d0eeb` with this documentation drafted: **3,986 passed, 12 skipped, 0 failed** (1 m 30 s,
  2026-11-17). The 27 harness tests were not re-run in this drafting session; the update-button
  report records 3,986 unit and 27 harness passing at its end. `ci.yml` is not recorded here; check
  it before merging.
- **Live runs, 2026-11-17** (dev builds on a test data folder, `RORORO_DATA_DIR`, the real data
  folder and five registry values showing 0 differences before and after, from
  `docs/cart-runs/2026-11-17-stability-part-two/report.md` and `2026-11-16-data-folder-override`):
  - one `taskkill` (no `/F`) with the leftover-clients dialog up: exit 0.36 s, startup abandoned;
  - with the first-run tour up: exit 0.35 s, and earlier 0.39 s and 0.33 s;
  - four wrong-shape files (favorites, session history, private servers, session stats): app
    started, one warning per file, exit 0.35 s;
  - `accounts.dat` of eight junk bytes, Start Fresh pressed: set aside as `accounts.dat.corrupt-<time>`
    with its bytes intact, app carried on, exit 0.34 s;
  - exit timing: 2.5 s with the "DisposeAsync did not complete within 2s" line before the exit fix,
    0.41 s and 0.58 s after, with no such line (two of two).
- **Tests only, not seen live:** the already-running dialog (needs a Roblox client that holds the
  singleton name), the expired-account alert, the failed-launch row, the salvage of a file with one
  bad row, the shortcut list in another language, a Start Fresh that cannot move a file, a Start Fresh
  over a vault that turns out to be readable (the last two on real DPAPI and real files), the
  direct-download "Quit to update" button (needs a Velopack install with an update staged), and the
  Store query's failure handling. **Task Manager's End task was not measured in these runs; only
  `taskkill`.** (v1.39's smoke saw End task quit RoRoRo once.)
- **Seen by hand:** the Store link opened the Microsoft Store app.
- **Not exercised, cannot be before release:** the Store update query on a Store-installed build.
- **Review caught five of seven built items changing** in the stability run before they landed
  (the raw exception message on a row, a vault rejected over one bad row, a Windows shutdown that
  would have paged for every expired account, a Start Fresh that set aside a healthy vault, and an
  outside-close fix that missed the reported case), plus a direct-download tooltip that claimed when
  the update installs. See the two reports.
- **Pre-release agents:** not run in this drafting session (the cookie-exposure audit over
  `git diff v1.39.0.0..HEAD -- src`, the MSIX asset review, the plugin-catalog drift check
  `scripts/check-plugin-catalog-drift.ps1`). Run them before the tag.

**Not verified, and said so in the release notes:** the Store query on a real install; the
direct-download button; whether a staged update applies at exit or at the next start; and, carried,
the backup rescuing a real damaged file, the crash line after a real crash, the v1.38 launch messages
against a live Roblox failure, the launch check on a PC whose only Roblox is the Store edition, the
rating line on a real Store install, and the 75/100/180 caps on a live client.

## 8. Shipped

**Nothing has shipped.** Filled in as each step happens.

| Step | When | Evidence |
|---|---|---|
| Version confirmed and stamped | | |
| Translation verifier and listing verifier | | |
| PR merged | | |
| Store submission created and verified | | |
| Owner's two pages (packages, reviewer letter) | | |
| Committed | | |
| Certified | | |
| Tag pushed, GitHub release drafted | | |
| Live on the Store | | |
| GitHub release published as Latest | | |

**Before the first row:** the update-button report's open items (the owner's look at the captures
against the placement he picked), the PR for `fix/stability-part-two`, and the name-test decision
already recorded in `f1d0eeb`.
