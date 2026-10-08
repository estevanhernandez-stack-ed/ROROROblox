# Submission packet — v1.33.0.0

Everything Partner Center asks for on this submission, in the order it asks. Certification last saw
**v1.32.1.0**, so this is a single version's delta — and a wide one: the alerts cycle, twelve built
items, 133 files, +12,670/−1,300.

> **DRAFTED 2026-10-07, not yet submitted.** Three things are open before the owner's submit click,
> each stated rather than papered over: the translation verifier has not run (section 3), the
> clean-VM smoke has not been done (section 7), and one screenshot is now one release stale
> (section 5). None of the three is a blocker on its own; the ruling is the owner's.

---

## Read this first — one plugin-facing addition, and nothing else new to assess

A reviewer looking for new powers will not find any. Everything in this release is about
notifications the app already sent and where they go, plus one environment variable on plugin
launches.

**The one addition:** optional plugin programs the user installs and consents to are now launched
with `RORORO_LAUNCH_REASON` in their environment, whose value is one of `autostart`, `manual`,
`install`, `update`, `restart`. It lets a plugin distinguish a start the user asked for from one that
happened on its own. An environment variable rather than a command-line argument deliberately, so a
plugin that does not read it is completely unaffected.

Verified against `git diff v1.32.1.0..HEAD`, not assumed:

- **133 files changed, +12,670/−1,300; 92 of them under `src/`.**
- **`Package.appxmanifest`: the only change is the `Version` attribute.** Same identity, same
  publisher, same `runFullTrust`, same languages, same protocols, same startup task.
- **`plugin_contract.proto`: byte for byte unchanged.** No capability, no consent prompt, no RPC
  added; contract version untouched.
- **No input synthesis and no new interop.** Across every added line under `src/`, the combined
  count of `SendInput`, `keybd_event`, `mouse_event`, `SetWindowsHookEx`, `PostMessage`,
  `SendMessage(`, `WriteProcessMemory`, `OpenProcess`, `DllImport` and `LibraryImport` is **zero**.
- **No new network address and no new network call.** Seven URL strings appear in added lines and
  none is an address the app contacts: four are XML namespace declarations (two XAML, the SVG
  namespace, and a C2PA attribute carried in from the brand wordmark's own export) and three are
  obviously-fake Discord webhook fixtures inside unit tests. The two SVG files carrying namespace
  declarations are design sources — present in the repository only, and in neither the build output
  nor the package, which was checked rather than assumed.
- **No new permission, and no change to what the app stores or where.**

The macro wall is untouched: the Store binary still synthesizes no input and injects into nothing.
The new environment variable is read by consented out-of-process plugins, which is what the plugin
system exists for.

## 1. Packages

| File | Size | Architecture |
|---|---|---|
| `dist/RORORO-Store-x64-1.33.0.0.msix` | 106.89 MB | x64 |
| `dist/RORORO-Store-arm64-1.33.0.0.msix` | 100.71 MB | arm64 |

Both unsigned — Partner Center signs after upload. Ship **both**.

Identity: `626LabsLLC.RoRoRoBlox`, publisher `CN=177BCE59-0966-4975-9962-10E36652141F`, display name
`626Labs LLC`. Version `1.33.0.0`, fourth component zero as the Store requires. Both built by
`scripts/finalize-store-build.ps1`, which stamps the csproj and the manifest rather than leaving it
to hand-editing.

Sideload build for the clan: `dist/RORORO-Sideload-x64-1.33.0.0.msix` (106.92 MB), signed with
`dev-cert.pfx`. Signer subject reads `CN=177BCE59-0966-4975-9962-10E36652141F`, equal to the packed
manifest's `Publisher`, which is the check that matters. `Get-AuthenticodeSignature` reports
`UnknownError` on this machine, which is the expected result for a self-signed package whose root is
not in the local trust store — testers import `dev-cert.cer` into Local Machine \ Trusted People
first, as they always have. **No cert rotation this release**, so nobody needs to re-import anything.

## 2. Listing

**Audited across all four surfaces, and two findings. Not unchanged.**

**The standing trip-wire closed rather than fired.** v1.32.1's packet found the long description's
"route each alert to any mix of desktop notifications, a Discord webhook you create, and your phone"
was *incomplete* — memory warnings raised a desktop balloon whatever the user ticked — and ruled: do
not edit the listing, fix the behaviour, and soften the sentence only if the alerts cycle slipped
past the next submission. **It did not slip.** The claim is now true as written, which is the
outcome that ruling was betting on.

**Finding 1 — the alert-kind enumeration was short, in seven languages.** Beside that claim, the
long description lists which alerts exist, and idle was missing from the list although it is a
routable kind. The English sheet and the hub page were corrected in `1c84d28`; **the six
per-language sheets were not**, so the translated listings enumerated the kinds without idle while
the English one did. Closed rather than noted: all six long descriptions are now at parity, and each
one's word for idle is lifted from that culture's shipped `SettingsPage_AnAccountGoesIdle` rather
than translated fresh, so the listing names what that user actually sees on screen. Both halves also
gained the sentence naming the two new controls.

This is the same divergence class v1.27 caught as a *security* regression, when the account-export
warning was stronger in English than in four other languages. The lesson recorded: editing the
English long description is a seven-file job, not a one-file job, and nothing in the repository
enforces that today.

**Finding 2 — the screenshots. One is now a release stale; see section 5.**

The short description, the product features and `docs/index.md` need no further edit: this release
adds no capability and removes no claim beyond what is above.

## 3. What's new in this version

Blocks: [`whats-new-1.33.0.0.md`](whats-new-1.33.0.0.md) — English plus six translations, each
stepped into its own per-language sheet, because **the listing console reads the sheets, not the
version file.** That was v1.32's near-miss, so it is checked and recorded rather than trusted: all
six sheets open their What's-new block with `v1.33.0.0`, and the English sheet's pointer — which is
hand-maintained and had previously gone six versions stale — now names this version.

Every block is well inside the 1500-character limit: English 1,154, German 1,328, French 1,334,
Russian 1,265, Portuguese 1,227, Polish 1,219, Spanish 1,244.

**One deliberate departure from v1.32.1's house rule, stated so it is not mistaken for drift.** That
release's field said what a setting does now and never that it had been wrong, because the defect
was a *mechanism* — read once at startup — that a Store field has no business carrying. Here the
first bullet does say memory warnings used to appear whatever the user picked, because that is an
**outcome** the reader needs: without it, "alerts obey the grid" restates what the listing already
promised, and the person whose unticked Desktop box never worked is told nothing. The mechanism —
three separate code paths to the screen — still stays out. The clan-facing notes carry it.

**Verifier run: NOT RUN.** Stated rather than assumed. Every quoted UI label in every block names
the resx key it came from (`SettingsPage_Desktop`, `_HowOftenAtMost`, `_EveryTime`, `_AlertSound`,
`_HearIt`, `_AnAccountGoesIdle`, `_Appearance`), specifically so that the expected rule-3 overrule is
checkable rather than asserted. If the run repeats the false positive it drew twelve times on v1.32
and three times on v1.32.1 — "in-app UI strings must remain untranslated in English", a rule that
assumes an English-only interface this app has not had since v1.26 — it is overruled on the same
ground. Filed upstream as
[translation-verification#27](https://github.com/estevanhernandez-stack-ed/translation-verification/issues/27).

The three alert-sound choices are **described rather than quoted** in every block: their option names
are built by a converter rather than held as resx values, so quoting them would mean re-translating,
which is the one thing that file does not do.

## 4. Notes for certification

Reviewer letter: [`reviewer-letter-1.33.0.0.paste.txt`](reviewer-letter-1.33.0.0.paste.txt). It leads
on `RORORO_LAUNCH_REASON` rather than letting a reviewer discover it, and every "nothing moved"
claim in it is the measured one from the top of this packet.

No new capability, no new network endpoint, no new permission, no change to data handling. The
privacy policy is unchanged and still accurate.

## 5. Age rating, privacy, screenshots

Age rating and privacy: **unchanged** from v1.32.1.0. No new content, no new data collection.

**Screenshots: one is stale, and it is a judgement call rather than a blocker.** Ten images, and this
release visibly changes two surfaces. The Settings → Alerts section went from three cards to one
section — and **no screenshot shows Settings at all**, so nothing there needs retaking. But
`03-about.png` was captured 2026-08-30 and v1.33 item 10 changed the About page: it now carries the
626 Labs wordmark at its foot and the easter egg behind the version number.

**The caption is not wrong, which is why this is not a blocker.** Image 3's caption talks about the
mutex and the clean reimplementation, not about the page's furniture, so the listing makes no claim
the old capture contradicts — the image is merely a release behind on one page's decoration.
**Recommendation: retake `03-about.png` before submitting if it is convenient, and ship without it
if it is not.** Flagged rather than silently skipped, because "the screenshots are unchanged" would
have been the easy and slightly false line.

## 6. GitHub release

Tag `v1.33.0.0` fires `release.yml`, which builds, tests, runs Velopack and drafts the release with
`rororo-win-Setup.exe`, the full and delta nupkgs and `releases.win.json`, then signs and attaches
`roblox-compat.json` + `.sig` and `plugins-catalog.json`. The sideload MSIX and `dev-cert.cer` are
attached by hand in Phase 6. The body is the block between the `---` markers in
[`release-notes-1.33.0.0.md`](release-notes-1.33.0.0.md).

**`plugins-catalog.json` was two releases stale and the tag would have published it a third time.**
The catalog is bumped by hand and re-attached on every tag push, so a stale row is actively
republished rather than merely out of date. Four of five entries matched their live releases; the
fifth, `626labs.ur-mcp`, read `0.1.0` while `rororo-ur-mcp` has been published at `v0.2.0` since
2026-09-28 — verified not a draft, not a pre-release, and carrying `manifest.json`,
`manifest.sha256` and `plugin.zip`. That means v1.32.0.0 and v1.32.1.0 both shipped a catalog that
under-reported Ur MCP. **Bumped to `0.2.0` before this tag.** All five rows now equal their live
releases:

| Entry | Catalog | Live release |
|---|---|---|
| `626labs.ur-task` | 0.9.1 | v0.9.1 |
| `626labs.ur-ocr` | 0.4.0 | v0.4.0 |
| `626labs.ur-afk` | 0.5.2 | v0.5.2 |
| `626labs.ur-mcp` | **0.2.0** | v0.2.0 |
| `626labs.ur-score` | 0.7.0 | v0.7.0 |

v1.32.1's packet recorded this exact hazard for Ur Score and caught it that time; the same by-hand
check then missed a different row. A per-release eyeball across five plugins in five repositories
does not scale, and everything needed to automate it is available from the `gh` CLI. Logged as a
decision with that as the next step.

## 7. Verification

- **Full solution, Release, on this tree at `1.33.0.0`:** 2,804 unit + 27 harness pass, 1 harness
  skip by design. Built fresh rather than `--no-build`, because the csproj version moved.
- **CI on `main`:** green at `190c099`, the commit this release is cut from, across x64, native
  arm64 and the secret-scan / local-path guard.
- **Ten-run live smoke on real accounts**, 2026-10-07, written up in
  [`smoke-2026-10-07-alerts-cycle.md`](../smoke-2026-10-07-alerts-cycle.md). Nine of ten runs pass;
  run 4 failed, was fixed, and the fix is in this build. **The smoke produced nine findings of its
  own**, two of which were closed in-cycle — the alert sound had no way to be previewed from
  Settings at all, and the Store build never listed installable plugins — and the rest are recorded
  as known issues or backlog rather than left in a transcript.
- **Two of the three behaviours this release is mostly about were confirmed on screen, not only by
  test:** with Desktop unticked, a two-account crossing logged "routed nowhere" and nothing
  appeared; and driving `TogglePattern.Toggle()` directly — the screen-reader path, not a mouse —
  now moves the UI state and `settings.json` together.
- **Clean-VM smoke: NOT DONE.** The playbook asks for a manual smoke on a clean VM and no VM was
  available, the same gap v1.32.1 shipped with. The live verification above covers the behaviour
  this release changes, but not a fresh install on an untouched machine. Stated so the owner can
  decide whether it blocks the submit click.

## 8. Shipped

*Not yet. This section gets the submission id, the timings and the `inspect` read-back once the
owner has clicked submit, in the same shape as v1.32.1's.*
