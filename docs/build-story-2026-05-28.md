# Build Story — ROROROblox — 2026-05-28

**Competitive recon by mid-afternoon, a shipped v1.7.0.0 by evening.** A parity-and-outdo plan became multi-instance support, a caught startup crash, and a CI gate — eight commits between 17:18 and 19:40 CT, on `main`.

## The shape of the day

Two sessions, afternoon into evening. The spine titles them *"Add account tags and fix launch all feature"* and *"Research Roblox multi-launcher alternatives"* — so the working intent was broader than what landed in git. The early stretch ran heavy on discussion (~1,400 prompts) and committed nothing; the commit arc starts at 17:18 with the competitive plan and runs clean through to the release. The story below is what actually shipped on the 28th — not everything that was talked through.

## The arc

**Name the plan (17:18).** `docs(competitive)`: parity + outdo plan, a mutex-name spec, and a drift banner (586 lines). The recon written down as intent — copy this, beat that.

**Close the drift (18:04).** Config-driven singleton mutex name, closing "spec 7.1 drift" — 434 insertions across 9 files. The single-instance gate stopped being hardcoded and started being configurable, which is the foundation the multi-instance lane needs.

**Catch the crash (18:05).** `fix(launch)`: a `RobloxUpdateProbe` typed-client constructor ambiguity — flagged in the commit as a *pre-existing v1.7 startup crash*. Already sitting in the tree, waiting for the version bump to make it real.

**Build the headline feature (18:30).** `feat(tray)`: stop-all-instances + reload-on-error recovery — the multi-instance lane, 284 insertions across 8 files.

**String the net before the drop (18:35 → 18:56).** `NoOpLauncher` brought into conformance (`RequestLaunchTargetAsync` + `GetCurrentServerAsync`), then a full-solution test gate wired to run on push and PR. Publish `roblox-compat.json` and auto-upload it on every release.

**Ship (19:40).** `chore(release)`: bump to **v1.7.0.0**.

## The momentous hurdle

**The crash that almost rode the release out the door.** The `RobloxUpdateProbe` typed-client constructor ambiguity would have crashed v1.7 on startup. It wasn't introduced that day — it was already there. The competitive/parity pass is what flushed it out, hours before the version bump would have shipped it to everyone. The story could have been "v1.7 crashes on launch." Instead the fix landed at 18:05 and the story is the multi-instance feature.

## Method and cadence

Recon → spec → implement parity features → harden → release, in that order, in one sitting. The tell is the timing: the full-solution CI gate landed at 18:56, *before* the v1.7.0.0 bump at 19:40 — the version didn't ship until a test gate was standing guard over the whole solution. Multi-instance support — configurable mutex name, stop-all-instances, reload-on-error recovery — is the headline the day was built around.

Two and a half hours of committing, but the afternoon's competitive read is what aimed them.

---
*Generated via Vibe Insights build-story mode — grounded in the canonical spine (`~/.vibe-insights/story-input.md`: scoped session timeline + repo git log; 0 decisions, so no Decisions-section cross-attribution). Scoped to 2026-05-28 Central. Engine: the bundled vibe-insights plugin 0.1.0.*
