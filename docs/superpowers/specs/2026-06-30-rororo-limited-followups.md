# RORORO — Limited-cycle follow-ups (next-iteration candidates)

**Date:** 2026-06-30
**Status:** Backlog — NOT yet scoped. Captured at the v1.8 Limited (403 soft-lock) cycle wrap.
**Source:** PR #30 wrap conversation. Each substantial item needs its own `/scope` + design spec before build; the small mechanical ones are noted as such.

This is a parking lot, not a design. It exists so the next session picks up with full context instead of re-deriving it.

---

## 1. Reauth tag doesn't clear after enabling 2FA (concrete bug)

> **STATUS 2026-07-03 — fixed on `fix/reauth-2fa-tag`.** Root cause was sharper than hypothesis (a):
> the capture window validates the cookie via `GetUserProfileAsync` before returning Success, and on
> `CookieExpiredException` it closed itself mid-flow with a silent `Failed` — with 2FA, a
> not-yet-valid cookie at the challenge step tripped the capture, the window slammed shut while the
> user was mid-challenge, and `ReauthenticateAsync` silently swallowed the non-Success result. Fix:
> (1) capture window re-arms and stays open on a rejected/failed validation (debounced per
> rejected cookie value; immediate one-shot re-read closes the swallowed-nav-event race; closing
> the window after a rejection returns `Failed` with the reason instead of `Cancelled`; in-window
> hint appears on first rejection), (2) reauth surfaces Cancelled/Failed via StatusBanner
> (state-neutral copy — the button also shows on Limited rows), (3) identity guard refuses a
> wrong-account cookie overwrite, (4) opportunistic RobloxUserId backfill on reauth (soft-fail).
> Live 2FA repro still wanted to confirm the mechanism end-to-end. Follow-ups parked from the
> two review rounds: (a) extract `TryCaptureAsync`'s decision chain into a testable core (seam
> precedent: CookieCaptureSweepTests); (b) pre-existing — the `Contains("WebView2")` substring
> routing to the install-runtime modal misfires on messages embedding `webview2-data` paths or
> `CoreWebView2` exception text — route on a typed result instead; (c) residual edge, accepted:
> same-value cookie turning valid while its ONLY post-login nav event was swallowed mid-probe
> and the page then stays quiescent → manual close (window hint says so, close returns the
> reason).

**Symptom (user-reported 2026-06-30):** an account showed the reauth / "Session expired" tag after 2FA was enabled on the Roblox account. The user completed the re-auth (WebView2 re-login), but the tag did not clear.

**Not from the Limited work** — this is the pre-existing `SessionExpired` / re-auth flow, and it reproduced on the *shipped* build (not the `feat/limited-session-handling` branch). Enabling 2FA server-side invalidates existing Roblox sessions, which is why it went expired.

**Hypotheses (unverified — the shipped build doesn't log the reauth/capture flow, so no log evidence):**
- **(a, likely)** `MainViewModel.ReauthenticateAsync` only clears the tag when `ICookieCapture.CaptureAsync()` returns `CookieCaptureResult.Success`; otherwise it hits an early `return` and the tag stays. The 2FA step changes the WebView2 login page flow, so the capture may not recognize the completed login and returns non-Success.
- **(b)** Capture returns Success but the new cookie still 401s (2FA session weirdness), and a background validation / presence poll re-flags `SessionExpired`.

**Fix direction:** read `CookieCapture` + the capture-result handling; make the WebView2 flow handle the 2FA login; **validate the new cookie before clearing the tag**; and if reauth didn't take, surface "re-auth didn't complete" instead of silently leaving the tag. Needs a repro (add 2FA → reauth → observe). **Size: small–medium. Own fix, TDD-able around the capture-result branch.**

## 2. Roblox 0.727 tray-residence / orphan processes / mutex-ordering (the big track)

The *other* root cause from the original "work after the Roblox changes" ask — separate from the 403 soft-lock this cycle shipped.

**Facts gathered this session:**
- Roblox 0.727 (installed 2026-06-27) keeps the client **resident in the system tray** when the window is closed — it no longer fully exits.
- A tray-resident Roblox **holds the singleton mutex** (`Local\ROBLOX_singletonEvent`), so RORORO must acquire the mutex *first*; if Roblox is already tray-resident, multi-instance breaks.
- **Orphan windowless `RobloxPlayerBeta.exe` accumulate** — the machine had 13 processes for 6 accounts (6 real in-game ~2 GB windowed, 6 windowless ~180 MB orphans from failed/closed launches, 1 errored). "Must fully close Roblox from the tray before starting RORORO."

**Scope seeds (needs its own /scope + spec):** detect tray-resident/orphan `RobloxPlayerBeta`; surface them + offer cleanup; rework the startup mutex gate (`StartupGate` / `RobloxAlreadyRunningWindow`) for the now-common runtime "Roblox already running" case (the existing gate only guards at startup — see `docs/superpowers/specs/2026-05-08-roblox-already-running-detect-design.md`); decouple the "what's actually open" view from server-side presence (which is unreliable for flagged accounts). **Size: large.**

## 3. `roblox-compat.json` known-good bump 0.722 → 0.727 (cosmetic)

Installed Roblox is `0.727.0.7271199`; the published compat pin tops out at `0.722.0.7221024` (May 28), so `RobloxCompatChecker` shows a false drift banner. It gates nothing (banner only). **Fix: publish an updated `roblox-compat.json` to the GitHub release with `knownGoodVersionMax` ≥ 0.727. Trivial, no code.**

## 4. Limited copy / recovery guidance refinement

The shipped copy `Limited by Roblox — re-capture or wait` is half-right for the **join-gate flavor** (suspicious-activity verification): re-capture does NOT clear it (the captcha is a client game-join gate, not web-login — see the [bot-challenge investigation](../../investigations/2026-05-07-account-bot-challenge.md) 2026-06-30 entry). Consider flavor-aware copy: "solve the check in-client, or join via a friend, or wait." **Size: small (copy + maybe branch on the detection source).**

## 5. OPEN DECISION — "Join via friend" on Limited rows

Follow-a-friend joins bypass the client join-gate; RORORO already supports the path (`LaunchTarget.FollowFriend` → `RequestFollowUser`). A one-click "Join via friend" on Limited rows would turn a dead-end into an actionable launch.

**The tension (yours to decide, not to build unasked):** manual, user-initiated follow-join is clearly fine. A one-click "get a flagged account in without the verification" edges toward the *spirit* of the no-evade-Roblox-trust-gates wall, even though it breaks none of the letter (no automation, no injection, no captcha auto-solve, no spoofing). Also: it only helps the join-gate flavor (auth-ticket fetch still succeeds), and it does NOT clear the flag. **Decide before scoping.**

## 6. Stable per-account browserTrackerId (launch trust hygiene)

We generate a random 13-digit `browserTrackerId` **per launch** ([`RobloxLauncher`](../../../src/ROROROblox.Core/RobloxLauncher.cs)). A real client keeps a *stable* btid tied to the account; a fresh random one every launch reads as a brand-new, unfamiliar client. **Launch-only** — the btid is a launch-time URL param; a client-driven auto-reconnect does NOT carry it, so this does **not** help reconnect collisions (corrected 2026-06-30). Persist a per-account btid so each account reads as a returning client. Cheap, in our control, **unproven** (Roblox risk inputs aren't public — needs a real test). Healthy accounts already launch fine on the random btid, so this is trust-*reduction* hygiene, not a captcha fix.

## 7. Idle-timeout / synchronized-reconnect + the "stay active" split

**The problem (user-clarified):** accounts idle-time-out at ~20 min; a batch launches together, so their auto-reconnects fire together → a synchronized reconnect wave trips the trust gate (captcha/logout). RoRoRo has **no hook into a running client** (the reconnect is client↔Roblox), so it can't harden or de-stagger the reconnect itself.

**Corrected governance understanding (2026-06-30):** the "no macros" wall is **core-only**. The plugin system already hosts input automation via consent-gated `system.synthesize-keyboard-input` / `system.synthesize-mouse-input` capabilities — `rororo-ur-task` is the precedent (full macro suite, consent-sheet-gated). So keep-active is a **plugin**, not strictly MaCro-only.

**The split (user-confirmed):**
- **RoRoRo core (this repo) = awareness + notify.** Track per-account **last window-activity / time-since-last-activity** (core already tracks managed windows via the decorator / `RunningRobloxScanner`; this adds last-foreground/last-activity time). Expose it to plugins via a host event/capability. Pure observation — wall-clean, and useful beyond keep-active (feeds detect/surface for the Limited + reconnect story).
- **New simple keep-active plugin (separate repo, its own /scope) = acting.** A stripped-down `rororo-ur-task`: the minimum is *focus the window + press space (jump)*, per-account, only when idle past a threshold (driven by core's awareness). Declares `system.synthesize-keyboard-input` + window-focus + `host.events.account-launched/exited` + the new awareness capability; consent-gated like ur-task. **Simple-from-step-one UI** (on/off per account + idle threshold) — for users who want keep-active without ur-task's full button set. Power users stay on ur-task and record a focus+space macro.

**RoRoRo's other (imperfect) lever:** de-sync the initial launches so the timeout waves start offset — imperfect (game in-out moves can re-align idle timers) and trades off getting everyone in fast.

**Next step:** brainstorm → scope → spec, split across the core awareness capability (here) and a NEW plugin repo. `rororo-ur-task` is the architectural template (separate EXE, named-pipe host, consent-gated capabilities); `rororo-ur-ocr` (mentioned, not cloned locally) is another family sibling.

---

## Related records

- Shipped this cycle: PR #30 (`feat/limited-session-handling`), spec `2026-06-29-rororo-limited-session-handling-design.md` (banner-corrected 2026-06-30), plan `2026-06-30-limited-session-handling.md`.
- Root-cause + correction decisions logged to the 626 dashboard (project RORORO).
