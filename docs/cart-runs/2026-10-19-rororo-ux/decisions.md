# Decisions: rororo-ux

## D1
- **Decision:** Cut from `origin/main` (`c4f2acb`, v1.33.0.0) through the detached worktree `~\.cwt\rororo-main`; Este's checkout stays on `docs/localization-wave-2` untouched.
- **Why:** The checkout is 184 behind main with two dirty files; building there would repeat the PR #173 trap.
- **Alternative not taken:** Running from the checkout.
- **Item:** run-wide
- **How to reverse:** `git worktree remove` after cleanup.

## D2
- **Decision:** Harvest closed two of the nine UX rows as shipped in v1.33 (alert-sound "Hear it" button; Settings nav renamed to Language and Appearance) and grouped the remaining seven findings plus hygiene into seven items instead of one item per task.
- **Why:** The ledger line for v1.33 and the tree both carry the two fixes; the dashboard rows were filed mid-cycle on 10-07 and never closed. Grouping by file surface (idle settings trio, disclosure copy trio, catalog pair) keeps each worker inside one area and halves the number of full-solution builds, which are the slow part here.
- **Alternative not taken:** One item per dashboard task (twelve builds).
- **Item:** harvest
- **How to reverse:** n/a.

## D3
- **Decision:** host_info build identity (item 4) is `hold` because it changes the PluginContract proto and bumps the contract package version; the metric-alert item (6) is `hold` because it is a diagnosis first.
- **Why:** The contract is a published NuGet surface consumed by five plugin repos; additive or not, Este sees the version bump before it merges. A diagnosis that turns into a scoping filter changes what alerts say; that is his call too.
- **Alternative not taken:** `go` on both.
- **Item:** 4, 6
- **How to reverse:** n/a.

## D4
- **Decision:** The plugin-audio item ships only its honest-wording half; the host-level mute that plugins obey is out of scope.
- **Why:** A mute the plugins respect is a contract field plus five plugin repos; the user-facing lie ("Silent" that is not silent) is fixed by one sentence under the picker today.
- **Alternative not taken:** Contract-level mute in this run.
- **Item:** 2
- **How to reverse:** n/a.

## D5
- **Decision:** The catalog drift check fails the release workflow on disagreement, instead of warning like the hub's copy.
- **Why:** The hub's warning has existed since 10-08 and the catalog went stale again four days later; a warning in an Actions log nobody reads is not a gate.
- **Alternative not taken:** Warning only.
- **Item:** 3
- **How to reverse:** Change the exit code.

## D6
- **Decision:** host_info tells store from sideload by `Package.Current.SignatureKind` (Store = "store", any other packaged kind = "sideload", no identity = "dev"), because package name, publisher CN and family name are identical across the two MSIX channels (the sideload cert carries the Store CN).
- **Why:** Nothing in the manifest differs between `finalize-store-build.ps1` and `build-msix.ps1 -Sideload`; who signed the package is the only remaining signal. Unit-tested with the probe injected; the real kinds on a Store and a sideload install are unverified until Este reads host_info on each.
- **Alternative not taken:** Reporting "packaged" for both (honest but useless for the false-green case that filed the task).
- **Item:** 4
- **How to reverse:** One mapping function.

## D7
- **Decision:** Metric-alert scoping (item 6) stays docs-only. The alert names every saved account Ur Score reports for, by design on both sides; the fix is a product call (running-only or all saved) plus wording, not a one-line filter.
- **Why:** The worker traced the path end to end (Ur Score's allow list is the saved-accounts list; the host coordinator has no filter and cannot see the running set; the wording never claims "running"). A Rate rule firing on an idle saved account is the rule working as written. Changing what an alert claims without Este's call would be guessing the product.
- **Alternative not taken:** Wording-only tweak now.
- **Item:** 6
- **How to reverse:** n/a.

## D8
- **Decision:** The per-sample ActivityMonitor line is Trace, not Debug (31f9686 on the integration branch, after item 1 merged).
- **Why:** The list asked for "a Debug line per sample" (the dashboard task's words) and the worker delivered it; the orchestrator checked `AppLogging.cs` and the app's Serilog floor is Debug, so that line would have written about 86,000 lines a day. The crossing line at Information is the diagnostic the task wanted; the per-sample line is for a Trace session.
- **Alternative not taken:** Throttling the Debug line to one per minute (still noise at the Debug floor).
- **Item:** 1
- **How to reverse:** One log level.

## D9
- **Decision:** Item 5 aligned all eight App-side User-Agent sites to the package-aware version, not just the two the follow-up named; the Core-side reads stay on the assembly version.
- **Why:** The goal was one version string per packaged install; two aligned and six not would have shipped the inconsistency the fix exists to remove. Core cannot see the App project, so its three reads are a separate change if anyone wants them.
- **Alternative not taken:** The narrow two-site change.
- **Item:** 5
- **How to reverse:** n/a.

## D10
- **Decision:** `docs/decisions.md` conflicts between items are resolved by keeping both entries in merge order; the run does not reorder or rewrite entries, only corrects a sentence that the same run made stale (item 5's "two headers" after the eight-site follow-up).
- **Why:** The file is append-only by the keystone's rule; two workers appending on sibling branches is the expected conflict and has one right answer.
- **Alternative not taken:** Serialising every item that touches decisions.md.
- **Item:** close-out
- **How to reverse:** n/a.

## D11
- **Decision:** New UI strings go through `docs/store/translations/ui-<culture>.json` and `scripts/gen-culture-resx.py`; a worker that edits a culture resx by hand gets its keys backfilled into the catalogs on the integration branch (item 2, commit on `cart/rororo-ux` after 07aa590) and item 7's brief was corrected mid-run.
- **Why:** The six culture resx files are generated; `CultureKeyParityFenceTests` only compares resx to resx, so a hand-edited resx passes the fence and silently loses its keys on the next regeneration. Found while preparing the translation-verifier spot-review. Regeneration reproduced the worker's resx byte for byte, so only the catalogs changed.
- **Alternative not taken:** A fence that compares the catalogs to the resx (worth adding; filed in the report as a follow-up, not built in this run).
- **Item:** 2, 7
- **How to reverse:** n/a.

## D12
- **Decision:** The translation-verifier spot-review on the 15 new keys is handed to Este as a pre-tag step, not run by the orchestrator.
- **Why:** The verifier ingests a dataset by URL; hosting a 15-key slice needed a gist upload, which the auto-mode guard blocks for the agent as a public data-sharing action. Working around that would bypass the guard's intent. The exact command sequence is in the PR body and the report.
- **Alternative not taken:** Committing the slice into the repo to get a raw URL (pollutes the tree for a one-off).
- **Item:** 2, 7
- **How to reverse:** n/a.

## D13
- **Decision:** After merging item 7, the six catalogs held each item 2 key twice (both branches appended the same three keys with identical values; git merged both hunks). Dropped the duplicates (81f2ccf) after asserting the values matched; the rendered resx was unaffected because `json.loads` keeps the last copy.
- **Why:** A duplicate JSON key is a latent trap for the next tool that reads the catalogs strictly.
- **Alternative not taken:** Leaving them (invisible today).
- **Item:** close-out
- **How to reverse:** n/a.

## D14
- **Decision:** Item 6 became a build after Este's ruling (running-only naming; Rate rule; clan thresholds cross on other players). Triggers are marked, not dropped, so rule inputs, Rate series, per-account mute and cooldown stay as they were; the empty-case sentence was reworded before push so it is true for a Rate rule on an idle alt as well as a clan-wide threshold.
- **Why:** Dropping rows would change verdicts and leave Rate gaps; the worker's first empty-case line blamed other players in every case.
- **Alternative not taken:** Filtering at the coordinator (changes verdicts); wording-only (names the wrong machine).
- **Item:** 6
- **How to reverse:** The mark is one bool on `AlertTrigger`.
