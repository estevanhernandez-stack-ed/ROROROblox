# Builder profile — ROROROblox v1.18 cycle (Settings becomes a place)

Cart cycle #4 on this repo. Full unified profile at `~/.claude/profiles/builder.json`. Cycle-relevant
excerpt:

- **Builder:** Estevan ("Mr. Solo Dolo")
- **Experience level:** experienced (.NET 10 LTS, WPF, MSIX, C#, Win32 interop, WebView2, gRPC
  plugin host all already shipping in this codebase)
- **Persona:** Architect (locked, cross-plugin)
- **Mode:** Builder
- **Pacing:** brisk
- **Autonomy:** fully-autonomous
- **Deepening rounds habit:** zero when the recon is clean. **This cycle qualifies unusually
  strongly** — every row was re-verified against the tree hours before scoping, with per-row evidence
  preserved in `docs/superpowers/research/2026-08-10-register-reverification/`. There is no discovery
  left to do at `/scope`; the forks that remain are design calls, surfaced as assumptions.
- **Cycle type:** Remediation. Not spec-first and not greenfield — the input is a measured list of
  thirteen verified defects on one surface, so `/spec`'s job is picking the shape of the fix rather
  than discovering the problem.
- **Cycle target:** Close the 13-row Preferences/Settings cluster. Register 51 open → 38.
- **Project relationship:** clean reimplementation of MultiBloxy by Zgoly (technique, not code) — see
  [`PROVENANCE.txt`](../PROVENANCE.txt). v1 shipped to Microsoft Store + GitHub Releases via Velopack.
- **Distribution audience:** Pet Sim 99 clan first (non-technical Windows users), Microsoft Store
  second — **Store submission deliberately deferred** for v1.16 and v1.17 while the backbone is
  remediated. v1.17.0.0 is published as a pre-release, which the shipped updater filters out
  (`UpdateChecker.cs:43` constructs `GithubSource(..., prerelease: false)`), so no existing install
  auto-updates to it.
- **Deployment target:** microsoft-store-msix-velopack (Store leg parked, direct-download leg live)
- **Quality bar:** "Won't ship a broken-looking tile even if the rest works." This cycle's version of
  that bar is consistency: a page named for a feature that is not on it fails the same test a broken
  tile does.
- **Course-correction style:** raises the real objection directly — treat such interventions as
  load-bearing constraints, not casual remarks. Demonstrated this session: caught an off-centre 6px
  dot and a plugin that had stopped following the theme, both by looking at the running app, both
  invisible to every automated gate in the project.
- **Voice cell (per `~/.claude/CLAUDE.md` synthesis):** working/technical — Carmack precision when
  narrating mechanism, Antirez compressed problem-framing for trade calls

## Standing constraints for this cycle

Three exclusions the builder set explicitly at scope time. They are not preferences, they are guards:

1. **F-050 never closes in a remediation sweep.** Flipping its status cell auto-deletes the contrast
   gate's exemption and reddens three of four built-in themes.
2. **F-091 (plugin theming) is its own cycle.** Contract change to a NuGet external plugin authors
   consume.
3. **F-068 (61 flat button call sites) is its own cycle.** F-046 in this cycle brushes against it and
   must be scoped not to start that migration.
