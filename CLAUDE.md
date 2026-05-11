# RORORO

> **Persona:** This repo inherits **The Architect** from `~/.claude/CLAUDE.md`. No need to re-establish — just adds project context below.

RORORO is a Windows desktop app that lets a Roblox player run multiple clients side by side and quick-launch them as different saved accounts. Distributed first to Este's Pet Sim 99 clan, then to the Microsoft Store. Free product, branded under 626 Labs — every install spreads the umbrella.

## Tech Stack & Voice

- **Stack (locked, see spec §3):** .NET 10 LTS + C# 14, WPF + WPF-UI by lepoco, Hardcodet.NotifyIcon.Wpf (tray), Microsoft.Windows.CsWin32 (Win32 source generator), Microsoft.Web.WebView2 (cookie capture), `System.Security.Cryptography.ProtectedData` (DPAPI), Velopack (auto-update), Windows Application Packaging Project → MSIX, xUnit. WPF chosen over WinUI 3 for v1 because tray + Win32 interop is more battle-tested; WinUI 3 is a v2 conversation, not a v1 hack.
- **Brand:** Cyan `#17d4fa` + magenta `#f22f89`, always paired. Navy `#0f1f31` field. Space Grotesk display, Inter body, JetBrains Mono code/meta (uppercase + 0.12em tracking on small labels). Tagline: *Imagine Something Else.* Apply across icon, Store listing, About box, README, splash, and any user-facing surface — this product is brand-spread to a non-626 audience, so the brand has to land on first contact.
- **Voice (user-facing copy: README, Store listing, modals, About box):** Builder-to-builder, second person, sentence case. No "empower / leverage / seamlessly / unlock / unleash." Em-dashes welcome. No emoji in UI copy or marketing surfaces. Lead with the verdict, then unpack. Specific over generic — "second client opens in under 2 seconds" not "fast multi-instance."
- **Voice (clan-facing copy specifically — README short pitch, Discord post when shipping):** still 626 voice but warmer. The Pet Sim 99 audience is non-technical Windows users; explain the "More info → Run anyway" SmartScreen click-through plainly, no apologies, no jargon.

## Design system

Canonical brand spec lives at `~/.claude/skills/626labs-design/` (globally available — same skill across every 626 Labs repo). Use `colors_and_type.css` as the token source and `ui_kits/` as the pattern reference. **Apply via the design skill before producing icons, Store tile graphics, splash, or About-box artwork** — programmatic placeholders are disqualifying for the "won't ship a broken-looking tile even if the rest works" bar (pattern x from SnipSnap retro). For RORORO specifically: the `Square150x150Logo`, `Square44x44Logo`, `Wide310x150Logo`, splash, and tray icon set must all go through the skill.

## What's where

**What exists today:**

| Path | What it is |
|---|---|
| `docs/superpowers/specs/2026-05-03-RORORO-design.md` | **CANONICAL design spec.** Every architectural decision lives here. Other `docs/*.md` are pointer-stubs. |
| `docs/scope.md` | Spec-first Cart pointer-stub |
| `docs/spec.md` | Spec-first Cart pointer-stub with section index |
| `docs/prd.md` | Compressed PRD — stories + acceptance criteria + prioritization |
| `docs/builder-profile.md` | Builder-profile excerpt for this cycle |
| `docs/checklist.md` | **Active build plan.** Cycle-shaped — current cycle (v1.4 plugin system) ships 18 items across 3 milestones with 3 verification checkpoints. Older cycles overwrite this file each round. |
| `process-notes.md` | Cart cycle notes — sequencing rationale + risk callouts |
| `PROVENANCE.txt` | Source/hash/caveats for the reference binary; load-bearing for "this is a clean reimplementation, not a fork" |
| `MultiBloxy.exe` | Reference binary (Zgoly's MultiBloxy v1.1.0.0). Read-only — DO NOT modify or replace. |
| `src/ROROROblox.PluginContract/` | Shared NuGet (v0.1.0+) — `.proto` + generated C# bindings consumed by both RoRoRo and plugin authors. Versioned independently from RoRoRo's app version. |
| `src/ROROROblox.App/Plugins/` | Plugin host module — `PluginManifest`, `PluginCapability` vocab, `ConsentStore` (DPAPI), `PluginRegistry`, `PluginInstaller` (SHA-verified GitHub URL flow), `PluginProcessSupervisor`, `PluginHostService` (gRPC server impl), `CapabilityInterceptor`, `PluginUITranslator`, `PluginHostStartupService` (Kestrel + named pipe). |
| `src/ROROROblox.App/Plugins/Adapters/` | Adapters bridging existing RoRoRo singletons to plugin interfaces (mutex state, running accounts, launch invoker, WPF UI host). |
| `src/ROROROblox.PluginTestHarness/` | Integration test project — real Kestrel + named-pipe gRPC against real RoRoRoHostClient. |
| `docs/plugins/AUTHOR_GUIDE.md` | Plugin author guide — contract NuGet, capability vocabulary, manifest format, GitHub release shape (manifest.json + manifest.sha256 + plugin.zip), recipes. |
| `docs/store/reviewer-letter-1.4.0.0.md` | Partner Center submission letter for v1.4 — leads with the policy 10.2.2 alignment narrative. |
| `.superpowers/brainstorm/` | Pre-onboard brainstorming session artifacts |

**Source tree shipped (v1.0 onward):**

| Path | What it is |
|---|---|
| `src/ROROROblox.App/` | WPF process — App, MainWindow + ViewModel, TrayService, CookieCapture, Plugins/ (v1.4) |
| `src/ROROROblox.Core/` | Interfaces + primitives — `IMutexHolder`, `IAccountStore`, `IRobloxApi`, `IRobloxLauncher` (no UI dependencies) |
| `src/ROROROblox.Tests/` | xUnit unit-test coverage |
| `src/ROROROblox.PluginTestHarness/` | xUnit integration tests — real named-pipe gRPC (v1.4+) |
| `src/RORORO.Package/` | MSIX wapproj for Store-signed + self-signed sideload flavors |
| `roblox-compat.json` (in GitHub Releases) | Remote config — known-good Roblox version range + current mutex name; fetched at app startup |
| `dev-cert.pfx` | Self-signed sideload cert; **gitignored**, regenerated per dev box (see [`CONTRIBUTING.md`](CONTRIBUTING.md)) |

## How the system works at runtime

Two technical core ideas. Everything else is the wrapper that makes them safe and approachable.

1. **Hold the named mutex `Local\ROBLOX_singletonEvent`** so subsequent Roblox launches don't see themselves as the singleton — same trick MultiBloxy uses. The mutex name is read from remote config, NOT hardcoded — when Roblox renames it we ship a config update + Velopack release within hours rather than rebuild the binary.
2. **Use Roblox's documented authentication-ticket flow** (cookie → CSRF dance against `auth.roblox.com/v1/authentication-ticket` → `RBX-Authentication-Ticket` → `roblox-player:` URI → `Process.Start`) to launch a specific saved account. Same path Bloxstrap and earlier account managers use. We hit only documented public endpoints — UA is `ROROROblox/<version>`, no browser spoofing.

DPAPI per-machine, per-user is the access boundary for saved cookies. A `accounts.dat` file copied between PCs cannot be decrypted on the destination — that's intentional v1 design, not a bug. v1.2 will introduce per-cookie encryption + per-account WebView2 profiles; v1 deliberately ships whole-blob encryption + wipe-userdata-per-capture for simplicity.

The full architecture, data flows, error buckets, and decision rationale live in the canonical spec. Read it before asking "how should X work?"

## Common tasks

| You want to… | Path / command |
|---|---|
| See architectural intent | [`docs/superpowers/specs/2026-05-03-RORORO-design.md`](docs/superpowers/specs/2026-05-03-RORORO-design.md) |
| See the active build sequence | [`docs/checklist.md`](docs/checklist.md) |
| See sequencing rationale + risk callouts | [`process-notes.md`](process-notes.md) |
| See reference-impl provenance | [`PROVENANCE.txt`](PROVENANCE.txt) |
| Run the auth-ticket spike (item 1 HARD gate) | `dotnet run --project spike/auth-ticket` *(after item 1 lands)* |
| Build the app | `dotnet build` *(after item 2)* |
| Run unit + integration tests | `dotnet test src/ROROROblox.Tests/` (unit) + `dotnet test src/ROROROblox.PluginTestHarness/` (integration, v1.4+) |
| See plugin author guide (v1.4+) | [`docs/plugins/AUTHOR_GUIDE.md`](docs/plugins/AUTHOR_GUIDE.md) |
| See v1.4 plugin-system design | [`docs/superpowers/specs/2026-05-09-rororo-plugin-system-design.md`](docs/superpowers/specs/2026-05-09-rororo-plugin-system-design.md) |
| Build sideload MSIX | `msbuild src/RORORO.Package/RORORO.Package.wapproj /p:AppxPackageSigningEnabled=true /p:PackageCertificateKeyFile=dev-cert.pfx` *(after item 11)* |
| Build Store-signed MSIX | `dotnet publish src/RORORO.Package -p:GenerateAppxPackageOnBuild=true` *(after item 11)* |
| Cut a release | Velopack via `vpk pack` against the latest signed MSIX *(after item 10 + 11)* |

## Conventions

- **Commits:** conventional commits (`feat` / `fix` / `docs` / `refactor` / `test` / `chore` / `build` / `ci`). Item 1 (spike) is gitignored — first real commit is item 2's `feat: solution scaffold + AppLifecycle (single-instance + DI + run-on-login)`.
- **Style:** .NET defaults via `.editorconfig` (lands at item 2). C# 14 features welcome where they earn their place — no novelty for novelty's sake.
- **File rules:**
  - `docs/superpowers/specs/2026-05-03-RORORO-design.md` is canonical. When build reality drifts from the spec, **banner-correct** at the top of the doc (per pattern v from Vibe Thesis) — name what was originally proposed vs what was actually built. Do NOT rewrite top-to-bottom; that destroys /reflect-time framing.
  - `MultiBloxy.exe` and `PROVENANCE.txt` are reference-only. Treat as immutable — they're load-bearing for the "clean reimplementation, not a fork" framing.
  - `accounts.dat`, `consent.dat`, `webview2-data/`, `/plugins/`, `*.pfx`, `spike/`, `bin/`, `obj/`, `*.user`, `last-update-check.txt` are gitignored at all times. (`/plugins/` is anchored to root so it doesn't shadow `docs/plugins/`.)
  - The singleton mutex name lives in `roblox-compat.json` (remote config). Hardcoded fallback is OK in `MutexHolder`'s constructor as a last-resort default; the runtime read is always from config.
- **Manifests:**
  - `Package.appxmanifest` declares `runFullTrust` ONLY. No `broadFileSystemAccess`, no `internetClient` (outgoing HTTPS doesn't need declaration).
  - Partner Center identity is the Store-signed cert; sideload uses a separately generated self-signed cert. **The two keys are never the same.**
- **Roblox compat:** every architectural decision that depends on a Roblox-side contract (mutex name, auth-ticket endpoint, URI format, RobloxPlayerLauncher behavior) gets logged to the dashboard with the caveat. When Roblox ships a breaking update, the decisions log is how we trace what assumed-stable.

## Decisions log

Significant decisions log to the **626 Labs Dashboard** via MCP — `mcp__626Labs__manage_decisions log`. Tag with the bound project ID. The bar: *would future-you (or someone asking "why this approach?") want to know this in 3-6 months?*

Especially log:

- **Architectural choices.** Examples already locked in spec §11: WPF over WinUI 3, whole-blob over per-cookie encryption for v1.1, wipe-userdata-per-capture, `roblox-player:` URI over direct launcher exec, remote config for known-good Roblox versions.
- **Roblox-side compatibility events.** Mutex rename, auth-ticket endpoint shift, Hyperion adding a non-mutex check, RobloxPlayerLauncher protocol-handler behavior change. These are the events that make `roblox-compat.json` shift; each warrants a decision entry.
- **Distribution decisions.** Microsoft Store submission outcome (accept / reject / cert request), sideload cert rotation, SmartScreen escalation strategy if AV heuristic flags surface.
- **Deviations from the canonical spec.** Any time build reality requires drifting from `docs/superpowers/specs/2026-05-03-RORORO-design.md`, log the *why* — banner-correct the spec separately.
- **Overcame momentous hurdle.** Hyperion-shape changes that almost killed multi-instance, DPAPI envelope shifts, packaging breakdowns. Per global Architect rules: not "we fixed a bug," but "we found out the bar was higher and met it anyway."

Skip the routine: ran tests, fixed typo, renamed a variable.

If unbound (no 626 Labs Dashboard project yet): tag with `RORORO` in the description and set `projectId: null`. First session in this repo should attempt `mcp__626Labs__manage_projects findByRepo` against `https://github.com/estevanhernandez-stack-ed/ROROROblox` to bind, and offer to create a Dashboard project if no match.

## What NOT to do

- **Don't commit `dev-cert.pfx`, `accounts.dat`, `webview2-data/`, or any file containing a `.ROBLOSECURITY` value.** These are user-secret-equivalent. The pre-commit hook (lands at item 12) and CI must fail loud if they appear. If you find one in working tree, scrub it before next commit.
- **Don't hardcode the singleton mutex name in source.** It lives in `roblox-compat.json`. Hardcoded fallback is OK as a last-resort default in `MutexHolder`'s constructor; the runtime read is always from config. This is the reason we can ship a config update in hours when Roblox renames the mutex (spec §7.1).
- **Don't masquerade as a browser in HTTP requests.** User-Agent is `ROROROblox/<version>` — no `Mozilla/5.0`, no Edge spoofing. Roblox treats spoofed UAs as a flag; we want to be transparent and identifiable, both for compatibility and for the "we're a tool, not malware" Store narrative.
- **Don't ship programmatic icon placeholders to the Store.** All icons / Store tile graphics / splash / tray icons go through the `626labs-design` skill (pattern x from SnipSnap retro). The Store reviewers AND the Pet Sim 99 clan eyes will both notice. Brand-spreads-for-free only works when the brand actually shows up.
- **Don't rewrite the canonical spec on drift — banner-correct it.** When build reality diverges from `docs/superpowers/specs/2026-05-03-RORORO-design.md`, add a top-of-doc warning block naming what was originally proposed vs what was actually built (pattern v from Vibe Thesis). Top-to-bottom rewrites destroy /reflect-time framing and bury the original architectural reasoning.
- **Don't add macros, input automation, or any client-injection capability.** That product is **MaCro** (separate macOS product) and the wall is intentional. Roblox treats macro-tooling as far higher-risk than mutex-fiddling. Keeping RORORO macro-free is a deliberate Roblox-relations move, not a feature gap. If a clan member asks for it: "MaCro handles that — different product, different platform."
- **Don't push to main without item 12's local-path audit.** Per pattern kk from wbp-azure: a `c:\Users\<name>\` reference in committable code breaks CI on every machine that isn't yours. The grep is one line; run it before push.
- **Don't run end-to-end automation against real roblox.com.** Bot accounts get flagged; flaky CI eats trust. Manual smoke from spec §8 on a clean Win11 VM is the v1 trade. If automated coverage of the auth-ticket flow becomes load-bearing later, that's a v1.2 conversation about owning a dedicated test-account with appropriate isolation.
- **Don't ship the Store-signed cert and the sideload cert as the same key.** Store-signed identity is owned by Microsoft Partner Center; sideload cert is generated locally per dev box and gitignored. Cross-contamination of the two keys is an immediate distribution-trust incident.

## References

- Canonical design spec: [`docs/superpowers/specs/2026-05-03-RORORO-design.md`](docs/superpowers/specs/2026-05-03-RORORO-design.md)
- Active build plan: [`docs/checklist.md`](docs/checklist.md)
- Cart process notes: [`process-notes.md`](process-notes.md)
- Reference-impl provenance: [`PROVENANCE.txt`](PROVENANCE.txt)
- 626 Labs design system (global): `~/.claude/skills/626labs-design/`
- Global persona (The Architect): `~/.claude/CLAUDE.md`
- Repo: https://github.com/estevanhernandez-stack-ed/ROROROblox
