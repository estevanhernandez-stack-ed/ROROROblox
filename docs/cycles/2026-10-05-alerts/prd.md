<!-- Cart cycle #5, step 3. Input: scope.md beside this file. Written to docs/cycles/2026-10-05-alerts/
     rather than docs/prd.md, which holds the retired v1.18/v1.21 snapshot CLAUDE.md treats as history. -->

# RoRoRo v1.33 — Product Requirements

## Problem Statement

RoRoRo interrupts people on terms they cannot set. One hardcoded five-minute constant governs every
alert that goes through the router, and the notification users actually see and hear — the Windows
balloon — goes through nothing at all: no destination check, no mute check, no cooldown. The controls
that do exist are scattered across three cards and two storage files, and one of them lies to a screen
reader. Este hit all four complaints in a single evening with six accounts running; the clan hits them
quietly and says nothing.

## User Stories

### Desktop is a destination like any other

The structural epic. Nothing else in this cycle is true until this is.

- As someone who routed memory warnings to Discord only, I want the desktop to stay quiet, so that my
  routing choices mean what they say.
  - [ ] Unticking **Desktop** for a kind produces no balloon and no sound for that kind, measured by
        forcing a crossing with the other destinations ticked.
  - [ ] Ticking **Desktop** produces exactly one balloon per event — not two, which is what the
        unconditional tray path plus a ticked Local destination would produce today.
  - [ ] A per-account mute silences the desktop for that account, as it already does for Discord.
  - [ ] The desktop obeys the cadence (next epic) on the same key as every other destination.
  - [ ] The tray **badge** is unaffected: it still colours on any crossing, with no cadence and no
        destination check. It is a colour change, not an interruption, and it is how the state stays
        visible when every destination is off.
  - [ ] Memory warnings, idle alerts, drops, recycles, uptime marks and metric breaches all take the
        same path. No kind keeps a private route to the screen.

- As someone whose machine is about to run out of memory, I want to still find out, so that turning
  down noise does not turn off the one warning that matters.
  - [ ] With every destination unticked for memory warnings, the tray badge still shows pressure and
        the row chips still read it.
  - [ ] The Alerts section says plainly, where destinations are chosen, that the badge is always on.

### A cadence you can see and set

- As someone being paged every few minutes, I want to say how often an alert may speak, so that I can
  keep the alert and lose the pestering.
  - [ ] A single **"At most once every"** control governs every alert kind: 1, 5, 15, 30, 60 minutes,
        and **Every time**.
  - [ ] Default is 5 minutes, so nobody's current behaviour changes on upgrade.
  - [ ] Setting 30 minutes means no kind speaks more than twice an hour, measured against the log.
  - [ ] **Every time** disables the throttle, and the UI says what that means in one line.

- As someone who wants drops instantly but memory warnings rarely, I want a per-kind override, so that
  one noisy kind does not set the pace for the useful ones.
  - [ ] Each alert row can override the global, or follow it. Following is the default and is shown as
        following, not as a copied number.
  - [ ] Changing the global moves every row that is following it, visibly.

- As someone who saw an alert vanish, I want the quiet period to behave predictably, so that I stop
  wondering whether the app is broken.
  - [ ] A repeat landing at exactly the cadence boundary fires. Today's strict `>` comparison drops
        it, which is why 23:33:03 produced nothing and 23:34:03 produced an alert on 2026-10-04.
  - [ ] The cadence applies per account and per kind, as the cooldown does now — one noisy account
        does not mute another.

- As someone whose alerts arrive four times for one event, I want fan-out counted honestly, so that
  "once every 30 minutes" is not once per destination.
  - [ ] The cadence is evaluated once per event, not once per destination.

### One alerts section

- As someone changing an alert setting, I want everything about alerts in one place, so that I stop
  hunting.
  - [ ] Idle, memory and metric alerts sit under one heading with one shape: **does it fire**, **how
        often at most**, **where does it go**.
  - [ ] The memory numbers (keep free, per-account cap, warn-ahead) sit with the memory row they drive,
        not 460 lines of markup away.
  - [ ] Idle alerts gain destinations and a cadence like every other kind, losing nothing they have.
  - [ ] Nothing in the section requires knowing which file a setting lives in. `settings.json` and
        `discord.dat` stay as they are; the split stops being visible.
  - [ ] Every control keeps an accessible name, and the fence counts move in the same commit.

### The sound is ours, and it is a choice

- As someone who hates the Windows notification sound, I want to pick what RoRoRo sounds like, so that
  an alert does not sound like an error.
  - [ ] A **sound** setting with three options: **Silent**, **RoRoRo chime** (default), **Windows
        default**.
  - [ ] The default is the bundled chime, not silence — an alert that makes no sound is a log entry.
  - [ ] The stock Windows notification sound plays only when explicitly chosen.
  - [ ] The notification is drawn by the app and themed, so it stops looking like a system error
        popup and follows the active theme.

### A toggle tells the truth to everything that asks

- As someone using a screen reader, I want a control I toggle to actually change, so that the app does
  not report a state it is not in.
  - [ ] Toggling any alerts control through UI Automation, voice access or a screen reader saves, and
        the behaviour changes to match.
  - [ ] A fence test fails if a new `CheckBox` or `ToggleButton` on the Settings page is wired to
        `Click`, which is what made `MemoryWatchdogEnabledToggle` report Off while it kept running
        (measured live, 2026-10-05).

### Rider: a plugin learns why it was started

- As a plugin author, I want to know whether RoRoRo autostarted me, so that my plugin can come up in
  the tray instead of opening a window.
  - [ ] `RORORO_LAUNCH_REASON` is set in the started plugin's environment: `autostart`, `manual`,
        `install` or `update`.
  - [ ] A plugin that does not read it is unaffected. No argv change.
  - [ ] All four launch paths set it, including the update relaunch at `PluginsViewModel.cs:293`.
  - [ ] `contractVersion` does not move; nothing in the gRPC surface changes.

### Rider: game titles look like their game

- As someone reading a row, I want the emoji in a game's name to look the way Roblox shows them, so
  that our rows do not read as a washed-out copy.
  - [ ] Emoji in game titles render in colour wherever a title is shown: account rows, the Games page,
        saved private servers.
  - [ ] Titles without emoji are unchanged, and layout does not shift.
  - [ ] Alert text carrying a game title does the same, or the PRD says plainly where it does not.

### Rider: the About page signs itself

- As someone who found the egg, I want it to look like somebody made it, so that the reward is worth
  the six or seven taps.
  - [ ] The **626Labs wordmark with LLC** sits at the bottom of the About page, always visible, 40px
        tall minimum.
  - [ ] The egg reveals the **"Koii 4 eva" wordmark** in Este's hand, not styled text.
  - [ ] Both are drawn marks, following the avatar pattern (`sources/*.svg` kept, PNG committed).
        Neither requires EsteFont Pro inside the binary.
  - [ ] The reveal keeps its glow and gains a gradient, applied as an `OpacityMask` over theme brushes
        so it follows the active theme.
  - [ ] The six localized `AboutPage_Koii4Eva` entries are retired in the same commit as the asset.
        All seven were byte-identical, so nothing is lost.
  - [ ] The egg still fires on six **or** seven clicks, randomised per shell. Already true and now
        fenced by `EasterEggCounterTests` (PR #228).

## What We're Building

Must-have, in build order. The first is load-bearing for the second and fourth.

1. **Route the desktop.** `IdleAlertPresenter` and `WireMemoryWarningTray` stop writing to the screen
   directly; every kind reaches the desktop through the dispatcher, subject to destinations, mute and
   cadence. The tray badge keeps its unconditional path.
2. **Cadence as a setting.** `AlertRouter.Cooldown` becomes a value the router is given: a global
   minimum plus a per-kind override, defaulting to today's five minutes. Boundary comparison fixed.
3. **One section.** The three cards become one, with the memory numbers beside the memory row and idle
   alerts in the grid.
4. **The themed notification and the sound choice**, including the bundled chime asset.
5. **Click-wired toggles fixed**, plus the fence that stops the next one.
6. **`RORORO_LAUNCH_REASON`** on all four launch paths.
7. **Colour emoji in titles.**
8. **The About marks**, the gradient, and retiring the localized entries.

## What We'd Add With More Time

- **Quiet hours.** Cadence's natural neighbour, and a second feature with its own edge cases.
- **A metric-rule editor.** `metric-rules.json` is plugin-authored data with an owner field; a
  half-editor is worse than the file.
- **Per-destination cadence.** "Phone rarely, desktop always" is coherent and nobody has asked for it.
- **Action Center toasts**, if a reason appears beyond sound.

## Non-Goals

- **No new alert kinds.** This cycle governs what exists.
- **No real Windows toasts.** The direct-download build would need an AUMID shortcut and a COM
  activator; that is the two-build-shapes trap that cost v1.23 its Join-by-URI and run-on-login. The
  custom balloon solves the sound complaint without it.
- **No change to the five-minute default.** The number becomes visible and settable; it does not move
  under anyone who liked it.
- **No phone or Discord redesign.** Those destinations work.
- **No Store-policy change.** Core still observes and never synthesizes input; nothing here touches
  the macro wall.

## Open Questions

- **The chime itself.** Needs an actual audio asset — short, quiet, not a ding. Synthesising one is
  feasible rather than licensing or recording it, which also keeps it ours and keeps the file tiny.
  **Needed before the sound task is called done, not before /spec.**
- **Does a per-kind cadence override belong on every row, or only where it earns one?** Six overrides
  is six more controls on a page we are trying to simplify. **Can wait for /spec.**
- **Colour emoji rendering strategy — ANSWERED 2026-10-06, and it is small.** Confirmed by search:
  WPF has no native colour-glyph rendering in any version, .NET 10 included; UWP's
  `IsColorFontEnabled` has no WPF counterpart. But two maintained libraries provide drop-in
  `TextBlock`/`RichTextBox` replacements that render colour emoji from the **system** Segoe UI Emoji
  font — [Emoji.Wpf](https://www.nuget.org/packages/Emoji.Wpf/) and
  [iNKORE.UI.WPF.Emojis](https://github.com/iNKORE-NET/UI.WPF.Emojis) — with no bundled images and no
  embedded font, which was the expensive part of every approach considered before. So this rider is a
  dependency evaluation plus targeted swaps at the handful of places a game title is drawn, not its
  own cycle. **What `/spec` must settle:** licence and supply-chain fit for a Store binary, whether
  the replacement honours `DynamicResource` theming and the accessible-name fences, and the fallback
  if it does not — which is to drop the rider, not to hand-roll a glyph renderer.
- **Does the themed balloon survive a fullscreen Roblox client?** A WPF popup may not paint over an
  exclusive-fullscreen game, which is where these users are. The current shell balloon may have the
  same limit; nobody has measured either. **Measure during build, before the sound task is called
  done.**
