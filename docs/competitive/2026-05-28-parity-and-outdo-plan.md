# RoRoRo parity + outdo plan

> **Status:** review draft. Read it, mark up the matrix, settle the open questions at the bottom — then it becomes a `/checklist`-shaped build plan. This is the catalog of every axis where a rival currently beats us, what parity looks like, and how we leapfrog instead of merely matching. Nothing here is committed yet.

We already ran the competitive verdict: RoRoRo leads the full stack — the only polished, signed, Store-distributed account-manager + multi-instance product with a real plugin system. But "leads the stack" is not "wins every axis." Five axes have a rival genuinely ahead of us right now. This doc names each one without homer-ing, defines concrete parity, then defines the move that uses a moat the rival cannot copy.

---

## Where we stand

**Axes we lead, and should defend:**

- **Account security.** Per-user DPAPI + `UseCookies=false` + AES-256-GCM/PBKDF2-600k passphrase export. Every rival with accounts (RAM) is weaker: LocalMachine DPAPI with public hardcoded entropy. Everyone else has no account model at all.
- **Distribution trust.** Signed MSIX + Microsoft Store + Velopack delta auto-update. Bloxstrap is signed-EXE only (lower tier); RAM / Fishstrap / MultiBloxy / Voidstrap are unsigned and chronically Defender-quarantined.
- **Extensibility architecture.** Capability-gated gRPC-over-named-pipe plugin system + versioned contract NuGet. RAM's web/Nexus API is the only comparable surface and it's global on/off toggles, not per-capability consent. Nobody else has anything.
- **Consumer UX.** Presence-as-truth status, tags + filter, saved private servers, local rename overlay, branded splash. The whole field below Bloxstrap is utility-tier; even Bloxstrap is launcher-tier, not account-manager-tier.
- **Roblox-breakage recovery posture.** Remote-config version-drift banner + Store/Velopack pipeline. Honest caveat: the *mutex-name* swap is NOT yet config-driven (hardcoded default), so a rename still ships a binary, not a config push. That's a real gap inside an axis we claim — flagged below.

**Axes a rival genuinely beats us on today:**

| Axis | Who's ahead | The specific edge |
|---|---|---|
| Account-management *breadth* | RAM (evanovar + ic3w0lf22) | Bulk cookie import, auto cookie-refresh, server browser, last-used tracking, launcher composition |
| Multi-instance *resilience* | Fishstrap + RAM | Process-detection launch-confirm (Fishstrap v3.0.2); Handle64 add-to-already-running (RAM) — we require quitting both processes |
| Client customization | Fishstrap + Bloxstrap + Voidstrap | FastFlag editor, persistent client mods, render presets — we deliberately don't do this |
| Community trust + tenure | Bloxstrap | Years-deep install base, BloxstrapRPC open standard, ambient "it's clean" reputation |
| Lightness / zero-install | MultiBloxy | ~88KB single portable exe, ~3MB RAM, run-from-StartUp-folder |

Three of those five we will partially or fully **decline** on purpose. Two we should **adopt to parity and then outdo**. The matrix sorts which is which.

---

## The parity matrix

Cells: **Y** = ships it · **~** = partial / different shape · **N** = doesn't have it. "MB/light" = MultiBloxy + the bare-utility tier + Voidstrap's lightest behaviors.

| Feature | RAM | Fishstrap | Bloxstrap | MB/light | RoRoRo today | Gap verdict |
|---|---|---|---|---|---|---|
| **Multi-instance via mutex hold** | Y | Y | N (removed v2.10) | Y | Y | AHEAD (Watchdog + honest ERROR state) |
| Remote-config *mutex-name* swap | N | ~ (remote config exists) | N | N | N (hardcoded default) | ADOPT — closes our own claimed gap |
| Remote-config *version-drift* check | N | Y | ~ (channel fallback) | N | Y | AHEAD |
| Process-detection launch-confirm | ~ (running warning) | Y (v3.0.2) | N | ~ (remembered dialog) | ~ (StartupGate hard-block) | ADOPT — refine, not rebuild |
| Add-to-already-running Roblox (Handle64) | Y (admin) | N | N | N | N (must quit both) | ROUTE-TO-PLUGIN — non-elevated path exists but is foreign-process handle work; elevated variant is hard AVOID (see gate) |
| Live pause/resume/reload mutex toggle | Y | N | N | Y | ~ (tray on/off) | ADOPT (cheap, diagnostic value) |
| Stop-all-instances | Y | N | N | Y | N | ADOPT (cheap) |
| **Documented auth-ticket launch** | Y | N | N | N | Y | AHEAD |
| Saved-account roster | Y | N | N | N | Y | AHEAD (outright — uncontested below Bloxstrap) |
| WebView2 cookie capture | Y (Chromium) | N | N | N | Y (per-capture profiles) | AHEAD |
| Single cookie paste-import | Y | N | N | N | N | ADOPT (power-user onboarding) |
| Bulk cookie import | Y | N | N | N | N | DECLINE-ish — adopt *single*, not farm-scale bulk (see Q3) |
| JS bulk-add / account harvesting | Y | N | N | N | N | DECLINE (ban-evasion/farming territory, off-brand) |
| Auto cookie-refresh | ~ (orig C# only) | N | N | N | N | ADOPT — but as *re-auth nudge*, not silent re-mint (see axis) |
| Lazy session validation (anti-2FA-flag) | N | N | N | N | Y | AHEAD (we removed startup-validate on purpose) |
| Cookie validity / staleness indicator | Y | N | N | N | ~ (presence flips to expired) | AHEAD-ish (presence covers it) |
| Last-used tracking + sort | Y | N | N | N | ~ (session history exists, not surfaced as sort) | ADOPT (small) |
| Per-account notes/alias | Y | N | N | N | ~ (tags + local rename) | AHEAD-ish |
| Drag-reorder roster | Y | N | N | N | Y (SortOrder) | AHEAD |
| Account groups/folders | Y | N | N | N | ~ (tags + filter) | AHEAD-ish (tags are the consumer slice) |
| **Per-user DPAPI cookie encryption** | ~ (LocalMachine + public entropy) | N | N | N | Y | AHEAD (materially stronger) |
| Password-portable vault tier | Y | N | N | N | ~ (passphrase *export*, not at-rest) | DECLINE — export is the deliberate, narrower hole |
| Passphrase-encrypted export/import | N | N | N | N | Y (AES-GCM, no oracle) | AHEAD (uncontested) |
| **FastFlag editor** | ~ (FPS unlock only) | Y | Y (removed for Player v2.10) | ~ (Voidstrap 800+) | N | DECLINE (safety wall) |
| Persistent client mods (cursor/sound/font) | N | Y | Y (gold standard) | ~ (Voidstrap hub) | N | DECLINE (safety wall = MaCro's lane) |
| Render/quality presets | N | Y | ~ (gutted by allowlist) | Y (Voidstrap) | N | DECLINE (FastFlag-adjacent) |
| Global Basic Settings FPS lever | ~ (settings editor) | Y (v3.0.0) | N | Y (Voidstrap) | Y (per-account, dual-write) | AHEAD (ours is per-account; theirs global) |
| Anti-AFK / input automation | Y | N | N | N | N | DECLINE (MaCro / out-of-process plugin only) |
| RAM trim / working-set clear | Y | N | N | N | N | DECLINE-ish (low value, plugin-able — see Q4) |
| **Server browser (pop + ping/region)** | Y | ~ (region display) | ~ (region hint) | N | N | ADOPT (genuine UX gap) |
| Join-smallest / recommended server | Y | N | N | N | N | ADOPT (pairs with browser) |
| Job-ID (exact server) join | Y | N | N | N | ~ (private-server code path) | ADOPT (co-locate alts) |
| Private-server / VIP link save+launch | Y | ~ (deeplinks) | N | N | Y (saved + ToShareUrl) | AHEAD |
| Share-token resolve (opaque URLs) | Y (v2.4.6) | N | N | N | Y (resolve-link API) | AHEAD |
| Follow-a-friend / join-user | Y | N | N | N | Y (RequestFollowUser) | AHEAD — honest gap: Friends-modal lacks land-at-home guard |
| Favorites / recently-played | Y | N | N | ~ (Voidstrap history) | ~ (saved games) | AHEAD-ish |
| Launcher composition (route via Bloxstrap/Fishstrap) | Y | N/A | N/A | N | N | DECLINE — but compatible coexistence is a talking point (see Q5) |
| Multi-account simultaneous launch | Y (staggered) | N | N | ~ (start-new) | Y (LaunchAll + PreWarmGate) | AHEAD (pre-warm beats stagger) |
| Auto-rejoin / AFK-relaunch | Y | N | N | N | N | DECLINE (farming; plugin-only if ever) |
| Internet-loss rejoin safety | Y | N | N | N | N/A | DECLINE (no rejoin to guard) |
| **Discord Rich Presence** | ~ (webhook) | Y | Y (+ open standard) | ~ (Voidstrap) | N | ADOPT — but as a *plugin*, not core |
| BloxstrapRPC-style game-dev presence standard | N | Y (implements) | Y (owns standard) | N | N | DECLINE (their moat; not our lane) |
| Activity / player / message logs | N | Y | Y | N | ~ (session history) | DECLINE-ish (log-scraping; low brand value) |
| Server-region display | ~ | Y (RoValra) | Y (ipinfo) | N | N | ADOPT (folds into server browser) |
| BetterMatchmaking / latency steering | N | Y (experimental) | N | N | N | DECLINE (experimental, brittle) |
| **Window tiling / auto-arrange** | Y | N | N | N | N | ADOPT (real multi-window pain) |
| Per-window title rename | Y | N | N | ~ (MultiBloxy) | ~ (caption tint, not title) | ADOPT-ish (tint covers most of it) |
| Per-account window caption tint | N | N | N | N | Y (DwmSetWindowAttribute) | AHEAD (uncontested) |
| **Code signing** | N | N | Y (SignPath EXE) | N | Y (MSIX, higher tier) | AHEAD |
| Microsoft Store distribution | N | N | N | N | Y | AHEAD (uncontested) |
| Background/delta auto-update | ~ (download-replace) | Y (Roblox bg) | ~ (prompt/opt-in) | N | Y (Velopack) | AHEAD |
| Channel switch / pin Roblox version | N | Y | ~ (fallback only) | N | N | DECLINE-ish (compat config covers the defensive need — see Q2) |
| Skip/defer Roblox update on launch | N | Y | ~ | N | ~ (v1.7 PreWarmGate defers mid-batch) | ADOPT-ish (we defer timing, not the update itself) |
| Roblox version downloader / pin | Y | N | N | N | N | DECLINE (compat-banner is our answer) |
| **Custom themes** | Y (import/export) | Y (zip) | Y (Theme.xml ecosystem) | N | Y (ThemeBuilder) | AHEAD-ish (ours branded, theirs broader) |
| Tray-only operation | N | ~ | ~ | Y | ~ (tray + window) | DECLINE (window is the consumer trade) |
| Single-file portable exe | N | N | N | Y (~88KB) | N (MSIX) | DECLINE (trust > size) |
| Localization (multi-language) | N | Y (locale) | Y | ~ (MultiBloxy EN/RU) | N | DECLINE for v1 — revisit if clan goes international (Q6) |
| **Out-of-process capability-gated plugin SDK** | ~ (global-toggle web API) | N | N | N | Y (gRPC + contract NuGet) | AHEAD (uncontested) |
| SHA-verified install + DPAPI consent | N | N | N | N | Y | AHEAD |
| Plugin crash isolation + UI translation | N | N | N | N | Y | AHEAD |

**Tally:** 8 ADOPT, ~8 ADOPT-ish/small, the rest split between AHEAD (our defended ground) and DECLINE (the safety wall, named below). The two axes worth a real cycle are **multi-instance resilience** and **server-selection UX** — both are where a rival beats us on a capability our audience actually touches, and both have an outdo move our architecture can reach and theirs can't.

---

## Shippability gate — the hard constraint

Every candidate above is filtered against two commitments that are not negotiable: (1) the capability-gated, out-of-process gRPC **plugin system**, and (2) **Store distribution via signed MSIX**. Anything that collides with either is **AVOIDED** or **routed to the plugin lane**. This isn't new policy — it's the doc's own "yes to power, no in the Store binary" thesis, now enforced as a gate over every row.

**The one fact that decides most of the triage:** the Store binary is a **medium-integrity, non-elevatable, full-trust Win32 process.** `runFullTrust` grants the complete Win32 API surface *as the interactive user* — it is **not** elevation; the two are orthogonal. (Verified against Microsoft Learn; Store Policies **v7.19**, effective Oct 14 2025.)

The verified policy spine:

- **Elevation is a hard Store no.** An app that requires elevation for *any* part of its functionality is rejected. MSIX honors no `requireAdministrator`, and there is no supported elevated-child or per-user-service escape hatch. ([desktop-to-uwp-prepare](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-prepare))
- **`runFullTrust` = full Win32 at medium IL, not admin.** `OpenProcess` / `DuplicateHandle` / `NtQuerySystemInformation` and writes to `%LOCALAPPDATA%` (incl. `%LOCALAPPDATA%\Roblox` for the per-account FPS dual-write) all work *without* elevation, scoped to same-user / same-integrity processes — exactly the mutex-hold neighborhood. ([behind-the-scenes](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes))
- **Policy 10.2.2 is the config-vs-code hinge.** Fetching a *string* (the mutex name from `roblox-compat.json`) is remote **data** — allowed. Fetching + executing a *script* to gain new behavior is remote **code** — rejected. This keeps remote-config mutex name clean, and it's why plugins must be out-of-process and user-initiated.
- **Run-at-login is legal via `windows.startupTask`** (`EntryPoint=Windows.FullTrustApplication`) — but the user owns the kill-switch and `DisabledByUser` is final; the app may not re-enable it. (Not a Run-key or scheduled task — both draw heuristic scrutiny.)
- **The cookie surface is shippable but highest-review-risk** — it survives on 10.5.1 (privacy policy, mandatory for packaged Win32), 10.5.4 (DPAPI-at-rest), 10.5.5 (explicit consent), 10.2.4 (first-line Roblox-dependency disclosure), and the reviewer-letter's 10.2.2 pre-emption.
- **The plugin lane absorbs foreign-process power.** Plugins run as their own full-trust process (10.2.2 forces out-of-process), are sideloaded SHA-verified outside the MSIX package, capability-gated via the `x-plugin-id` header, and **can never reach `.ROBLOSECURITY` / `accounts.dat`** — the host only hands a plugin an `accountId`. `system.*` capabilities (input synthesis, foreign-process reach) are disclosure-only because RoRoRo can't sandbox a separate process — which is exactly why dangerous power belongs there, behind user consent, and not in the Store binary.

**Gate verdicts — every candidate:**

| Candidate | Gate verdict |
|---|---|
| Remote-config mutex name | **KEEP-CORE** |
| Process-based confirm read | **KEEP-CORE** |
| Live pause/resume/reload mutex toggle | **KEEP-CORE** |
| Stop-all-instances | **KEEP-CORE** |
| Batch re-auth queue (not silent auto-refresh) | **KEEP-CORE** |
| Last-used sort + staleness badge | **KEEP-CORE** |
| Window tiling / auto-arrange | **KEEP-CORE** |
| Single `.ROBLOSECURITY` paste-import | **KEEP-CORE-WITH-CARE** (advanced gate + 10.5.x mitigations) |
| Server browser (pop / ping / region / smallest / Job-ID) | **KEEP-CORE-WITH-CARE** (documented endpoints, no scraping) |
| Mutex-only background "light mode" | **KEEP-CORE-WITH-CARE** (`windows.startupTask`; v2) |
| Add-to-already-running (Handle64), non-elevated | **ROUTE-TO-PLUGIN** |
| Discord Rich Presence | **ROUTE-TO-PLUGIN** |
| RAM trim / working-set clear | **ROUTE-TO-PLUGIN** (or never) |
| Add-to-already-running (Handle64), elevated variant | **AVOID** (elevation = Store rejection) |
| Roblox version pinning / downloader | **AVOID** (10.2.3) |

The shape of the result: **nothing on the core roadmap collides** — the two real-cycle axes (multi-instance resilience, server-selection UX) are KEEP-CORE. Only one item changed lane (Handle64 → plugin), one factual error got caught (there is no "signed Store-trusted elevation path" — struck below), and two AVOIDs got confirmed as rules, not preferences.

**Watch flag (Roblox-side contract):** if Roblox ever promotes the client to a protected process (PPL), `PROCESS_DUP_HANDLE` is stripped for *all* callers regardless of user/integrity — killing even the plugin-lane Handle64 technique. Not true today; log it to the dashboard as an assumed-stable contract to track.

---

## Account-management depth — vs RAM

**What RAM does better.** Breadth. Single-paste cookie import and bulk cookie import onboard a roster in seconds; the original C# auto-refreshed `.ROBLOSECURITY` tokens so a large fleet never silently went stale; last-used tracking + sort surfaces dead alts; a server browser with population + ping lets you pick a healthy server before joining. RAM is built for someone running dozens of alts. We're built for someone running a clan loadout — but the onboarding-friction and staleness-visibility gaps are real even at clan scale.

**Parity target (concrete):**
- Single `.ROBLOSECURITY` paste-import as an explicit "Add by cookie" path alongside the WebView2 flow — gated behind a clear "advanced" affordance so the default stays the safe login flow.
- A staleness signal that doesn't depend on the user opening presence — a quiet badge sortable to the top ("3 accounts need re-auth").
- Last-used timestamp surfaced as a sort option (we already log sessions; just expose it).

**The outdo move.** RAM's auto cookie-refresh is the feature that *looks* like the win and is actually the trap — silently re-minting tokens is exactly the anti-fraud-heuristic pattern-match we **removed** in v1.1.2.0 because it triggered surprise 2FA. So we don't copy it. We outdo it: a **batch re-auth queue** — one consent-gated pass that walks every stale account through the *real* WebView2 re-login at the moment the user intends it, instead of a background daemon poking Roblox's auth surface on a timer. Pairs the staleness badge with a one-click "re-auth all flagged" that's honest about touching the network. That's the maturity move RAM's architecture can't make without re-introducing the flag risk it never noticed it had.

And the structural outdo: **bulk import stays out of the Store binary.** If farm-scale onboarding ever matters, it ships as a sideload plugin over the capability-gated gRPC contract — RoRoRo-the-Store-product never grows a cookie-harvesting surface. RAM can't separate the dangerous breadth from the safe core; we can, by construction.

**Effort/risk.** Paste-import + staleness badge + last-used sort: **low effort, low risk** — additive to AccountStore, no new trust surface. Batch re-auth queue: **medium effort** (WebView2 sequencing + consent UX), **low risk** (uses the existing safe flow). Bulk-as-plugin: **defer** until demand is real (Q3).

---

## Multi-instance resilience — vs Fishstrap

**What Fishstrap (and RAM) do better.** Two distinct things, don't conflate them:
1. **Fishstrap v3.0.2** changed its launch-confirmation check from mutex-existence to *process-detection*, because the mutex lingers for seconds after Roblox closes and caused false "still running" reads. That's a UX-accuracy refinement on the confirm prompt — not a different core mechanism (still watcher-held mutex). Our `StartupGate` already detects pre-existing processes, but it *hard-blocks* and exits; Fishstrap *informs and proceeds*.
2. **RAM's Handle64 mode** is the genuinely bigger edge: using `handle64.exe` (admin) to release/duplicate the singleton handle on a Roblox the tool **didn't launch**, so you can add instances alongside an already-open client. Our mutex-recovery memory is blunt about our gap here — we require quitting *both* Roblox and RoRoRo to re-acquire cleanly. RAM doesn't.

**Parity target (concrete):**
- Soften the all-or-nothing recovery: when Roblox is detected already running, give a clearer recovery modal — and treat handle adoption as the plugin-lane escape hatch (it's foreign-process work that can't sit in the Store binary; see gate), not the core's only door beyond "close both and restart."
- Make the launch-confirm / running-state read process-based where it's currently mutex-based, so we stop inheriting the lingering-mutex false read.

**The outdo move.** Handle64 is RAM's win *and* its liability. The verified Win32 reality: a **non-elevated** same-user path exists (`PROCESS_DUP_HANDLE` + `DuplicateHandle(DUPLICATE_CLOSE_SOURCE)` against a Roblox the user owns — RAM's admin requirement is a convenience from shelling out to `handle64.exe`'s system-wide scan, not a hard floor). But that technique is foreign-process handle-table manipulation; it reads identically to RAM's AV-quarantine history and trips Store cert + Defender heuristics. And the *elevated* variant is a hard AVOID: MSIX provides no supported elevation path, and an app that requires elevation for **any** part of its functionality is rejected from the Store (Store Policies v7.19 — no elevated-child or per-user-service escape hatch). ~~Present a signed, Store-trusted elevation path~~ is a factual impossibility for a packaged app and is struck. So add-to-already-running can never live in the Store binary — either way. The outdo is to put it **exactly where the architecture was built to quarantine foreign-process power**: a capability-gated, consent-gated, SHA-verified *sideload plugin* running as its own process. RoRoRo-the-Store-product stays clean; the power user who wants add-to-already-running installs the plugin and consents to it. No rival can offer "this power, but the signed Store product never grows the surface" because no rival built the wall.

The deeper outdo is the one no rival can touch: **remote-config the mutex name.** Right now this is a gap *inside* an axis we claim to lead — CLAUDE.md says config-driven, the code hardcodes `Local\ROBLOX_singletonEvent` with a fallback default. Close it. When Roblox renames the mutex (the single event that hard-bricks MultiBloxy and forces a Fishstrap/Voidstrap rebuild), RoRoRo ships a `roblox-compat.json` push and recovers in **hours, not a release cycle** — and the Store-signed Velopack channel delivers it cleanly. Fishstrap reacts fast *for an unsigned tool*; we react faster *and* arrive trusted. That's the resilience story that actually wins the axis, and it's a config field plus a constructor read.

**Effort/risk.** Process-based confirm read: **low**, core. Remote-config mutex name: **low-medium**, core (the read path exists for version-drift; extend it — but test the fallback-default behavior hard, this is the load-bearing mechanism). Handle adoption: **medium-high effort, medium risk, plugin-lane only** — Win32 handle duplication is fiddly, and the technique can't sit in the Store binary regardless of elevation. Sequence the cheap core wins first; the handle work is its own plugin spike (Q1).

---

## Client customization / FastFlags — vs Fishstrap + Bloxstrap

**What they do better.** This is the lane we lose on paper and win on posture. Bloxstrap's FastFlag editor + persistent client mods (cursor/sound/font/texture overlay re-applied every launch) are the genuine gold standard — the features users switch launchers *for*. Fishstrap kept and extended both. Voidstrap piles on 800+ flags, an AI flag chat, and a community mod hub. We have none of it, on purpose.

**Parity target.** **None.** This is a DECLINE, and the reason is load-bearing, not defensive:
- FastFlag value was structurally gutted by Roblox's **Oct 2025 allowlist** — most custom flags are accepted in the editor and silently dropped at runtime. Bloxstrap *removed* the editor for Player users in v2.10.0. We'd be chasing a feature Roblox is actively neutering.
- Client mods are the exact capability our wall excludes — file-overlay-into-the-client-folder is one category line away from injection, and it's **MaCro's lane** (separate macOS product, intentional separation). Roblox treats client modification as higher-risk than mutex-fiddling; the May 2025 "modified clients" policy carved out Bloxstrap precisely *because* it does no binary mod. We stay on the same safe side without taking on the mod-overlay surface at all.

**The outdo move.** We already won the part that survived the allowlist: our **per-account FPS cap** dual-writes `GlobalBasicSettings_<N>.xml` (the lever that actually works for default-config users — the dominant clan profile) *and* the FFlag, non-blocking and degraded-on-failure. Fishstrap's Global Basic Settings editor is *global*; ours is *per-account*. That's the right granularity for a multi-account tool and it's already shipped. The outdo is to **lean into "ban-safe tuning, per account"** as the explicit framing — and route anything heavier (true client mods, macros, AFK) to the **out-of-process signed plugin lane**, where it can never contaminate the Store binary. We get to say yes to the power user *and* keep the Store narrative clean. No rival can offer "macros, but the Store product stays macro-free" because no rival has the capability-gated plugin architecture to put the wall in.

**Effort/risk.** Zero for the decline. The framing work is copy + listing, not code.

---

## Community trust + tenure — vs Bloxstrap

**What Bloxstrap does better.** Years-deep install base, ambient "it's clean, Roblox staff confirmed it" reputation, and **BloxstrapRPC** — an *adopted open standard* for game-dev-controlled Discord presence that Sober and other forks implement, with an SDK on the marketplace and npm. That's an ecosystem moat measured in time and adoption, not features. We can't buy tenure.

**Parity target.** We don't out-tenure a multi-year project in a cycle. Parity here is **trust signals**, not install count:
- The Store listing + signed MSIX is *already a higher trust tier* than Bloxstrap's signed-EXE — surface that plainly in clan-facing and Store copy ("Signed by Microsoft. No SmartScreen warning." — which is literally true on the Store path and already in `index.md`).
- A "RoRoRo is a clean tool" explainer that mirrors Bloxstrap's bans-stance wiki page: documented endpoints, transparent UA, no client mod, no injection. We have the stronger story; we just haven't packaged it as a single citable page the clan can point skeptics at.

**The outdo move.** Bloxstrap's reputation rests on "we read logs and edit local files, trust us, we're unsigned-but-clean." Ours rests on **architecture you can verify**: Microsoft-signed, Store-reviewed, minimal-capability manifest (`runFullTrust` only — no `broadFileSystemAccess`, no `internetClient`), per-user DPAPI, open source. The outdo is to make the trust **checkable, not anecdotal** — publish the reviewer-letter narrative (policy 10.2.2 alignment) as a public-facing trust page, and let the plugin system be the *community* play Bloxstrap's RPC standard is: a capability-gated, contract-versioned SDK any author can target, where the consent model is the selling point. Bloxstrap's standard is game-dev-side presence; ours is app-side extensibility with a real security model. Different lane, and ours is the one that compounds into a third-party developer ecosystem we own — branded 626 Labs, spreading the umbrella with every plugin shipped.

**Effort/risk.** Trust page + framing: **low effort, high leverage.** Plugin-ecosystem-as-community-play: already built; the work is author outreach + the AUTHOR_GUIDE we have. Tenure itself only comes with shipping cadence — keep shipping.

---

## Lightness / zero-install — vs MultiBloxy

**What MultiBloxy does better.** ~88KB single portable exe, ~3MB RAM, 0% CPU, drop-in-StartUp-folder persistence, settings-next-to-the-binary, live pause/resume/reload mutex control, multi-language. It's the literal benchmark for "you won't notice it's running." A WPF + WebView2 + plugin-host process cannot and will not touch 3MB.

**Parity target.** We do **not** chase size — that's a DECLINE, and the right one. The MSIX + Velopack + accounts + presence + plugins *is* the weight, and it buys everything MultiBloxy can't do. But two of MultiBloxy's *behaviors* are cheap and worth adopting because they're genuinely better UX, independent of footprint:
- **Live pause/resume/reload mutex toggle** as a tray/diagnostic affordance — we expose on/off; MultiBloxy's explicit reload-on-error is a faster recovery UX than "restart the app."
- **Stop-all-instances** — a one-click teardown the light tier's users expect and we lack.

**The outdo move.** We can't win on grams, so we win on what the grams buy and reframe the comparison: MultiBloxy holds the mutex and launches *whatever's logged in* — zero accounts, zero auth-ticket, hardcoded mutex name that **bricks on a Roblox rename until someone rebuilds an abandoned project** (last code commit Nov 2024). The outdo is to make our *idle* footprint honest and small where it can be — the mutex hold + watchdog is the only thing that needs to run continuously; the WPF window and plugin host are summonable, not always-resident. If we ever want the light-tier user, ship a **mutex-only background mode** that idles near MultiBloxy's profile and summons the full UI on demand — same binary, signed, auto-updating, remote-config-resilient. We'd be MultiBloxy's lightness *and* RoRoRo's everything, on the trust tier MultiBloxy will never reach because it's abandoned and unsigned.

**Effort/risk.** Pause/resume/reload + stop-all: **low.** Mutex-only background mode: **medium effort** (process-lifecycle work — decouple the resident hold from the WPF shell), **low risk**, and probably a v2 conversation, not v1.7 (Q7).

---

## What we will NOT match — and why

This is the safety posture as a *feature*, not a gap. Every item here is a deliberate line, and the line is what keeps RoRoRo in the Microsoft Store and on the safe side of Roblox's "modified clients" policy.

- **No macros / input automation / anti-AFK.** That's **MaCro** (separate macOS product). Roblox treats macro tooling as far higher-risk than mutex-fiddling. Macro features can only ever arrive as separately-distributed sideload plugins — never in the Store binary. RAM bundles anti-AFK key recording; we draw the line they don't.
- **No FastFlag editing / client mods / texture-sound-cursor overlay.** This is injection-adjacent and Roblox-relations-risky, and the May 2025 policy carve-out for Bloxstrap exists *because* clean tools don't binary-mod. Also: Roblox's Oct 2025 allowlist already gutted the feature's value. We'd take on risk to chase a dying capability.
- **No silent auto cookie-refresh.** Removed for cause in v1.1.2.0 — it pattern-matched anti-fraud heuristics and triggered surprise 2FA. RAM's "edge" here is a flag we already learned to avoid. Re-auth is user-intended, not daemon-driven.
- **No bulk JS account-add / harvesting / Nopecha captcha-solving.** Farm/ban-evasion tooling. Off-brand for a clan-distributed Store product whose whole narrative is "transparent tool, not malware."
- **No browser-UA spoofing.** UA stays `RORORO/<version>` across every HTTP client. Transparent and identifiable is a deliberate Roblox-relations + Store-trust choice.
- **No plaintext / portable-at-rest vault tier.** RAM offers password-encryption-at-rest as a portability feature; our answer is per-machine DPAPI with *one* deliberate, secured hole — the passphrase-encrypted export (AES-GCM, no oracle). We don't loosen the at-rest boundary; we provide a sanctioned, auditable transfer.
- **No cross-machine cookie sync via file copy.** DPAPI is per-user/per-machine *by design*. `accounts.dat` on another PC can't decrypt — that's the access boundary, not a bug.

The through-line: every "no" above is a capability that, if shipped *in the Store binary*, would either risk the Store listing, risk Roblox relations, or risk the user's account. The plugin system exists so the answer to "can RoRoRo do X" is "not in the Store product — but yes, as a signed sideload plugin you choose to install." That's a posture no unsigned grab-bag rival can offer.

---

## Outdo thesis — the sharpest leapfrog moves, ranked

Each tied to a moat a rival cannot copy without rebuilding their architecture from the foundation up.

1. **Remote-config the mutex name — turn our biggest claimed-but-unbuilt gap into the resilience win of the whole field.** Moat: Store-signed Velopack + remote config. A Roblox mutex rename is the one event that hard-bricks MultiBloxy and forces every unsigned fork to rebuild. We'd recover in hours via a config push, arriving *trusted*. This also closes a real spec/code drift (CLAUDE.md says config-driven; code hardcodes). **Highest leverage, lowest-to-medium effort, must-do.**
2. **Batch re-auth queue instead of auto cookie-refresh — the maturity move RAM's architecture can't make.** Moat: lazy-validation discipline (we already removed startup-validate for anti-fraud reasons). We give the staleness win without the 2FA-flag liability RAM never noticed it carries. **High value, medium effort.**
3. **Own the already-running case — as a sideload plugin.** Moat: the capability-gated plugin lane. RAM's Handle64 needs admin + an unsigned binary = trust-killer on the clan's machines, and the technique (foreign-process handle manipulation) can't live in a Store binary regardless of elevation. We put it where the wall was built to hold it: a consent-gated, SHA-verified plugin running as its own process. The signed Store product never grows the surface; the power user opts in. **High value, medium-high effort — spike it as a plugin (Q1).**
4. **Server-selection UX (browser + pop/ping/region + smallest-server + job-ID co-locate) folded into the account roster.** Moat: we already own the account + presence + saved-server surface; RAM has the browser but utility-tier UX, and no signed distribution. We'd be the only tool with a polished, branded server picker *attached to per-account launch*. **Genuine UX gap, medium effort.**
5. **"Yes to power, no in the Store binary" — the plugin wall as the trust differentiator.** Moat: capability-gated gRPC + contract NuGet + DPAPI consent. The answer to every dangerous feature request (macros, bulk import, AFK) becomes "as a signed sideload plugin you choose," keeping the Store product clean. No rival can separate the dangerous breadth from the safe core because no rival built the wall. **Already built — the work is framing + author ecosystem, low effort, compounding leverage.**

---

## Open questions for the review

Genuine forks. These decide scope before this becomes a checklist.

1. **Handle64-equivalent: plugin-lane spike, or shelve?** Two things are now resolved. (a) A non-elevated same-user path *does* exist (`PROCESS_DUP_HANDLE` + `DuplicateHandle(DUPLICATE_CLOSE_SOURCE)`); RAM's admin requirement is a convenience from `handle64.exe`'s system-wide scan, not a hard floor. (b) It can **never** live in the Store binary — foreign-process handle manipulation collides with cert + AV heuristics, and the elevated variant is an outright Store rejection. So the fork is narrower than first framed: time-box a spike to build it as a **capability-gated sideload plugin**, or shelve it and accept "quit both to recover" as the documented v1.7 trade? (Not "core vs trade" — core is off the table.)
2. **Roblox version pinning — confirmed AVOID, not just declined.** Fishstrap/Voidstrap let users pin/downgrade Roblox versions to dodge a breaking update. The gate hardens the lean into a rule: a version downloader = installing Roblox-client binaries we didn't author, which collides with Store policy 10.2.3 (no offering to install secondary software not developed by you) and edges toward the modified-clients risk. Not a plugin re-route either — pinning a foreign game client isn't power the plugin lane should legitimize. The remote-config drift banner + fast Velopack fix is the whole answer. Confirm we close this?
3. **Single cookie paste-import: ship it, and how guarded?** Gate ruling: it's Store-shippable — no elevation, no dynamic code, so it does *not* force the plugin lane — but it's the **highest review-risk shape in the set** (a "paste your login cookie" UI pattern-matches credential-theft tooling to human + automated reviewers). It survives *only* behind the mitigations: an "advanced" gate (default stays WebView2 login), the mandatory 10.5.1 privacy policy, 10.5.4 DPAPI-at-rest, 10.5.5 explicit consent at import, no off-device transmission, and the reviewer-letter pre-emption. Ship it with all of that, or hold the line at WebView2-only? Either way, **bulk / farm-scale import stays plugin-only by construction.**
4. **RAM trim / working-set clear: confirmed plugin-or-never, not core.** Low value on modern hardware, but the clan runs many instances on possibly-modest rigs. Gate reinforces the lean: `EmptyWorkingSet` / `SetProcessWorkingSetSize` touches a *foreign* process (the same AV-heuristic shape as Handle64), so if it ever ships it's a capability-gated out-of-process plugin — never core. Agree we never burn core trust surface on it?
5. **Launcher composition / coexistence: do we say anything?** RAM routes launches *through* Bloxstrap/Fishstrap. We won't be a FastFlag launcher, but "RoRoRo + your FastFlag tool coexist fine — different lanes" is a real talking point (the mutex hold is bootstrapper-agnostic). Worth a sentence in the docs/listing, or does naming rivals muddy the brand?
6. **Localization: v1 decline confirmed?** MultiBloxy (EN/RU) and the Bloxstrap family localize. The Pet Sim 99 / Store-v1 audience is English-first. Lean: **decline for now, revisit if clan distribution goes international.** Confirm the punt?
7. **Mutex-only background "light mode": v2 conversation, or off the table?** It would let us answer MultiBloxy's lightness pitch (resident mutex hold + summonable UI) on our trust tier. Gate: the Store-legal mechanism is the `windows.startupTask` manifest extension with `EntryPoint=Windows.FullTrustApplication` (MSIX has no per-user service; not a Run-key or scheduled task — both draw heuristic scrutiny). Two design constraints to bake in now so v2 doesn't assume otherwise: the **user owns the kill-switch**, and **`DisabledByUser` is final** — the app may not re-enable auto-start, so it must degrade gracefully to "auto-start is off." Decoupling the resident hold from the WPF shell is real process-lifecycle work. Lean: **v2, park it** — but flag now so the architecture doesn't foreclose it.
8. **Sequencing for v1.7 vs later:** the cheap, low-risk wins (remote-config mutex name, process-based confirm read, pause/resume/reload, stop-all, last-used sort, staleness badge) cluster naturally. The big rocks (handle adoption spike, server browser, batch re-auth) are each their own cycle. Do we bundle the cheap cluster into the current install-deferral cycle's tail, or cut a dedicated "competitive parity" cycle after v1.7 ships?
