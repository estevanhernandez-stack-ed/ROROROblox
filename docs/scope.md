# RORORO — Scope: the host tells plugins what colour it is

One findings row, two repos, one contract addition. F-091 (`3/4`).

Cycle history: v1.17 shipped flatline and the rendered contrast gate. v1.18 furnished the Settings
shell, closing 12 of 13 rows. Most recent: **v1.18.0.0**, merged as PR #107; **v1.17.0.0** is the last
published artifact and it went out as a **pre-release**. Store submission remains deliberately parked
while the backbone is remediated.

## Idea

**RoRoRo has four built-in themes. Its plugins can only see three of them, and the reason is that a
theme a plugin can see has to be a file.**

Plugins are separate processes with their own WPF windows. The contract between them carries no
colour except a `color_hex` badge override at
[`plugin_contract.proto:244`](../src/ROROROblox.PluginContract/Protos/plugin_contract.proto#L244). So
`626labs.ur-task` — whose manifest advertises *"theming that follows the host"* — reaches around the
contract and reads the host's storage off disk: `activeThemeId` out of `settings.json`, then the
palette out of `themes\<id>.json`.

That works for **user** themes, which are files. It cannot work for **built-in** themes, which are
records in host code and never touch the disk, so the plugin carries a hand-copied mirror of them:
`BuiltIns = { Brand, Midnight, MagentaHeat }`
(sibling repo `rororo-ur-task`, `src/Theming/HostThemeReader.cs:61`). Flatline shipped
after ur-task 0.5.0. It is not in the mirror, it is not on disk, and it lands on Brand.

This cycle deletes the reach-around. The host resolves its own active theme and **pushes the hexes**
over the gRPC channel that already exists, so a plugin never needs to know a theme id, a filename, or
a naming policy again.

## Who it's for

**The clan member who picked flatline and still has a plugin window painted brand navy.** Flatline is
the accessibility theme — the one that carries no meaning in colour, for someone on a bad panel, in
direct sun, or with a colour vision deficiency. Half-applying it is worse than not shipping it, because
the person who needed it now has one window that works and one that does not.

**Secondary, and the reason this is a contract change rather than a patch: every future plugin
author.** The `AUTHOR_GUIDE` does not document any of this, so an author who wants to match the host
today has to reverse-engineer it from ur-task, and inherit all five couplings while they are at it.

## The verification that shapes this cycle

F-091's row has now been **corrected twice, both times by me**, so every claim below was re-read
against both trees on 2026-08-10 rather than carried forward:

- **User themes work. This is the second time that had to be established.** `ResolveActive`
  (sibling repo `rororo-ur-task`, `src/Theming/HostThemeReader.cs:132-169`)
  checks the mirror first, then falls through to `themes\<id>.json` at `:158-161`. An id absent from
  the mirror is exactly the case that succeeds. The row claimed the opposite in its original text, and
  then **again** in a later sentence of the same cell *after* the correction was written above it.
- **Live repaint is already solved, and is not this cycle's problem.** `HostThemeService`
  (sibling repo `rororo-ur-task`, `src/Theming/HostThemeService.cs:38-76`) runs a
  `FileSystemWatcher` over the host folder with a 300ms debounce and re-applies on change. The plugin
  repaints live today. What it cannot do is **obtain** a built-in palette.
- **The host has no theme-changed signal at all.** `IPluginEventBus` carries `AccountLaunched`,
  `AccountExited`, `MutexStateChanged` and `MemoryPressure`. A theme change runs
  `PreferencesWindow.OnThemeChanged` → `ThemeService.ApplyTo` and nothing else hears it. **The bus
  event is net-new work, not plumbing that already exists.**
- **Two different version numbers, and only one moves.** The wire `contract_version` is the string
  `"1.0"`, supplied as a bare literal at
  [`App.xaml.cs:876`](../src/ROROROblox.App/App.xaml.cs#L876) and compared by **exact string match** at
  `PluginHostService.cs:70`. The NuGet package `ROROROblox.PluginContract` is independently at
  **0.7.0**. This cycle bumps the **package** and leaves the **wire string** alone, which is precisely
  why no existing plugin is rejected. The register row blurs these into "a version bump"; they are not
  the same number.
- **The addition is wire-additive on the host's service.** `RoRoRoHost` already ships four
  `Subscribe*(SubscriptionRequest) returns (stream …)` RPCs. A fifth is invisible to plugins that never
  call it. *(Note for `/spec`: the `Plugin` service — `OnUIInteraction` / `OnConsentChanged` /
  `OnShutdown` — is a reverse channel and looks like a natural home for a push, but adding there means
  the **host calls a method old plugins do not implement.** The host service is the additive side.)*
- **ur-task consumes 7 host slots plus one derived**, not the host's full ten
  (sibling repo `rororo-ur-task`, `src/Theming/HostThemeService.cs:110-122`).
  `RowExpired*` and `Navy` are dropped deliberately; `RowHoverBrush` is derived by tinting `RowBg` 4%
  toward `White`.

## In scope

**Host side — ships first, useful alone.**

1. **A theme message in `plugin_contract.proto`** carrying resolved hex slots, and a
   `SubscribeThemeChanged` streaming RPC on `RoRoRoHost` alongside the four that already exist.
2. **A theme-changed event on `IPluginEventBus`**, raised where the theme is actually applied, plus
   the `PluginHostService` handler that forwards it. This is the piece with no existing analogue.
3. **`ROROROblox.PluginContract` 0.7.0 → 0.8.0** on NuGet. Wire `contract_version` stays `"1.0"`.
4. **`AUTHOR_GUIDE.md` documents the feed**, because an undocumented capability is one more thing the
   next author reverse-engineers from ur-task.

**Plugin side — follows when it wants, in its own repo.**

- **ur-task drops the mirror** and takes the palette from the feed.

## What "done" looks like

A clan member switches RoRoRo to flatline with ur-task open, and the plugin window goes flat grey
within a beat. They switch to a theme they wrote themselves and it still works. They close RoRoRo and
the plugin keeps running, still usable.

`dotnet test ROROROblox.slnx` green including the harness, which is the project that matters here:
`ROROROblox.PluginTestHarness` runs real Kestrel and a real named pipe against a real
`RoRoRoHostClient`, so the new stream can be proven end-to-end **in the suite** rather than owed to a
human. That is unusual for this codebase and worth spending.

Register: F-091 closes. **38 open.**

## What's explicitly cut

- **Adding `flatline` to ur-task's `BuiltIns` table.** One line, fixes what Este saw, and it is the
  wrong fix: a fifth hand-synced copy of the palette, and the sixth built-in would break the same way.
- **Writing the host's built-ins to disk as theme files.** The honest cheap alternative, and it
  deserves naming rather than silence — `ThemeStore` already has the writer and the folder, so
  `themes\flatline.json` would make ur-task work **today with no contract change and no plugin
  release.** Rejected because it fixes availability while keeping all five storage couplings, and
  because it forces a new question the code currently answers by accident: `ThemeStore.ListAsync`
  drops a user theme whose id collides with a built-in, so materialising built-ins turns "the built-in
  wins" from an in-memory rule into a file-on-disk race. Cheaper today, more expensive every cycle
  after.
- **Theming anything beyond the palette slots.** No layout, no fonts, no per-plugin overrides, no
  theme *authoring* from a plugin. The host owns the theme; plugins receive it.
- **A capability gate on the theme feed.** `PluginCapability` exists to fence things that can hurt
  someone. A colour is not one; gating it would mean a plugin can be denied the ability to look
  correct.
- **F-050, F-068, F-046** — the standing exclusions, unchanged. F-046 in particular stayed open at the
  end of v1.18 and belongs to F-068's cycle.

## Assumptions surfaced

Per the fully-autonomous contract, filled from the record. Each is a real fork `/spec` should confirm
or overturn.

- **The message carries resolved hexes and no theme id** *(default — confirm)*. Sending an id is what
  the current design does, and it is the entire defect. If a plugin never learns the id, the failure
  mode cannot recur.
- **The message carries all ten host slots, not the seven ur-task uses** *(default — confirm)*.
  Truncating to today's consumer means a second package bump the first time a plugin wants
  `RowExpiredAccent`. Wire bytes are free here; a bump is not.
- **The plugin keeps a working no-host fallback** *(default — confirm)*. ur-task's own comment says it
  is *"fully usable standalone"*. Whether the fallback stays the disk reader or collapses to the Brand
  constant is a `/spec` call — the reader is dead weight once the feed lands, but deleting it means a
  plugin launched while RoRoRo is closed forgets the user's theme.
- **Host ships and releases independently of ur-task** *(default — confirm)*. Nothing forces a
  coordinated release, and pretending otherwise is how a two-repo cycle stalls.

**One fork deliberately left open for `/spec` rather than defaulted:** how a plugin discovers whether
the host supports the feed. Calling `SubscribeThemeChanged` against an older host returns
`UNIMPLEMENTED`, which a plugin can catch and fall back from; the alternative is a capability list on
`HostInfo`, which is field-additive and safe but is a standing commitment about how this contract
advertises itself for every future addition. That second option is small now and load-bearing later,
which is exactly the kind of call that should not be made by default.

## Distribution audience

Unchanged: Pet Sim 99 clan first, Microsoft Store second, Store parked. Worth noting that this cycle
has no listing value at all — nobody installs an app because its plugin windows match — and that is
fine. It is the cycle that stops the accessibility theme from being half-applied, which is a claim the
v1.17 release already implicitly made.
