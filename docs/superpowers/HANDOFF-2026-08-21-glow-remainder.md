# Handoff — the seven rows the glow campaign has left

**Written 2026-08-21, at the end of wave 21.** Register is
`docs/superpowers/research/2026-08-04-rororo-settings-ui-audit-findings.md`.
Scoreboard at handoff: **105 clean · 7 open · 7 closed-as-ruled · 119 total.**
Suite: **1798 unit + 22 integration green.** Branch `glow/wave-21-remainder`, PR #132.

## Read this first, or the register will lie to you

Four rules the campaign learned the hard way. They are in CLAUDE.md too, but these
are the ones that actually cost waves:

1. **Re-derive every tally from the rows.** Never carry a number forward from a
   previous wave's summary.
2. **Verify against the tree, never a changelog.** "Wave N closed this" is not
   evidence. Read the code, and where the row's evidence is a runtime measurement,
   *measure the runtime*.
3. **Check the instrument before working the number.** This is wave 21's lesson and
   the most expensive one available. F-052 was measured by a XAML regex whose count
   disagreed with the row's own UIA evidence — 26 of its 49 were things no person
   could ever have named, while 56 genuinely silent controls scanned as fine. Two
   waves of work were priced against that number. **Before trusting any gate's
   count, confirm the gate measures the population the row is about.**
4. **The register is a parsed data file.** Three separate rows have been corrupted by
   characters inside prose: a missing why-column, a literal form feed (F-091), and
   markdown-escaped pipes (F-052/F-054). Do not put a raw pipe, form feed, or
   carriage return in a cell. A broken row silently drops out of every derivation.

## The seven, triaged

### Not startable here — do not batch these

| Row | Why it cannot close in this repo |
|---|---|
| **F-098** | Under a standing user ruling: History's Bookmark exemption waits for the **Store submission run**. Three of its sites already migrated. Leave it. |
| **F-096** | The fix it names — a named mutex on the plugin id — belongs to **`rororo-ur-task`, a separate repository**. What remains *here* is only its second sentence: whether `docs/plugins/AUTHOR_GUIDE.md` should require single-instance of every plugin. The guide already gained that section in wave 13. Re-read before assuming it is open. |

### The two test-infrastructure rows — cheap, and one just got smaller

- **F-107** — *halved by F-105 and nobody has re-measured it.* Its premise was that a
  dirty environment produces ~103 failures instead of one. **The RoRoRo-running half
  of that condition no longer exists** — wave 20 found the render host was executing
  the real `App.OnStartup`, hitting the single-instance guard, and disposing the
  Application underneath renders in flight. That is fixed and fenced
  (`Rendering/StartupSuppressionFenceTests`). Only the *Roblox-client-running*
  condition remains, and **that guard half is now unverified** — nobody has
  reproduced it since. **Start by re-measuring whether F-107 still reproduces at
  all.** It may be closeable by measurement rather than by code.
- **F-116** — down to one family: the **`FpsCapSettlerTests` post-write quiet-wait
  pump**. Two of three flake families are fixed by injecting time (`IClock` + an
  injected delay), which is the pattern to copy. Note the risk direction: **F-105
  returned nine gates to CI and arm64 went 1m50s → 3m47s**, so load-sensitive
  assertions are under *more* pressure now, not less. **Do not raise the render-test
  timeouts** — standing constraint.

### The three real features — each wants its own wave and a user decision

All three are tagged `[new-mechanism]`: the platform has no consumer for them today,
so they price like features, not like re-styles. **Do not slip them into a batch.**

- **F-013** (sev 4, vis 3 — *the highest-value row left*) — six secondary windows fold
  into one non-modal shell with a persistent left nav. **`DiscordConfig` needs a
  single owner first**; that is the real first task, and it is a prerequisite, not a
  detail.
- **F-112** (sev 2, vis 2) — an app-level `InputBindings` keyboard vocabulary with a
  discoverable shortcut list. Pairs naturally with the accessibility floor
  (F-044/F-045/F-052/F-072/F-073), all of which are now clean — so the floor it was
  waiting on exists.
- **F-106** (sev 2, vis 1 — *lowest value, genuinely optional*) — an `IWindowHost`
  seam so `MainViewModel`'s twelve window-opening methods can be asserted without a
  WPF host. Pure testability. Worth saying plainly to the user that this one buys
  less than the other two.

## Recommended opening move

Two measurement tasks before any feature work, because both may close a row for free:

1. Re-measure **F-107**'s reproduction condition post-F-105.
2. Re-read **F-096**'s second sentence against `docs/plugins/AUTHOR_GUIDE.md` as it
   stands today.

Then ask the user which of **F-013 / F-112 / F-106** to spend a wave on. F-013 is the
recommendation — highest severity and visibility of everything left, and the only
open row a user would actually *see*.

## Standing constraints (do not rediscover these)

- Never paste API keys or credentials into the transcript. Pre-commit hooks block
  Roblox cookies, PFX bytes, and hardcoded absolute user-profile paths in committable code.
- **Do not raise the render-test timeouts.**
- No macros, input automation, or client injection — that wall is deliberate (MaCro's
  job, different product, different platform).
- The two shipped emoji **stay** (die on Reroll identity, clipboard on Copy AI
  prompt) — user ruling, thematic not decorative.
- **A PR that closes a register row flips that row in the same PR.** Not later.

## One landmine in the working tree

`docs/superpowers/HANDOFF-2026-07-03-store-v1.9.md` is untracked and contains a
an absolute user-profile path. **The pre-commit local-path guard will reject any commit
that stages it.** Stage paths explicitly; never `git add -A` in this repo.
