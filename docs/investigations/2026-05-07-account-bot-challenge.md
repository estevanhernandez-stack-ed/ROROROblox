# Account bot-challenge investigation

**Branch:** `fix/account-bot-challenge`
**Opened:** 2026-05-07
**Status:** Phase 1 reopened (2026-05-07, evening). CAPTCHA recurred on ELeonDog after the morning's solve. "Trust sticks" was wrong; age-verify is not a load-bearing lever. Roblox-side gate confirmed; specific input still uncertain. UX fix scope unchanged.

## Symptom

When all 6 saved accounts are launched simultaneously via RORORO, **one account (ELeonDog) consistently lands on Roblox's "Verifying you're not a bot" CAPTCHA screen** before the game loads. The other 5 accounts (estehernandez, CEICPapa, ItsJustEste, ESTEHERNANDEZ, plus the other) launch into the game without challenge.

Reference screenshots: `Screenshot 2026-05-07 120001.png`, `Screenshot 2026-05-07 121352.png` (in `~\OneDrive\Pictures\Screenshots 1\`).

## Findings

**2026-05-07 — Bisect complete.**

- CAPTCHA fires on ELeonDog when launched via the **official Roblox path through Chrome** (Play-button → `roblox-player:` URI handoff) — not just through RORORO.
- CAPTCHA does **not** fire when logging into the roblox.com website itself with the same account.
- → The trigger is Roblox's **client-launch trust gate**, separate from (and stricter than) the web-auth gate. Web auth doesn't run Hyperion / Arkose pre-checks; client launch does.

**What this rules out:** H2 (RORORO-side auth-ticket flow). Chrome's Play button takes the same `roblox-player:` path, gets the same challenge — RORORO is not the trigger.

**What this rules in:** H1 (account-trust score on Roblox's side) is now the leading track.

**Implication for fix scope:** UX, not code-path. Surface the challenge cleanly, never auto-retry the auth-ticket flow when a challenge is in flight (would burn trust further), document the warm-up path. Anti-cheat challenges are intentional walls — RORORO does not bypass them, full stop.

**2026-05-07 — Trust-stick test passed.** Solved the CAPTCHA on ELeonDog once. Logged out, logged back in — no second challenge. Roblox caches trust by device, and it survives logout/login. → One-time per-device warmup. UX fix is sufficient; no "warm up your account" path needed.

**2026-05-07 — Confound noted (correction).** Age-verifying ELeonDog and completing the first successful CAPTCHA solve happened close together. Both factors changed near-simultaneously, and we can't separate them with this evidence. Two variables were in flight; one outcome. Age-verification is **not** ruled out — it may be a precondition that lets a solve actually build trust, or it may be independent. Cleanly isolating this would require an experiment on a real account (un-verify, see if challenges return forever), which isn't worth the cost. We accept the uncertainty.

**Adjacent insight worth recording:** the prior "challenged on every launch, forever" history is consistent with **trust never building because no CAPTCHA was ever completed** (puzzles were too frustrating to finish). Once one completed, trust engaged. This reframes "Roblox is broken on this account" as "Roblox was waiting for a successful proof that never came" — a meaningful shift for how we explain this to clan-mates.

**2026-05-07 (evening) — Recurrence; "trust sticks" retracted.** ELeonDog hit the CAPTCHA again later in the day. So the morning's "logout/login carried it through" was a within-session or short-window cache, not durable trust. **Age-verification is not the load-bearing lever** (user-called) — verifying did not prevent recurrence. What we know now:

- Roblox-side gate at client launch is real (Chrome bisect still holds).
- Solving once builds *some* trust, but it decays / is re-evaluated on a shorter time window than we thought.
- Whatever signal flags ELeonDog specifically is still active: account-trust score (account age, payment history, friend count, organic login pattern, IP-history familiarity, device-fingerprint familiarity in a 6-account cluster) is the leading bucket. Age-verify status was one input we could test cheaply; ruling it out narrows H1 but doesn't kill it.
- **Practical implication for fix scope:** unchanged. The UX fix (surface clearly, suppress retries, document) works whether the trust window is 30 minutes, 6 hours, or rolling. We were never going to bypass the challenge — we were going to make it un-frustrating to solve when it fires.

## What's already been tried

- **Age verification.** ELeonDog was the only account not age-verified. User age-verified it. The CAPTCHA still fired immediately after, and **recurred later the same day** even after a successful solve. → Age verification is not a load-bearing lever for this trust gate. Marked off the track 2026-05-07 evening.
- **Bisect via Chrome.** Same CAPTCHA fires through the official Chrome Play-button path. → Not RORORO-specific.
- **Solving the CAPTCHA once.** Cleared the challenge and trust persisted across an immediate logout/login. → Did NOT persist for the rest of the day. Trust window is shorter than "once and done"; treated as a per-session or short-rolling cache, not durable.

## Reproduction (to confirm)

- [ ] Launch all 6 accounts via RORORO, fresh boot — does ELeonDog hit CAPTCHA every time?
- [ ] Launch ELeonDog **alone** via RORORO — does it CAPTCHA when no other accounts are running?
- [ ] Launch ELeonDog **first** (before any other account) via RORORO — does order matter?
- [ ] Launch ELeonDog **last** — same question, opposite end.

## Hypotheses (ranked)

### H1 — Roblox-side: account-trust score at the client-launch gate (LEADING)

Roblox's risk model at the client-launch boundary takes many signals: account age (days since creation), login history, friend count, payment / Premium status, prior-challenge solves, IP-history familiarity, device-fingerprint familiarity. When 6 accounts launch from one device + IP within seconds, the model challenges the **lowest-trust** account in the cluster.

**Evidence for:** Single account out of 6, cluster-launch context, **CAPTCHA fires through Chrome too (bisect 2026-05-07)** — confirms server-side gate, not client-side code. Trust persists once built (logout/login carries it).
**Evidence against:** None.
**Open variable inside H1:** which inputs are load-bearing for *this* account. Age-verification status is OUT (recurrence after age-verify confirms it's not the lever). Remaining candidates: account age in days, friend count, Premium / Robux purchase history, organic login pattern, IP-history familiarity, device-fingerprint familiarity in the 6-account cluster, and the trust window's actual length. Not separable without burning a real account in an experiment, so we keep the bucket and ship the UX fix that works regardless of which specific input is load-bearing.

### H2 — RORORO-side: auth-ticket flow specific to one cookie (ELIMINATED)

~~Something about ELeonDog's stored cookie or the auth-ticket exchange we do for it triggers Roblox's bot heuristic.~~

**Eliminated 2026-05-07** by the Chrome bisect. Same `roblox-player:` URI handoff via Chrome reproduces the CAPTCHA without RORORO in the loop.

### H3 — Cookie-capture context drift

ELeonDog's `.ROBLOSECURITY` was captured longer ago / from a different IP / during a session Roblox no longer trusts. Roblox sees a stale-context cookie reuse and challenges.

**Evidence for:** Plausible if the user remembers capturing this cookie under different conditions.
**Evidence against:** None yet — needs cookie-age comparison.

### H4 — Cluster-launch heuristic targeting the newest add

If ELeonDog was the most recently added to RORORO (or has the lowest activity history on this device), Roblox device-fingerprinting may correlate "this device just gained a new account" → challenge.

**Evidence for:** Compatible with H1.
**Evidence against:** None yet — needs the order-independence test.

## Diagnostic plan

### Done — the clean bisect (2026-05-07)

Launched ELeonDog through Chrome's Play-button. Same CAPTCHA. Roblox-side gate confirmed. RORORO-side eliminated.

### Next — does solving once warm up trust?

Solve the CAPTCHA on ELeonDog **once**. Within the next 30-60 minutes:

1. Close ELeonDog cleanly (no force-quit).
2. Re-launch via RORORO — CAPTCHA again, or straight in?

Two outcomes:

- **Trust sticks** → one-time per-device warmup. UX fix is sufficient: surface clearly, suppress auth-ticket retries while a challenge is in flight, document. This is the desirable path.
- **Trust doesn't stick** → account is in a chronically low-trust state. User probably needs to play it solo for a session or two to build organic history. RORORO can't shortcut this; we surface and link out to "warm up your account" guidance.

### Supporting probes

- **Solo-launch test:** does ELeonDog still CAPTCHA when launched as the only account (others closed)? If yes → not cluster-driven, account-trust alone. If no → cluster signal is part of the trigger.
- **Order-independence test:** does CAPTCHA timing change with launch order? (Launch ELeonDog first vs last in the 6-account batch.)
- **Compare account profiles** (user-supplied): account age in days, friend count, Premium status, total Robux spend, last-login-before-RORORO. Even if Roblox doesn't expose a trust score, these are the inputs we can compare across the 6.
- **Compare cookie ages:** if RORORO stores a capture timestamp per account, is ELeonDog's the oldest? Stale cookies are a weaker hypothesis after the Chrome bisect (Chrome used a fresh login, still CAPTCHAed) but worth a glance.

## Open questions

Resolved 2026-05-07:

- ~~Does ELeonDog CAPTCHA via the official launcher?~~ **Yes (through Chrome).**
- ~~Does trust stick after one solve?~~ **Within session yes; across the day NO** — recurrence proved the cache is short. Retracted.
- ~~Is age-verification a precondition for trust?~~ **No** — recurred after verification. Off-track.

Still genuinely open (kept on the list, not ruled out):

- **What's the actual trust window?** Roughly how long between "solved" and "challenged again"? Worth tracking informally on the next recurrence — note the time between the solve and the next CAPTCHA. Not load-bearing for the UX fix; useful color for the README warm-up note.
- **Does the cluster-of-6 launch matter, or is it pure account-trust?** Solo-launch test would tell us. Not load-bearing for the fix scope.
- **Is ELeonDog the lowest-history of the 6** (account age, friend count, payment history)? Useful color when explaining the pattern to clan-mates; not load-bearing for the fix.

## What we'll likely ship

Bisect resolved Roblox-side, so the fix lives in UX:

- **Surface the challenge clearly.** Pre-launch state in the RORORO panel: when a launched client lands on the CAPTCHA, the row should reflect it ("Verifying — solve in window") rather than just "Running." Removes the "why isn't this account loading" question.
- **Suppress auth-ticket retries while a challenge is in flight.** If we re-call `authentication-ticket` while the user is mid-puzzle, we burn freshly-built trust and may cascade into a stricter challenge. One ticket per launch attempt; if the user closes the challenge unsolved, that's a manual relaunch.
- **README + first-launch guidance.** Plain-language note that low-history accounts may be challenged once on a new device; solving once usually clears it. No apologies, no bypass language.
- **Out of scope:** any auto-solve, header spoofing, or retry-on-challenge. Anti-cheat challenges are intentional walls; we sit politely on this side of them.

## Out of scope (for this branch)

- The per-account FPS limiter work on `feat/per-account-fps-limiter` is unrelated.
- Any change that auto-solves CAPTCHAs. RORORO does not automate game/security challenges. If a challenge fires, the user solves it. That wall is intentional.
