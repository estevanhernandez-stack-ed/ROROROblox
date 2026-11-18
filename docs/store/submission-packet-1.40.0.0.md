# Submission packet — v1.40.0.0

> **SHIPPED 2026-11-18, both channels.** Live on the Store between 17:05:43 and 17:09:44 CST and
> GitHub Latest at 17:27:40 CST; section 8 has every step and its evidence. Drafted 2026-11-17 on
> `release/1.40.0.0`; this banner said "PREPARED, NOT SHIPPED" until then.

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
sheets, and the auto-update bullet held. **The owner approved the added sentence on 2026-11-17**, and
asked for three French sentences to be fixed (below).**

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

Built 2026-11-17 14:07 to 14:08 CST on Dunder from `release/1.40.0.0`, with
`scripts/finalize-store-build.ps1` (arm64, then x64) and `scripts/build-msix.ps1 -Sideload`. The
tree is main's code (`db8f3ec`) plus the release documents and the version stamp. **If any code
changes again, rebuild all three.**

| File | Size | SHA-256 | For |
|---|---|---|---|
| `dist/RORORO-Store-x64-1.40.0.0.msix` | 112,448,575 bytes | `A364733463FDE55071329657A4C859E2EEE828BFACE266476B277D7B7C1A7EAB` | Partner Center, unsigned |
| `dist/RORORO-Store-arm64-1.40.0.0.msix` | 105,956,063 bytes | `13C745C881E2D5EB16E6039E8AFF8B4B50BEBAAAEE39A5A73D80E8970480C62F` | Partner Center, unsigned |
| `dist/RORORO-Sideload-x64-1.40.0.0.msix` | 112,471,201 bytes | `BEBF94930A6C11876BA753B9EB75D9015D63A1FAF26EAF74E8796D3AA9353169` | GitHub release, signed |

Read out of each Store package's `AppxManifest.xml`: `Name="626LabsLLC.RoRoRoBlox"`,
`Publisher="CN=177BCE59-0966-4975-9962-10E36652141F"`, `Version="1.40.0.0"`, and
`ProcessorArchitecture` `x64` and `arm64` respectively. The source manifest's diff against
`v1.39.0.0` is the Version attribute and nothing else, as the reviewer letter says.

The sideload package's signer is SHA-1 `8232F92D70CE24B9EF2CCEB4B458F8C26D94AB87`, the unrotated dev
certificate. `Get-AuthenticodeSignature` reports its status as `UnknownError` on this PC, which is
what a self-signed certificate that is not in the machine's trusted store reads as; the thumbprint is
the check. The repo-root `dev-cert.cer` on Dunder was a stray with a different key at v1.39; attach
the copy from the v1.37.0.0 release, and compare thumbprints first.

**The first build attempt failed, and why.** At 14:05 the arm64 publish died in `ResolvePackageAssets`
with a `NullReferenceException`. The app project's `obj/project.assets.json` had been rewritten in the
same second without the `win-arm64` target, while the two library projects kept theirs. The version
stamp edits the csproj, and the editor's background restore shares `obj` with command-line builds; it
most likely overwrote the publish's restore. Re-run with the csproj already stamped, all three built.
If it recurs, re-run; if it recurs twice, close the editor for the build.

## 7. Verification

- **App strings.** 17 new keys in seven languages
  (`docs/store/ui-translations-1.40-delta.json`, `5e0e751`). Translation verifier, both shapes,
  2026-11-17: all 102 pairs judged, 97 clean. Five flagged, all `DpapiCorruptWindow_CouldNotSetAside`
  (fr, ru, pt-br, pl, es), all Rule 3: the verifier wants the button name left in English because it
  believes the app's interface is English. Checked against the catalogs: in all six languages the
  sentence names the button exactly as `DpapiCorruptWindow_StartFresh` labels it. No string changed.
  **Approval recorded on the owner's word:** 102 of 102, all six languages ready.
- **Listing verifier.** `docs/store/listing-translations-1.40-delta.json` at `1894f47`, two fields
  (What's new and the long description, whose privacy paragraph gained one sentence), both shapes,
  2026-11-17. All 12 pairs judged.
  - **What's new, six languages:** only Rule 3 findings, the same class the owner overruled at v1.36
    to v1.39: the tray menu's Quit, the Start Fresh button, the About page and (Spanish) the update
    button are named in the reader's language, as the localized app labels them. Each name was
    checked against that language's catalog. Nothing substantive.
  - **Long description:** German, Russian, Portuguese, Polish and Spanish approved, the new privacy
    sentence included. **French flagged, on sentences this release did not touch**, and differently on
    two runs: "si vous décrochez du jeu" for "if the game drops", a trademark line without "déposé",
    and a missing "en même temps". All three were published text from earlier releases. **Fixed on the
    owner's word, 2026-11-17:** "si vous êtes déconnecté du jeu", "en même temps sur un même PC", and
    "Le terme déposé". The sheet's separate French "Trademark info" block carried the same sentence
    without "déposé" and was fixed with it. That block is not a Store field: Partner Center has one
    copyright-and-trademark field and the console sends the copyright block only, so the Store still
    changes in two fields per listing (the console's plan: 20 fields, description and releaseNotes in
    ten listings). Re-verified at `8d74ab4`: the French trademark block approved, and the French long
    description and What's new carry only Rule 3 flags.
  - **A first run was invalid and is not counted.** `export-listing-translations.py` still pointed at
    the v1.39 English block (`WHATS_NEW_VERSION`), so the verifier compared the 1.40 translations
    against the wrong source and flagged all six. The constant moves with every release; fixed in
    `1894f47` and re-run.
  - **Rule 3 on What's new: overruled by the owner, 2026-11-17,** as at v1.36 to v1.39. The app's
    interface is localized, so its buttons are named as each language shows them.
  - **Approval:** the owner approved the listing copy on 2026-11-17 (the privacy sentence, the Rule 3
    overrule, the French fixes). Recorded in the verifier after the French re-verify.
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
- **Pre-release checks, 2026-11-17:**
  - *Plugin catalog drift* (`scripts/check-plugin-catalog-drift.ps1`): exit 0, five entries match
    live releases, tags and manifests.
  - *Store assets* (the `msix-asset-reviewer` agent): pass. All seven manifest references resolve to
    real art with every scale variant; the manifest, logos, graphics and screenshots are byte for
    byte v1.39's.
  - *Cookie exposure* (the `dpapi-cookie-blast-radius` agent, over `git diff v1.39.0.0..HEAD -- src`):
    no cookie, decrypted JSON or auth ticket reaches any log, screen, alert or support bundle. Three
    low findings, none a cookie leak: (1) the ignore list did not cover `accounts.dat.corrupt-*` and
    `accounts.dat.bak.corrupt-*`, **fixed** in `1894f47`; (2) a failed store read logs the whole
    exception, whose message can carry a path with the Windows user name, into the log file and so
    into a support bundle, which already prints the data path: left, backlog; (3) the bundle's
    redaction matches the cookie's name, not its shape, and nothing new depends on it: left, already
    a dashboard task.

**Not verified, and said so in the release notes:** the Store query on a real install; the
direct-download button; whether a staged update applies at exit or at the next start; and, carried,
the backup rescuing a real damaged file, the crash line after a real crash, the v1.38 launch messages
against a live Roblox failure, the launch check on a PC whose only Roblox is the Store edition, the
rating line on a real Store install, and the 75/100/180 caps on a live client.

## 8. Shipped

**Shipped on both channels, 2026-11-18.** Times are CST, on 2026-11-17 unless the row names the date.

| Step | When | Evidence |
|---|---|---|
| Version confirmed and stamped | 13:31 confirmed ("1.40.0.0 it is"); 14:08 stamped and built | `7ab45c7`; packages in section 6 |
| Translation verifier and listing verifier | 13:32 app strings approved, 102 of 102; 14:24 listing re-verified after the French fixes | section 7; `5e0e751`, `8d74ab4` |
| PR merged | 13:33 #295 (code), `db8f3ec`; 14:35 #296 (documents and stamp), `28c8f9a` | All four CI checks green on each head |
| Store submission created and verified | 14:36, on the owner's word ("do the store write once 296 is merged") | Submission `1152921505702432520`, `PendingCommit`. `apply`: 20 fields written and read back (description and releaseNotes, ten listings). `captions`: already matched. `verify`: 180 fields and captions ok. Console pinned at `8d74ab4`, whose listing files are identical to main's |
| Owner's two pages (packages, reviewer letter) | 14:42 ("my side is done") | A verify straight after read 180 of 180 fields and captions still matching, `PendingCommit`: the text survived the visit |
| Committed | 14:43:42, from the console, on the owner's word | `commit` returned `CommitStarted` |
| In certification, packages confirmed | 14:47:29, about four minutes after the commit | Status `Certification`. The API now names `RORORO-Store-arm64-1.40.0.0.msix` and `RORORO-Store-x64-1.40.0.0.msix`, both `1.40.0.0`, `Uploaded`: the owner's upload, seen for the first time. 180 fields and captions still match |
| Certified | 2026-11-18, before 17:03 CST; the exact time was not observed | The owner reported it published at 17:03. The status watcher from 2026-11-17 did not survive the session, so the moment certification passed is not on record. Committed 14:43 on 11-17, so certification plus the publish took at most about 26 hours |
| Live on the Store | 2026-11-18, between 17:05:43 and 17:09:44 CST | The public catalog served `626LabsLLC.RoRoRoBlox_1.39.0.0` at 17:05:43 and `1.40.0.0` (x64 and arm64) at 17:09:44. The API read `Certification` at 17:05 and `Published` at 17:10: it ran a few minutes behind the owner's Partner Center, as before |
| Tag pushed, GitHub release drafted | 2026-11-18, 17:19:49, on the owner's word ("Push and publish") | Tag `v1.40.0.0` on `28c8f9a`. `release.yml` run `44419788454` finished green at 17:26:04 and drafted one release with 11 assets. The sideload MSIX (112,471,201 bytes) and the saved `dev-cert.cer` (832 bytes, thumbprint `8232F92D…`, the package's signer; not the stray in the repo root) were attached by hand, and the notes set from `release-notes-1.40.0.0.md`: 13 assets |
| GitHub release published as Latest | 2026-11-18, 17:27:40 | `gh release list` reads Latest, the releases API names `v1.40.0.0` as the latest, and `releases/latest/download/roblox-compat.json` answers 200 |

**Before the first row:** the update-button report's open items (the owner's look at the captures
against the placement he picked), the PR for `fix/stability-part-two`, and the name-test decision
already recorded in `f1d0eeb`.
