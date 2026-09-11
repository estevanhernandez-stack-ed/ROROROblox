# RoRoRo — external metric alerts: a signed manifest, a plugin, and one new alert kind

> Design, 2026-09-09. Status: **draft for review, not approved.**
> Origin: Este — clan battles are monitored by hand today, and the clan posts in Discord when a
> member's contribution rate drops. The ask is for RoRoRo to notice first.

---

> **DECISION CORRECTION (2026-09-11) — the signed manifest is dropped; no reference plugin ships.**
>
> **Originally proposed** (§1.3, §1.5): a 626 Labs–hosted, signed manifest supplying a discovery
> request, a fetch template, an extract path, and rule parameters for whatever game the user points
> RoRoRo at — signed because, per §1.5, it "names URLs the plugin will call."
>
> **Why it was dropped, on two findings (Este's call):**
>
> 1. §1.5 and §1.6 contradict each other. §1.5 signs the manifest because it names the URL the
>    plugin calls; §1.6 has the user enter that URL in plugin settings. If the user supplies it,
>    the manifest names no URL, and the stated reason to sign one evaporates.
> 2. The plugin/core split in §1.2 already made the manifest's job disappear. Of its four payloads
>    — a discovery request, a fetch template, an extract path, and rule parameters — the first
>    three belong wholly to the plugin, which the user builds and updates freely, and the fourth
>    duplicates the local rules file (`LocalFileMetricRuleSource`) that already ships.
>
> **Consequence.** Dropping it is the strongest form of the separation §1.6 is protecting: 626 Labs
> now hosts nothing about any game at all, rather than hosting a file that describes one. The
> honest cost: a field-path change now means every user updates their own plugin, instead of one
> re-signed manifest reaching everyone. Full account:
> [docs/superpowers/plans/2026-09-11-metric-alerts-close-out.md](../plans/2026-09-11-metric-alerts-close-out.md#the-decision-this-plan-records).
>
> §0 below is unaffected — it is a record of live-verified facts, not a proposal. Do not rewrite §1
> or §2; this banner and the two shorter ones at §1.3 and §1.5 are the correction.

---

## §0 What was measured before designing

Every fact below was checked against the live API or the repo, not assumed. Two of them
contradict the vendor's own published example, which is why they are recorded here.

1. **The failure being detected is not disconnection.** Este, asked directly what the clan finds
   when it pings someone: "macro drifted away from zone, wrong loadout, hasn't upgraded, hasn't
   hatched stronger pets... Sometimes disconnected, but we have that covered." Presence-as-truth
   and the drop-out alert already handle the disconnect case. The remaining cases are an account
   that is genuinely in the game and genuinely earning too slowly, which no existing signal sees.

2. **On-screen OCR was considered and rejected.** Ur OCR already reads the screen, and diamonds
   are permanently in the HUD, so diamonds-per-minute looked like a local proxy needing no API at
   all. Este: battles "have their own scores... typically not in the same area that you score
   points in." A proxy that decouples from the metric under exactly the conditions being measured
   is worse than no detector, so this is not a fallback either.

3. **Per-member battle points ARE reachable for any clan, without auth or sampling.** Verified
   live 2026-09-09 against `ArcadeBattle2026`:
   - `GET https://ps99.biggamesapi.io/api/activeClanBattle` → `configName` of the live battle.
   - `GET https://ps99.biggamesapi.io/api/clan/{name}` → `Battles.<configName>.PointContributions[]`,
     75 entries of `{UserID, Points}`, plus clan `Points` and `Place`.
   - Confirmed working on clans at rank ~1200 of 169,161, which settles the sampling question:
     the **`/v1/clans/*`** aggregates sample the top 25/100, the **legacy `/api/clan/{name}`**
     lookup does not. The legacy family is documented as "fully supported and not deprecated."

4. **The vendor's documented response shape is stale.** `legacy/README.md` shows
   `Contribution.Battle[]`. The live response has no `Contribution` key at all. Anyone coding
   from the example gets an empty array and concludes the clan has no data. Recorded because it
   is the single most likely way an implementer loses an afternoon.

5. **The value is cumulative, not a rate.** `PointContributions[].Points` is points-so-far-this-
   battle. Points-per-minute is a derivative the client computes from two samples. This is the
   fact that forces local history into the design.

6. **Freshness is adequate and was the thing most likely to kill the feature.** Clan endpoints
   carry a 3-minute server cache (`max-age=60, s-maxage=180`). They do NOT share the per-player
   refresh quota (48/day, one per 30 min) that governs `/v1/players` and `/v1/account`. A ten
   minute rate window is therefore genuinely measurable.

7. **The alert rails already exist.** `AlertRouter.Route` does per-account mute, per-(account,
   kind) cooldown, coalescing and fallback-to-Local; `AlertDispatcher` fans out to toast, Discord
   webhook, or phone. `AlertKind` currently holds `AccountDroppedOut`, `MemoryWarning`,
   `Recycled`, `UptimeMark`. Phone alerts (spec 2026-09-04) rode these rails as a new
   *destination*; this rides them as a new *kind*.

8. **The remote-config-with-fallback pattern already exists and is signed.** The mutex name comes
   from `roblox-compat.json` — remote, signed, verified against a pinned key, resolving
   remote → last-known-good cache → hardcoded default, with `compat.yml` able to re-sign and
   re-attach without shipping a binary. That is this problem exactly.

9. **Terms are restrictive and this shapes the architecture, not just the paperwork.** The PS99
   API TERMS.md: *"provided for non-commercial and personal use only. Commercial use of the API
   or any of its data is strictly prohibited without prior written consent."* RoRoRo is free but
   is distributed on the Microsoft Store by 626 Labs LLC. Attribution is separately required
   (clause 3) for public display of their data.

## §1 The decision

**A signed metric manifest, a plugin that fetches and extracts, one new alert kind in core, and
no vendor-specific anything in the shipped binary.**

1. **`AlertKind.MetricBreach`** joins the enum. It rides the existing router unchanged — same
   mute, same per-(account, kind) cooldown, same coalescing, same destinations. A breach reaches
   the phone through machinery that already works and is already smoke-tested.

2. **The plugin fetches; core decides.** The plugin polls, extracts a number, and reports
   `(subjectId, metricId, value, observedAt)` over a new RPC. Core keeps the short history,
   computes the rule, and raises the alert. This split is deliberate: thresholding in core means
   the mute/cooldown/coalescing guarantees hold for metric alerts exactly as they do for every
   other kind, and a misbehaving plugin cannot page someone every thirty seconds.

3. **The manifest describes the shape, and ships nothing about any game.** The binary contains no
   endpoint, no field path, no vendor name. The manifest supplies:
   - a **discovery** request, optional, whose result parameterises the main request (this is what
     makes the active-battle name resolvable without a rebuild),
   - a **fetch** request template,
   - an **extract** path to a list of `{subject, value}` pairs,
   - a **metric kind** and its rule parameters.

   > **Dropped 2026-09-11.** The plugin/core split in item 2 above left these four payloads with
   > nothing to do: the first three belong wholly to the plugin, which the user owns and updates
   > freely; the fourth duplicates the rules file `LocalFileMetricRuleSource` already reads. Full
   > correction at the top of this spec.

4. **Three metric kinds, one fetch machinery.** "Points per minute" is too narrow — Este: "the
   points aren't always the same. So we'll have to pull some other events as they happen."
   - `rate` — value climbs; alert when the derivative over a window falls below a floor.
   - `level` — value is absolute; alert when it crosses a threshold in a stated direction.
   - `event` — a field changed; alert on the transition (place dropped, medal earned, battle over).

5. **The manifest is SIGNED, verified against the pinned key, with the same fallback chain as
   `roblox-compat.json`.** This is not ceremony. The manifest names *URLs the plugin will call*;
   an unsigned one is an attacker choosing this app's request targets, which is an exfiltration
   primitive rather than a config file. Resolution is remote → last-known-good cache → **nothing**
   (feature simply off), because unlike the mutex there is no safe hardcoded default and a
   metric feature that silently stops is a non-event.

   > **Dropped 2026-09-11.** This paragraph and item 6 below contradict each other: this one signs
   > the manifest because it "names URLs the plugin will call"; item 6 has the user enter that URL
   > in plugin settings. If the user supplies the URL, the manifest names none, and the signing
   > rationale here does not hold. Full correction at the top of this spec.

6. **The user brings the source.** The manifest ships from the 626 Labs feed describing *shapes*;
   the endpoint URL and the subject id (clan name, user id) are entered by the user in plugin
   settings. This is what keeps 626 Labs out of "commercial use of the API or its data" — the
   binary is a generic metric watcher, and the person calling Big Games is a clan member on their
   own machine for personal use. **The test to apply to any future change: does the shipped
   binary or the 626-hosted manifest name Big Games? If yes, the separation is a fig leaf.**

## §2 Rejected alternatives, with reasons

- **OCR the on-screen number (Ur OCR).** Rejected on §0.2: battle scoring is separate from where
  points are earned, so the only always-visible number decouples from the metric precisely when
  it matters. Attractive because it needs no API, no auth and no terms — recorded so nobody
  re-proposes it without knowing why it fails.
- **Ship the Big Games integration in the plugin.** Simplest to build and the most likely to age
  badly: it puts 626 Labs in the position the terms prohibit, and it hardcodes a shape the vendor
  has already changed once (§0.4).
- **Roblox Open Cloud / OrderedDataStore.** Owner-only — an API key is issued by the game's
  creator. Closed for a game you do not own, and worth recording because a plausible-sounding
  research summary pointed here.
- **Thresholding in the plugin.** Would let the plugin page directly and bypass the router's
  mute, cooldown and coalescing. The one guarantee this feature must not break is "a bad night
  does not become forty notifications."
- **Unsigned manifest.** Rejected in §1.5. The value being fetched is a URL, not a string.
  > **Still correct, 2026-09-11, even though the manifest itself is dropped:** this is exactly why
  > `LocalFileMetricRuleSource` ships unsigned. A rule names a metric id, a kind, a threshold and a
  > window — never a URL — so there is nothing here that signing would protect.
- **Alerting the clan leader about everyone (Este's "both, but definitely A").** Deliberately out
  of scope for this cycle. It cannot be done from the player's own machine — it needs a
  server-side watcher and its own privacy reckoning, and it shares the dead-PC gap already
  recorded as unscoped in the phone-alerts spec §2.

## §3 What deliberately does not change

- The four existing alert kinds, their triggers and their copy.
- Router semantics: mute, cooldown, coalescing, fallback-to-Local, streamer masking.
- The macro wall. This feature reads an HTTP endpoint the user configured. It synthesizes no
  input and injects nothing into any client.
- `roblox-compat.json` and its signing rig: the metric manifest is a *second* signed feed reusing
  the same key and the same verification path, not a change to the existing one.
- Store posture: no new outbound host ships in the binary, because no host ships in the binary.
  The reviewer letter says what the app does (watches a user-supplied endpoint) rather than
  naming a service.

## §4 Open questions for review

1. **Consent from Big Games.** Este is "not super worried," and the generic-shape architecture is
   the substantive answer. A one-line Discord ask still converts an argument into a written yes
   for the cost of one message, and RoRoRo has a 10.1.1.1 rejection in its history. Recommend
   asking; not a blocker on building.
2. **Where the manifest is hosted and how it is re-signed.** `compat.yml` already does exactly
   this for the mutex feed. Reuse it, or a sibling workflow?
3. **Whether `event` ships in the first cut.** `rate` covers the stated need. `level` is nearly
   free once `rate` exists. `event` needs change-detection semantics and its own copy, and may be
   a second cycle.
4. **Poll interval and politeness.** The server caches for 3 minutes; polling faster only burns
   the user's own bandwidth and looks like abuse under TERMS clause 4. Recommend a manifest-set
   minimum with a hard floor in core that the manifest cannot lower.

## §5 Test plan

1. **Unit** — rate computation across sample gaps including a missed poll and a counter reset;
   level crossings in both directions with hysteresis; router `MetricBreach` cases
   (configured/unconfigured/muted/cooled-down); manifest signature verification accept and reject;
   fallback chain remote → cache → off.
2. **Fences** — `RpcMethodCapabilityMapTests` and the harness `CapabilityMap_CoversEveryHostMethod`
   for the new RPC (absence is denial); a fence asserting no vendor hostname appears in the
   shipped binary or the 626-hosted manifest, which is §1.6's test made executable.
3. **Full suite** x64 + arm64 via CI.
4. **Live smoke (needs Este)** — a real clan, a real battle, a threshold crossed on purpose, and a
   phone that buzzes. Same gate discipline as the phone-alerts §4 smoke: the delivery legs were
   only believed once a real phone rang.
5. **Manifest rotation drill** — change a field path in the manifest, re-sign, and confirm a
   running install picks it up without a rebuild. That is the entire justification for the
   manifest; if it is never exercised it is speculative complexity.

## §6 Carried out of plan 1 — binding on plan 2 (added 2026-09-09)

Plan 1 (core: history, rules, coordinator, `AlertKind.MetricBreach`) landed on
`feat/metric-alerts-core`. Its final whole-branch review surfaced four things that plan 1 could
not fix because they live at the RPC ingress plan 2 builds. They are recorded here rather than in
a scratch ledger so plan 2 cannot miss them.

1. **The setting has no reader, and off-by-default currently rests on the wrong thing.**
   `IAppSettings.GetMetricAlertsEnabledAsync` exists and defaults false, but nothing consults it.
   The feature is off today only because `MetricBreachDestinations` is empty and routes nowhere.
   **Plan 2 must enforce the setting at the RPC ingress** — reject reported observations while it
   is off. If plan 3 populates destinations from a manifest without that gate, the toggle is
   decorative, and a user who turned the feature off still gets paged.

   > **Corrected 2026-09-11 (feat/metric-alerts-plugin-rpc):** The setting now HAS a reader,
   > enforced per report at the sink. The destination list now defaults to the local desktop toast.
   > Plan 2 wired the setting enforcement at the RPC ingress. Plan 3's task is now limited to the
   > settings control for routing a breach to destinations other than that toast.

2. **A clock-skewed reporter silently disables Rate rules and only Rate rules.**
   `MetricHistory` filters samples against the host clock, while `ObservedAtUtc` comes from the
   reporter. A plugin that sends local time in a UTC+2 zone puts every sample in the future, the
   in-window set is empty, the rate is forever null, and Rate never fires. Level and Event keep
   working, because neither consults time. The failure shape is "the feature half-works and nobody
   can tell." Plan 2's RPC must reject or clamp future-dated observations, and say so in a log line.

3. **The series dictionary is bounded per key, never by key count.** Each series is capped, but a
   reporter whose `metricId` varies — a server id, a session suffix — or observations for deleted
   accounts grow the dictionary for the process lifetime of a tray app that runs for days. Plan 2
   validates metric ids at ingress, which is the cheap place to bound this.

4. **`WebhookPayload` formats in the current culture** (pre-existing, not introduced here). A
   French-locale user's clan channel already reads "4,1 GB" for the memory warning, and will read
   "0,79" for a metric. Worth its own decision against the production formatter; it is not a metric
   alerts problem and was deliberately not patched on this branch.
