# Remediation waves — from F-103 onward

**Register at 2026-08-14:** 66 clean, 35 open, 4 closed (105 rows).
Waves 1–6 are shipped and clean; this plans 7 onward.

Findings are grouped by **what breaks them**, not by where they render. A wave that shares a root
cause can be fixed once and verified once; a wave grouped by screen re-derives the same fix five
times.

---

## Wave 7 — Lifetime: what happens after startup that nothing watches

**F-103** (4×4) · **F-099** (3×2) · **F-100** (1×3)

The anchor is F-103, and it earns first place on its own terms: *"this lands on the product's
entire premise."* RoRoRo exists so several accounts run at once and stay tellable-apart, the window
title **is** that affordance, and a Roblox self-update — the one event guaranteed to happen to every
user — silently removes it. The orphaned client still counts toward the running total and the memory
watchdog, but no row owns it, so Stop cannot reach it.

The other two share the shape: something recovers or changes after startup and the user is never
told. F-099 is a plugin failure that recovers invisibly; F-100 is process-global state behind a
null-conditional that silently disables itself.

**Why this wave does not need the instruments widened first.** F-103's own fix note says: *"Verify
by killing a decorated client and starting a bare `RobloxPlayerBeta`, not by reading the
decorator."* It is hand-verifiable by construction. That is the argument for taking it before the
instrument work rather than after.

**One caution, from the register's own words.** F-095, F-099, F-101 and F-103 were four
lifetime defects in a row, every one found by a human and none by an instrument. Two are now clean.
Fixing the remaining two without adding a watch means the fifth arrives the same way.

---

## Wave 8 — The instruments

**F-098** (2×4) · **F-105** (3×4) · **F-090** (3×3) · **F-050** (3×3) · **F-092** (2×3)

This is the cycle the other surface argued for, and today's evidence backs it: F-105's own verdict
is *"whether the suite has any authority left."* F-098 says every instrument was built by reading
the artefact it was meant to check. F-090 records **three separate checks each having a reason not
to see the same defect.**

F-050 and F-092 sit here rather than in the a11y wave on purpose: F-050's row looked resolved
because the gate got narrower, not because the defect left. It becomes *verifiable* only after the
gate widens, so fixing it first means fixing something the gate cannot confirm you fixed.

---

## Wave 9 — Naming and copy

**F-034** (4×4) · **F-061** (2×3) · **F-071** (1×3) · **F-075** (0)

F-034 ties F-103 at the top of the whole register and is among the cheapest things here: the repo
name still ships in the tray menu and tooltip — the surface a tray-resident app shows most often.
Grouped together because they are one editing pass over strings with no shared code risk.

---

## Wave 10 — Dialogs and window conventions

**F-041** (3×4) · **F-048** (3×3) · **F-079** (2×3) · **F-060** (2×3) · **F-054** (2×3) · **F-084** (3×4)

F-041 is the root: the most-repeated element in the app — a Close button, nine of them — has no
agreed weight, size, or default-button behaviour. F-048 (resize not derived from window kind),
F-079 (six modals opting out of theming), F-060 (window size forgotten) and F-054 (InputBindings)
are the same absence showing up in different properties.

F-084 rides along because it is also window ownership: a tray-invoked dialog raised while RoRoRo is
minimized becomes unreachable.

---

## Wave 11 — The accessibility floor

**F-044** (3×4) · **F-045** (3×4) · **F-052** (2×4) · **F-102** (1×3) · **F-072** (1×2) · **F-073** (1×2)

**F-102 is ranked 3 and should be treated higher than its rank.** It is a *privacy* control — the
streamer-mode toggle reports engaged while disengaged. A privacy switch that lies about its own
state is a different category of defect from a small font.

---

## Deliberately unscheduled

**F-013** (4×3) — six modal islands, going modeless. The row itself names a hard prerequisite, which
makes it `[new-mechanism]`: it gets its own decision, not a slot in a re-style batch.

**F-083** (2×4) — adaptive client footprint. Already scoped
(`2026-08-09-rororo-adaptive-footprint-scope.md`) and waiting on measurements from other hardware.

**F-038, F-039, F-018, F-057, F-059, F-067, F-093, F-094, F-096** — real, none sharing a root cause
with the above. They are the residue wave, taken last or folded in where they touch a surface a
wave is already opening.

---

## Order, and why

7 before 8 because F-103 is hand-verifiable and touches the premise. 8 before 9–11 because it is
what makes the later waves' claims checkable. 9 is cheap and could be slotted anywhere.
