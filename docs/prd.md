# RORORO — Product Requirements: Settings becomes a place

Expands [`docs/scope.md`](scope.md). Technical design lands in `/spec`; nothing here picks a control,
a style key or a mechanism.

**Anchor:** every page holds what its name promises.

Every claim about current behaviour was re-verified against the tree on 2026-08-10 and cites the file
it came from. Per-row evidence:
[`2026-08-10-register-reverification/`](superpowers/research/2026-08-10-register-reverification/).

## Problem statement

v1.16 built the Settings shell and stopped. A clan member who opens Settings today finds a page
called **"Alerts & memory"** with four persisted memory settings and no memory controls on it
([`AppSettings.cs:322-392`](../src/ROROROblox.App/AppSettings.cs#L322-L392) persists them;
zero `.xaml` references exist). To change how much memory triggers a warning they edit
`settings.json` in Notepad. If their theme fails to save they watch it apply, see nothing, and find
the old theme back after restart. Nine cards on one page wear identical chrome whether they hold one
control or ten.

None of this is exotic. It is the ordinary cost of shipping a container and not filling it, and it
lands on a non-technical audience running eight alts who opened Settings to change one thing.

## User stories

Epic headings are stable addresses. `/spec` and `/checklist` reference them by name.

### Epic 1 — A page holds what its name promises

**Story 1.1 — Setting a memory threshold without a text editor.**
As a clan member watching an alt climb toward a memory cap, I want to change when the warning fires
and what counts as too much, from the page already named for it.

- [ ] All four persisted watchdog settings are editable in the app: `MemoryWatchdogEnabled`,
      `MemoryReserveMb`, `MemoryCapMb`, `ProjectionWarnMinutes`
      ([`AppSettings.cs:322-392`](../src/ROROROblox.App/AppSettings.cs#L322-L392)).
- [ ] They live on the **Alerts & memory** page ([`PreferencesWindow.xaml:80`](../src/ROROROblox.App/Preferences/PreferencesWindow.xaml#L80)),
      beside the alert routing that already ships there. Not a new page.
- [ ] Values persist across restart and round-trip through the same `IAppSettings` accessors the
      file already uses. No second source of truth.
- [ ] A value outside a sensible range is refused or clamped **in the UI**, with the reason visible.
      A megabyte field that accepts `-1` silently is the same defect one level down.
- [ ] Editing `settings.json` by hand still works and the UI reflects it on next open.

**Story 1.2 — Changing squad-launch behaviour without launching a squad.**
As a user who wants careful mode on, I want to set it in Settings, because today the only way to
change how squad launches behave is to begin one.

- [ ] The careful-mode toggle is reachable from Settings, binding the same persisted value
      `CarefulSquadLaunch` the modal writes ([`SquadLaunchWindow.xaml.cs:59,73,79`](../src/ROROROblox.App/SquadLaunchWindow.xaml.cs#L59)).
- [ ] Both surfaces stay in sync — changing it in one is visible in the other on next open.
- [ ] The in-modal toggle stays. This is a mirror, not a move; the modal is where the setting is
      most often wanted.

**Story 1.3 — Seeing which accounts I muted.**
As a user who muted an account by right-clicking it three weeks ago, I want the Alerts page to admit
that, because it owns every other alert decision.

- [ ] The Alerts section shows how many accounts are muted.
- [ ] An unmute-all affordance exists.
- [ ] Both read from the muted set the view-model already materialises at startup
      ([`App.xaml.cs:1376`](../src/ROROROblox.App/App.xaml.cs#L1376)). No new persistence.
- [ ] Zero muted accounts reads as a clean state, not an empty row or a stray "0".

**Story 1.4 — Changing my mind about the theme prompt.**
As someone who dismissed a prompt in the first ten seconds of a launch to get on with playing, I want
a route back that is not editing JSON.

- [ ] A re-ask affordance exists on the Appearance page and calls the existing setter.
- [ ] It is discoverable without knowing the prompt ever happened.

### Epic 2 — A section is not a setting

**Story 2.1 — Telling a group apart from a single control.**
As a user scanning Settings, I want a section to look like a section and a setting to look like a
setting, so "split it up" is answerable.

- [ ] A card holding one control and a card holding ten no longer carry identical weight. Measured
      today: 9 cards at contents 1/1/2/1/2/2/**10**/2/3
      ([`PreferencesWindow.xaml`](../src/ROROROblox.App/Preferences/PreferencesWindow.xaml)).
- [ ] The distinction is structural or typographic — **not a second container primitive**, which
      would reproduce the two-meanings defect this story is about.
- [ ] It reads correctly in all four built-in themes, flatline included. A hierarchy carried only in
      colour fails the theme the last cycle shipped.

**Story 2.2 — Moving between groups with a keyboard.**
As a keyboard or screen-reader user, I want to move group-to-group rather than through every control
in order.

- [ ] The five-page rail already provides between-page movement
      ([`:72-83`](../src/ROROROblox.App/Preferences/PreferencesWindow.xaml#L72-L83)); confirm it, do
      not rebuild it.
- [ ] The three hand-rolled 13px SemiBold headings adopt the shared `SectionHeadingStyle` that
      already ships ([`ControlStyles.xaml:143`](../src/ROROROblox.App/Controls/ControlStyles.xaml#L143))
      and currently has exactly one consumer app-wide.
- [ ] Worst-case linear focus run does not grow. Measured today: 12, down from the audited 19.
- [ ] **Whether Preferences gets an accessible-naming layer here is `/spec`'s call and must be stated
      either way.** F-052 owns the cross-surface naming pass and is not in this cycle; 0 of 137
      declarations carry an `AutomationProperties.Name`. Doing Preferences alone is defensible; doing
      it silently is not.

### Epic 3 — Failure says so

**Story 3.1 — Knowing my theme did not save.**
As someone who picked a theme, I want to be told if it did not persist, rather than discovering it
after a restart.

- [ ] On persist failure the theme still applies live — the session is not degraded.
- [ ] A message appears in the Theme section, mirroring the `AlertsStatusLine` pattern the page
      already uses. Not a modal.
- [ ] The success path stays silent. A status line that speaks on every save is noise.

**Story 3.2 — Knowing my theme file was unreadable.**
As someone who dropped a JSON file in the themes folder, I want to know it failed rather than watch
it not appear.

- [ ] An unreadable or malformed theme file is reported in the app, naming the file.
- [ ] The tooltip stops promising a restart the code does not require — it says reopen this page.
- [ ] A folder with one bad file among good ones still loads the good ones.

### Epic 4 — One voice, one meaning for weight

**Story 4.1 — Settings that sound like one app.**
As a non-technical clan member, I want Settings to speak the way the rest of the app speaks.

- [ ] All six settings on the run-on-login and Discord cards are second person with terminal
      periods. No first person — today one setting speaks as "I".
- [ ] The run-on-login copy states the **effect**, not the registry path. A registry key is
      implementation, and it is the wrong first read for this audience.
- [ ] No line duplicates the checkbox label directly above it.
- [ ] Clan-facing register per `CLAUDE.md`: no jargon, no "seamlessly", no em-dash pile-ups.

**Story 4.2 — The loudest control does something.**
As a user scanning a window, I want the filled button to be the thing the window exists to do.

- [ ] Dismissal takes the secondary treatment everywhere. Measured today across the nine Close
      buttons: **5 accent-filled, 4 secondary** — so half the app already does this and the fix is a
      decide-and-sweep, not a fix-one-window.
- [ ] The window's actual primary action keeps the filled treatment.
- [ ] Diagnostics is the existing precedent and does not change.

**Story 4.3 — Destructive actions look destructive.**
As a user about to remove an account, I want that button to look different from Save.

- [ ] A destructive variant exists in the shared button styles. Today there are two ranks and no
      destructive one.
- [ ] It is assigned by **consequence**, not by window.
- [ ] **Bounded deliberately:** this story defines the variant and applies it where consequence
      demands on this cycle's surfaces. It does **not** start F-068's migration — 61 un-migrated call
      sites across 24 files, direction flat. If that line cannot hold, this story drops to F-068's
      cycle rather than dragging it forward. `/spec` states where the line is.

### Epic 5 — The app remembers

**Story 5.1 — Compact mode survives a restart.**
As a user who runs RoRoRo on a second monitor, I want compact mode to still be on tomorrow. The
Welcome tour pitches it as a second-monitor workflow and it is forgotten on every restart.

- [ ] `CompactMode` persists. `MainViewModel`'s property is a plain in-memory `SetField` today
      ([`:695-709`](../src/ROROROblox.App/ViewModels/MainViewModel.cs#L695-L709)) with nothing
      writing it to disk.
- [ ] It restores on load.
- [ ] No migration step — the settings record's defaulted fields load cleanly on an existing file,
      which the code already documents.

## What we're building

Ordered by dependency, not importance.

1. **Persistence and missing controls** (Epic 1, Epic 5) — the settings that exist but cannot be
   reached, and the one that can be reached but does not stick. Independent of everything else.
2. **Hierarchy** (Epic 2) — because the memory controls Epic 1 adds land on the page whose grouping
   is the defect. Doing Epic 1 first and Epic 2 second means adding to a broken structure and then
   fixing it; `/spec` decides whether that ordering inverts.
3. **Failure messaging** (Epic 3) — small, self-contained, and mirrors a pattern already on the page.
4. **Voice and weight** (Epic 4) — copy is independent; the button work is the only part that
   touches other windows.

## What we'd add with more time

- **The cross-surface accessible-naming pass** (F-052). 0 of 137 declarations named. Real, and
  bigger than one page.
- **The button vocabulary migration** (F-068). 61 sites, flat for five days, with the
  template-trigger half unshipped.
- **Plugin theming** (F-091). Every user-authored theme is broken in every plugin, not just flatline.
- **A settings-schema test** so a persisted field with no UI fails a build rather than waiting for an
  audit. F-023 existed because nothing connected "persisted" to "reachable."

## Non-goals

1. **F-050 does not close.** Its status cell auto-deletes the contrast gate's exemption and reddens
   brand (3.79:1), midnight (4.16:1) and magenta-heat (3.29:1) against a 4.5 threshold. Only flatline
   survives. Untouched by this cycle.
2. **No plugin-contract change.** F-091 needs a proto message, a NuGet version bump and an external
   plugin release.
3. **No 61-site button migration.** Epic 4.3 defines a variant; it does not sweep.
4. **No new nav page.** The rail has five and the cycle's whole argument is that pages should hold
   what they are named for, not that there should be more of them.
5. **Not a register sweep.** Thirteen rows in one cluster. The other 38 keep their own sequencing.

## Open questions

**Before `/spec`:**

- **Where careful mode lives.** The rail has no launch page and the row's fix direction assumes one.
  Startup is the closest existing fit; a sixth page for one checkbox trades one inconsistency for
  another.
- **Whether Epic 2 precedes Epic 1.** Adding four memory controls to the page whose grouping is the
  defect, then fixing the grouping, is rework. Inverting risks designing hierarchy against a page
  that is about to gain a card.
- **How far Epic 4.3's destructive variant reaches.** "Assign by consequence" is unbounded on its
  face. `/spec` names the specific actions.

**During `/spec`, resolved by looking:**

- **What the hierarchy answer actually is.** Typographic, spacing, or a heading level above the card.
  The rail already supplied the between-page level, so this is narrower than the row's framing.
- **Whether Preferences gets named for accessibility here or waits for F-052.**

**Can wait until `/build`:**

- Exact copy for the six settings and the two status messages. Register is clan-facing; the shapes
  are in the fix directions.
