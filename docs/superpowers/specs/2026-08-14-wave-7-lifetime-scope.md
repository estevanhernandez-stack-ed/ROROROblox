# Wave 7 — Lifetime: what happens after startup that nothing watches

**Findings:** F-103 (4×4), F-099 (3×2), F-100 (1×3)
**Branch:** `glow/wave-7-lifetime`

**One sentence:** a Roblox self-update orphans a client permanently, and RoRoRo has been logging
the symptom into its own file the whole time without acting on it.

---

## F-103 — the mechanism, confirmed in the tree

Three facts, each verified rather than assumed:

1. **`RobloxWindowDecorator` keys targets by the pid RoRoRo launched.**
   `Track(pid, summary)` writes `_targets[pid]`; the 1.5s reapply timer walks only `_targets.Values`;
   `ApplyOnce` **removes** a pid once `HasExited` (`:127-129`). Nothing adopts a pid we did not start.

2. **The title is the re-attach key.** `RunningRobloxScanner` maps a window back to an account by
   parsing `"Roblox - {name}"` (`:97`). A bare `Roblox` title is unattributable by construction.

3. **The scan runs exactly once.** `App.xaml.cs:428`, at startup. Nothing enumerates Roblox
   processes again for the rest of the session.

So when Roblox restarts itself: the old pid exits → `Untrack` → the new pid appears bare → nothing
looks for it → it stays bare forever. It still counts toward the running total and the memory
watchdog, but no row owns it, so **Stop cannot reach it**.

The app already prints the symptom: `"Untagged Roblox process pid {Pid} title='{Title}'"` (`:102`).

---

## What ships

### 1. Succession attribution

The tracker already raises `ProcessExited(accountId, pid)`. Keep a short-lived record of recent
exits — account, pid, when. When an unattributed `RobloxPlayerBeta` appears within the succession
window, attribute it to the account whose client just left.

**Attribute only when it is unambiguous.** One recent exit and one new orphan is a succession. Two
of each is a guess, and a wrong guess puts someone's alt under another account's name — worse than
leaving it unlabelled, because the label would be believed.

### 2. A periodic orphan sweep

`Scan()` already enumerates every `RobloxPlayerBeta` and already identifies the untagged ones. It
needs a driver beyond startup. Slower than the decorator's 1.5s title tick — process enumeration is
not free, and a self-restart is not a sub-second event.

### 3. Never leave the window bare

If attribution fails, still write a title the user can act on. F-103's own words: *"an 'unknown
account' label the user can resolve beats a window that silently claims to be nobody."*

### DECISION NEEDED — do we re-title a client we did not launch?

The scanner's existing comment says an untagged window is *"likely launched outside ROROROblox."*
Those are real: someone opens Roblox from the desktop while RoRoRo runs.

Re-titling a process we never started is a behaviour change, and it is the one part of this that
could annoy rather than help. Three options:

- **(a) Adopt everything untagged.** Simplest, and satisfies "never bare" literally. Also renames a
  window the user opened deliberately and may not want us touching.
- **(b) Adopt only successions** — an orphan that appeared right as a tracked client exited. Leaves
  a manually-launched client alone, which is arguably correct, but leaves it bare, which F-103
  explicitly argues against.
- **(c) Adopt successions silently; label other orphans only after they persist.** Two behaviours,
  more code, but it distinguishes *"this is yours and Roblox restarted it"* from *"something else is
  running."*

**Recommendation: (b) for this wave.** It fixes the defect F-103 actually describes — the guaranteed
event, the self-update — without changing how RoRoRo treats windows a human opened. (a) and (c) are
a separate decision about whether RoRoRo manages all Roblox clients or only its own, and that is a
product question, not a bug fix.

---

## F-099 — a recovered failure the user never sees

A plugin failure that recovers invisibly. Same family: something happened after startup and nobody
was told. Surfaced where the user already looks rather than in a new place.

## F-100 — a null-conditional on process-global state

An off switch nobody chose. Small, mechanical, in the same lifetime area.

---

## Testing

The fix note is explicit and it governs: **"Verify by killing a decorated client and starting a bare
`RobloxPlayerBeta`, not by reading the decorator."**

- Succession attribution is pure logic given (recent exits, current orphans, clock) — unit-tested
  directly, including the ambiguous case where it must decline.
- The sweep's cadence and the succession window are values, not behaviour: assert the decline path
  and the attribute path, not the timer.
- **Hand-verified before merge**, per the fix note. A green suite here proves the logic, not that a
  real self-restart is adopted.

## The caution this wave must answer

F-095, F-099, F-101 and F-103 were four lifetime defects in a row, every one found by a human and
none by an instrument. Fixing the last two without leaving a watch behind means the fifth arrives
exactly the same way.

Cheapest honest watch: the app already logs `Untagged Roblox process pid …`. **Something should
count those and say so** rather than writing them at Debug where nobody reads them. That is not the
full instrument wave (that is wave 8) — it is one line that turns an existing silent symptom into a
visible one.
