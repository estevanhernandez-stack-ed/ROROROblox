# Builder profile — ROROROblox v1.3.x cycle (default-game widget + local rename)

Cart cycle #2 on this repo (lifetime cycle #14 across all projects). Full unified profile at `~/.claude/profiles/builder.json`. Cycle-relevant excerpt:

- **Builder:** Estevan ("Mr. Solo Dolo")
- **Experience level:** experienced (.NET 10 LTS, WPF, MSIX, C#, Win32 interop, WebView2 already shipping in this codebase)
- **Persona:** Architect (locked, cross-plugin)
- **Mode:** Builder
- **Pacing:** brisk
- **Autonomy:** fully-autonomous (promoted 2026-04-26 after 9+ Cart cycles; reaffirmed by 13/13 completion rate at unified profile)
- **Deepening rounds habit:** zero when the spec is clean (this cycle qualifies — `2026-05-07-default-game-widget-and-rename-design.md` is "Approved for implementation planning" with no architectural decisions still open)
- **Cycle type:** Spec-first (pattern mm) — substantive design done in pre-onboard authoring, captured in the design spec; `/scope`, `/prd`, `/spec` will compress to pointer-stubs + compressed PRD
- **Cycle target:** Default-game widget + per-record local rename overlay across `FavoriteGame`, saved private servers, and `Account`. Mac-banner parity, Windows-tailored. v1.3.x feature add post-v1.2 FPS limiter
- **Project relationship:** clean reimplementation of MultiBloxy by Zgoly (technique, not code) — see [`PROVENANCE.txt`](../PROVENANCE.txt). v1 shipped to Microsoft Store + GitHub Releases via Velopack. v1.2 added per-account FPS limiter via `GlobalBasicSettingsWriter` (XML lever, NOT FFlag — see memory `GlobalBasicSettings is the FPS lever`)
- **Distribution audience:** Pet Sim 99 clan first (non-technical Windows users), Microsoft Store second
- **Deployment target:** microsoft-store-msix-velopack (refreshed for this cycle — was stale from cycle #13 Marcus context)
- **Quality bar:** "Won't ship a broken-looking tile even if the rest works" — applies to icons, Store assets, SmartScreen first-run UX, AND now to the rename popup chrome (must match `ui:FluentWindow` of existing modals per spec §3)
- **Course-correction style:** raises the real objection directly — treat such interventions as load-bearing constraints, not casual remarks
- **Voice cell (per `~/.claude/CLAUDE.md` synthesis):** working/technical — Carmack precision when narrating mechanism, Antirez compressed problem-framing for trade calls
