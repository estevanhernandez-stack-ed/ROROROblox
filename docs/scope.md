# RORORO — Scope: Settings becomes a place

Remediation cycle on an established app. Thirteen findings-register rows, one surface, one story.

Cycle history: v1.16 built the Settings shell — five-page nav rail, resizable window, card vocabulary.
v1.17 shipped flatline and the rendered contrast gate. Most recent: v1.17.0.0, published as a
**pre-release** on 2026-08-10. Store submission is deliberately deferred until the backbone is
remediated; this cycle is the first pass of that remediation.

## Idea

The Settings shell shipped and nobody finished furnishing it.

v1.16 built the room — a nav rail, five pages, real containers, a resizable window. Then the cycle
ended. What is left is thirteen rows that all say a version of the same thing: **the structure
exists and the things that belong inside it were never moved in.** A page called "Alerts & memory"
with no memory controls. A theme picker that fails to persist without saying so. A card primitive
carrying two incompatible meanings at identical weight. Six settings written in three voices.

This cycle furnishes the room.

## Who it's for

The Pet Sim 99 clan member, same as always — a non-technical Windows user running eight alts. But
this cycle's user is specifically **the one who opens Settings and tries to change something.**
Today that person hits a page named for a feature that is not on it, edits `settings.json` in
Notepad to change a memory threshold, and cannot tell whether the theme they picked actually saved.

Secondary user, honestly: everyone who has to reason about this app later. Six of these rows are
consistency and copy defects, and consistency defects compound — every future surface either
inherits the vocabulary or adds a fourteenth exception.

## The verification that shapes this cycle

Every row here was **re-verified against the tree on 2026-08-10** before scoping. That pass is the
reason this cycle exists in this shape, and its findings are load-bearing:

- **Several rows are far cheaper than their ratings suggest.** F-026's fix direction asks for "a
  level above the card" — the nav rail already supplied it, so what remains is the card-versus-row
  distinction *inside* a page. F-023's destination exists and is already named "Alerts & memory."
  F-024's data is already materialised in the view-model. F-033 is one bool on a record the code
  documents as needing no migration.
- **F-019 is roughly half-delivered.** `ResizeMode="NoResize"` is gone, the rail is the
  group-to-group keyboard mechanism the row asked for, and the worst-case linear focus run dropped
  19 → 12. What remains is the accessible-naming layer and adopting a `SectionHeadingStyle` that
  already ships with exactly one consumer app-wide.
- **The register drifts faster than it is read.** Eleven of 57 open rows would have mis-scoped a
  cycle read off the table. That is why the recon is attached to this doc rather than re-run at
  `/spec`.

Full per-row detail: [`2026-08-10-register-reverification/`](superpowers/research/2026-08-10-register-reverification/).

## In scope — the thirteen rows, grouped by what they actually are

**Hierarchy — the card means two things (2 rows).**
F-026 (`4/5`) and F-019 (`3/3`). One grouping primitive carries both "section" and "setting" at
identical weight, which is why "split it up" cannot be answered with more cards. The rail solved the
between-page level; this solves the within-page one.

**Missing tenants — pages that do not hold what they are named for (4 rows).**
F-023 (`3/1`) the memory watchdog has four persisted settings and no UI, on a page called "Alerts &
memory." F-020 (`2/2`) the only way to change squad-launch behaviour is to begin a squad launch.
F-024 (`2/1`) per-account muting is right-click-only and the Alerts page never mentions it. F-078
(`2/2`) the theme-consent prompt is one-shot with no route back short of editing JSON.

**Silent failure (2 rows).**
F-051 (`4/2`) a failed settings write applies the theme live, says nothing, and reverts on restart.
F-053 (`3/2`) an unreadable theme file vanishes with no signal, and the copy sends users through a
restart the code does not require.

**Voice and weight (4 rows).**
F-043 (`3/4`) six settings, three voices, first person among them. F-062 (`2/3`) a registry path
presented as an effect, to a non-technical audience. F-037 (`3/5`) accent fill marks four different
things. F-046 (`3/3`) fill weight tracks neither consequence nor frequency.

**Persistence (1 row).**
F-033 (`4/4`) compact mode, pitched by the Welcome tour as a second-monitor workflow, is forgotten
on every restart.

## What "done" looks like

A clan member opens Settings and every page holds what its name promises. Changing a memory
threshold does not require Notepad. Picking a theme that fails to save says so. A section reads as a
section and a setting reads as a setting without either being a card. The page speaks in one voice,
in second person, and the loudest control on screen is the one the window exists to perform.

Register goes from **51 open to 38**, and `dotnet test ROROROblox.slnx` is green including the
contrast gate and the three rendered gates v1.17 added.

## What's explicitly cut

- **F-050 does not close, and must not.** `NoExemptionOutlivesItsFinding` deletes the contrast
  gate's exemption the moment that row stops reading `open`, which reddens **three** built-in themes
  — brand 3.79:1, midnight 4.16:1, magenta-heat 3.29:1, all under a 4.5 threshold. Only flatline
  survives. It stays open until something implements its fix direction, which is not this cycle.
- **F-091 — plugin theming — is its own cycle.** Plugins read the host's active theme *id* and look
  it up in a hardcoded table, so every user-authored theme is already broken in every plugin. The
  fix is a theme message in `plugin_contract.proto`, a `PluginContract` version bump, a host-side
  push, and a matching `ur-task` release. A contract change to a NuGet external authors consume does
  not get improvised inside a remediation sweep.
- **F-068 — the button vocabulary — is its own cycle.** Re-counted at **61 un-migrated call sites
  across 24 files, direction FLAT**: 96 pre-wave-5, 61 at the commit that wrote the file's own
  "63 across 15 files" comment, 61 at HEAD. Wave 5 migrated 35 in one pass and nothing has moved in
  five days. Its template-trigger half is 0% shipped.
- **The 38 remaining open rows.** This cycle is one cluster, not a register sweep.

## The two rows that are not really Preferences, named rather than smuggled

Honesty about the cluster's edges, because "one surface" is the cycle's whole justification:

- **F-037** is a nine-window Close-button sweep. Bounded — nine buttons, and the re-verification
  measured today's split at 5 accent-filled versus 4 secondary, so half the app already does what
  the fix direction asks. It is a decide-and-sweep, not a fix-one-window.
- **F-046** spans MainWindow, Library, Plugins and Preferences, and its fix direction names *shared
  button styles* — which is F-068's territory. **Scope it deliberately at `/spec`:** define the
  destructive variant and assign by consequence on this cycle's surfaces; do **not** start the
  61-site migration. If that line cannot hold, F-046 drops to F-068's cycle rather than dragging it
  forward.

## Loose implementation notes

Non-binding, refined at `/spec`.

- `Controls/ControlStyles.xaml` ships `SectionHeadingStyle` with exactly one consumer app-wide
  (`DiagnosticsWindow`). Preferences' three card headings are hand-rolled 13px SemiBold. F-026 and
  F-019 may both be partly answered by adopting what already exists rather than inventing a level.
- F-051's fix direction names a pattern the page already has — `AlertsStatusLine`. Mirror it rather
  than design a second status affordance.
- The `AutomationProperties` layer is effectively absent app-wide: 0 of 137 Button/ComboBox/
  ToggleButton/TextBox declarations carry a `Name`. F-019's remainder overlaps F-052, which is
  **not** in this cycle — decide at `/spec` whether Preferences gets named here or waits for the
  cross-surface pass, and say which.
- Four rows are pure copy (F-043, F-062, plus the copy halves of F-053 and F-023). Clan-facing
  register per `CLAUDE.md`: second person, terminal periods, no jargon, no first person.

## Assumptions surfaced

Per the fully-autonomous contract, filled from the record rather than asked. Each is a real fork
`/spec` should either confirm or overturn.

- **F-020's careful-mode toggle gets a home on an existing page rather than a sixth nav item**
  *(default — confirm)*. The rail has no launch page and the row's fix direction assumes one. Adding
  a sixth page for a single checkbox trades one inconsistency for another; Startup is the closest
  existing fit.
- **F-023 ships all four watchdog settings, not a subset** *(default — confirm)*. They are persisted
  together and the page is already named for them; shipping two of four leaves the same complaint.
- **F-026's answer is typographic, not a new container** *(default — confirm)*. The rail supplied
  the structural level; adding a second container primitive inside a page risks the exact
  two-meanings defect the row is about.
- **F-037 sweeps toward secondary-for-dismissal** *(default — confirm)*, following the fix
  direction and Diagnostics' existing precedent, rather than promoting the other four windows to
  accent.

## Distribution audience

Unchanged: Pet Sim 99 clan first, Microsoft Store second — with the Store deliberately parked. Worth
noting for the eventual listing that "Settings you can actually use" is a weaker headline than an
accessibility theme, so this cycle is backbone work rather than listing material, and that is the
point of doing it now while submission is deferred.
