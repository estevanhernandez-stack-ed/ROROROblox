# F-013 — six modal islands become one shell (wave design)

**Date:** 2026-08-21 · **Status:** design for the F-013 wave · **Register row:** F-013 (QF-1, sev 4 · vis 3)

Fix direction, verbatim from the row: *"Fold six into one non-modal shell with a persistent left
nav; DiscordConfig gets a single owner first `[new-mechanism]`."*

This wave ships as **two PRs in sequence**. PR A is the prerequisite and flips no row. PR B is the
fold and flips F-013 in the same PR. The split is the handoff's own framing: the DiscordConfig
owner "is a prerequisite task, not a detail" — it changes the app's concurrency story and deserves
its own review surface before any window goes modeless on top of it.

## §0 Evidence base

Recon ran 2026-08-21 on four parallel readers (window census, DiscordConfig ownership graph,
conventions + precedent, gate landscape). Line references below were verified at branch
`glow/f107-close`; expect drift.

**The six** (all `ShowDialog()`, all destination surfaces, none holds cross-open state):
Games, Settings (`PreferencesWindow`), History, Diagnostics, Plugins, About. Corroboration:
F-012 counted the same six toolbar buttons; four of the six (Settings, History, Diagnostics,
Plugins) have duplicate tray doors in `App.xaml.cs` that a shell unifies; the `PluginsWindow`
header names Preferences/Diagnostics/History as one shared "auxiliary surface" pattern.

**Excluded and staying modal:** the result-bearing pickers (SquadLaunch, JoinByLink,
FriendFollow — modality is their return channel), CookieCapture (already non-modal, owned by the
capture service), Welcome tour, ConsentSheet, the Preferences sub-dialogs (ThemeBuilder,
Export/Import accounts), CaptionColorPicker, and the nine `Modals/` interruptions.

## §1 PR A — DiscordConfig gets a single owner

### The defect being pre-empted

`DiscordConfig` is a compound record (presence + join + two webhook credentials + two alert
routings + mute list) saved whole. Two production writers exist: `PreferencesWindow`'s
`_discordConfig` load-once/mutate/save-whole snapshot, and `MainViewModel.SetAlertsMutedAsync`'s
fresh load-modify-save. The field doc at `PreferencesWindow.xaml.cs:66-79` says it plainly: *"it
is the modality, not the code, that makes this safe today."* Three cached copies exist
(`_discordConfig`, `DiscordConfigCache.Current`, `DiscordPresenceService._config`), reconciled at
modal-close moments a persistent shell does not have.

### Three pre-existing defects the owner also closes (found by recon, not hypothetical)

1. **Mid-session mute never reaches the dispatcher.** `SetAlertsMutedAsync` writes the store but
   never `DiscordConfigCache`, and `AlertDispatcher` reads the cache per dispatch — a
   context-menu mute is invisible to alert routing until a tray-Preferences close or restart.
2. **Failed-save cache incoherence.** The Preferences toggle handlers write the cache before the
   awaited save and reload only the snapshot on failure — disk, snapshot, and cache disagree
   until the next full refresh.
3. **Double-Preferences.** The tray's Settings item is reachable while a modal Preferences is up
   (`ShowDialog` disables only pre-existing top-levels; the tray menu is created later), and
   `OpenPreferencesFromTray` has no already-open guard — two independent snapshots, the same
   lost update modality was supposed to prevent. (PR B's single-instance shell removes the
   second door entirely; PR A removes the snapshots that made it dangerous.)

### The design

One owner, grown from the existing pair (`DiscordConfigStore` + `DiscordConfigCache`), combining
the two in-tree precedents: `AppSettings`' serialized read-modify-write gate and
`StreamerIdentityProvider`'s owner-plus-`Changed`-event, views-not-snapshots contract.

`ROROROblox.Core.Discord.DiscordConfigService` (new, Core — no UI dependencies):

- `DiscordConfig Current` — volatile slot, torn-free read from any thread (absorbs the cache's
  one job).
- `Task InitializeAsync()` — load once at startup; defaults on tamper, same as the store.
- `Task MutateAsync(Func<DiscordConfig, DiscordConfig> mutate)` — the only write path.
  Serialized by a `SemaphoreSlim(1,1)`: read `Current`, apply, persist via the store, **then**
  publish to `Current` and raise `Changed`. A failed persist throws and publishes nothing —
  disk and memory cannot disagree, and callers keep their existing catch-and-tell-the-user
  shapes. A mutation on a never-initialized owner lazily loads the disk first, so a
  startup-ordering mistake composes against real state instead of wiping it. *(Corrected at
  build time from the earlier `Task<bool>` sketch: swallowing the exception would have cost the
  MessageBox its error message.)*
- `event EventHandler<DiscordConfig>? Changed` — raised after publish, on the mutating thread;
  subscribers marshal themselves (the app's existing convention).

Consumers become views:

- `PreferencesWindow` drops `_discordConfig` entirely. Handlers call
  `MutateAsync(c => c with { … })`; paints read `Current`; the window subscribes `Changed` so an
  out-of-window write (row mute) repaints the alerts section live. The modality comment is
  replaced by a pointer to the owner.
- `MainViewModel.SetAlertsMutedAsync` goes through `MutateAsync`. The interleave the old comment
  feared becomes a serialized pair of read-modify-writes — both land, by construction.
- `DiscordPresenceService` subscribes `Changed` → `ApplyAsync`, killing the divergence class
  (a writer who forgets to call `ApplyAsync`).
- `AlertDispatcher`'s `Func<DiscordConfig>` reads `service.Current` — unchanged shape.
- `DiscordConfigCache` is deleted; `App.RefreshDiscordConfigAsync` and both "on Preferences
  close" re-push sites for Discord state go with it.

### Tests (written first)

- Two concurrent `MutateAsync` calls with disjoint edits: **both** land (the lost update, pinned
  dead).
- A mute through the owner is visible to a dispatcher-style `Current` read immediately.
- Failed persist: `Current` unchanged, no `Changed` event.
- `Changed` fires once per successful mutate, carries the published record.
- `AccountMuteTests.MutingOneRow_LeavesEveryOtherSettingAlone` stays green through the rewire.
- Existing Discord suites (store, dispatcher, router, presence, status line) stay green.

## §2 PR B — the shell

### Shape

`ShellWindow` — non-modal (`Show()`), single-instance: every door (toolbar buttons, Tools menu
items, tray items, VM commands) resolves to one route that surfaces the existing shell and
navigates it to the requested destination. Resizable destination with `MinWidth`/`MinHeight`
(`WindowChromeFenceTests` → `Destinations`). The main window stays what it is; the shell is the
auxiliary-surface host.

### Nav

The Preferences rail pattern, promoted: left `ListBox` rail (164px), selection carried by the
reserved 3px cyan bar + SemiBold weight — **shape, not fill** (the F-002 lesson; a highlight fill
rebuilds the flatline failure). Six items: Games, Settings, History, Diagnostics, Plugins, About.
Deliberate coverage decision, stated here so it is chosen and not stumbled into: a `ListBox` nav
sits outside the button-rank vocabulary, so its item containers get contrast/muted-text/edge/
naming fences but no button-state gating. That is the same trade `SettingsNav` already made.

### Title rule (C2 tension, resolved)

One Alt-Tab window, six destinations: the shell's `Title` is set at runtime to the active
destination's noun, and the `PageHeader` heading matches it — the header-matches-title-bar rule
holds at every moment. No product name (`WindowTitleConventionTests` exempts only
MainWindow/About/Welcome; About folding into the shell means its page header says About while the
title bar tracks it — the About *window* exemption retires with the window).

### Pages

Each window's content extracts to a `UserControl` page; the six `Window` classes are deleted.
Sub-dialogs (ThemeBuilder, Export/Import, EdgeRemediation asks, rename, consent) stay modal and
take the shell as `Owner`. Settings keeps its inner rail (its five pages become the Settings
page's own sub-nav, unchanged), or flattens into the shell rail as siblings — **decided at build
time by what the rail can carry legibly; default is keep Settings' inner rail** so the shell rail
stays six items and the capture routes for 03a-03d survive with one added hop.

### Close-moment inventory (each becomes event-driven or read-per-use)

- `OpenGames`/`OpenHistory` → `ReloadGamesAsync` after close → favorites/private-server stores
  raise change events the VM subscribes to (or the shell raises a navigated-away event; store
  events preferred — they also fix staleness the modal never covered).
- Tray-Preferences close → idle-settings re-push (`InitializeIdleSettingsAsync`) → the idle
  settings follow the same owner-or-event path; recon flagged the main-window Settings door
  already misses this re-push today (pre-existing; confirm during build and record).
- Tray-Preferences close → `RefreshDiscordConfigAsync` → deleted by PR A.

### Gate enrollment (from the survey; registration-required items first)

1. `WindowChromeFenceTests.Destinations` += ShellWindow (MinWidth/MinHeight mandatory).
2. `WindowContentFitsTests` matrix += ShellWindow (needs a cheap-construction path).
3. `docs/ui-routes.json`: re-author every folded surface's open/close steps (05, 06, 07, 09, 20;
   03/03a-d/10/23 for Settings; 04 Games) **in the same commit** as the UI change; update the
   pinned surface count in `UiRoutesSchemaTests`; update `docs/ui-capture-checklist.md`. This is
   the surface-08 failure shape × 6 and the suite stays green through all of it — the routes are
   correct only if re-authored deliberately.
4. `CommandBindingIntegrityTests` covers MainWindow→MainViewModel only; command bindings moving
   into the shell exit its coverage — widen the gate to the shell or record the gap as a row.
5. Auto-enrolled and designed-for, not registered: title convention, button ranks (zero inline
   paint), accessible names on every interactive control (ceiling is exactly 1 and may not
   rise), dismiss-after-text ordering, contrast pairs × 4 themes, muted-text role fence,
   interactive-edge fence, type ladder, brand-name fence, style constructibility.
6. No new theme slot. No new button rank unless the rail turns out to need one, in which case it
   is a register row first (spec §3 rule: the vocabulary does not grow mid-sweep).

### What flips

PR B flips **F-013** in the same PR. F-012 (six identically-weighted toolbar buttons) and F-106
(twelve unassertable window-opening methods) are affected but not claimed: re-measure both after
the fold and record new numbers and direction in their rows — the fold likely shrinks F-106's
twelve and reshapes F-012's premise, and both deserve their own measurement, not a drive-by close.

## §3 Sequencing and risk

1. PR A lands first and alone. It is behavior-preserving for every surface except the three
   pre-existing defects it fixes, all in the user's favor.
2. PR B follows on top. Fold order within the PR: Diagnostics → About → History → Plugins →
   Games → Settings (ascending wiring complexity; Settings last because it carries the
   sub-dialogs and the reachability fence).
3. Standing constraints hold: no render-timeout raises, the two emoji stay, stage paths
   explicitly (never `git add -A` — the untracked 2026-07-03 handoff trips the path guard),
   same-PR row flips, and every new gate is shown failing before it counts.
