# Smoke checklist — trust-aware squad launch

**Branch:** `feat/trust-aware-squad-launch` · **Spec:** [`../specs/2026-07-09-trust-aware-squad-launch-design.md`](../specs/2026-07-09-trust-aware-squad-launch-design.md)

Store/plan/gate logic is unit-tested; the orchestration + UI below is the human pass. Needs live
Roblox + the clan private server + at least one challenge-prone account.

## Setup
- [ ] Quit installed RoRoRo (single-instance); run the dev build; 3+ eligible accounts, PS saved.

## Toggle + persistence
- [ ] **1.** Right-click a row → "Join via friend" → check appears; restart the app → still checked.
- [ ] **2.** Export accounts → import on the same box → flag survives.
- [ ] **2b.** Right-click menu: toggling shows/clears the ✓ prefix on the menu item (WPF-UI theme renders it).

## Squad launch — the real test
- [ ] **3. Zero flagged, careful off:** squad launch behaves EXACTLY as before (5s cadence, same banners).
- [ ] **4. Flagged path:** flag a challenge-prone account → squad launch → direct accounts go first,
      banner shows "Waiting for a squad member to land…", then "[flagged] joining via [anchor]…" —
      and the flagged account lands IN THE SAME private server, no CAPTCHA.
- [ ] **5. Fallback:** flag ALL accounts → squad launch → banner explains no anchor; everyone joins
      direct with the normal throttle. Nobody is skipped.
- [ ] **6. Careful mode:** enable the checkbox → cadence visibly serializes (each account lands
      before the next dispatches); toggle persists across app restarts.
- [ ] **7. Banner truthfulness:** every phase narrated; timeout fallback says so.

## The success metric (spec §7)
- [ ] The challenge-prone accounts land first-try, zero CAPTCHAs, across several days of dailies.

## Result
- [ ] All pass → merge-ready. Anything off → check + observation → fix pass before merge.
