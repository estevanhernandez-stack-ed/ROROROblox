# RORORO — v1.19.0 Plugin Theme Feed Build Checklist

**Cycle:** v1.19.0.0 — the host tells plugins what colour it is (current shipped: 1.18.0.0)
**Cycle type:** Contract addition across two repositories.
[`docs/spec.md`](spec.md) is the canonical technical artifact for this cycle.
**Archive it into `docs/superpowers/specs/` before the next Cart round overwrites it** — item 9 owns
that, and v1.17's was nearly lost exactly this way.
**Anchor:** a plugin should never need to know where the host keeps its themes.

## Build Preferences

- **Build mode:** Autonomous
- **Comprehension checks:** N/A (autonomous)
- **Git:** Commit after each item. Conventional commits. Branch `feat/plugin-theme-feed` — already cut,
  already carries reflection + scope + PRD + spec.
- **Verification:** Yes — **C1 after item 5**, **C2 after item 8**. C1 is the wire gate: the feed is
  provably working over a real named pipe and no existing plugin is broken. C2 is the only eyes-on gate
  in the cycle and the only proof that F-091 is actually fixed.
- **TDD:** strict on **item 1** (read-back semantics), **item 4** (the harness tests must go RED with
  `UNIMPLEMENTED` *before* item 5 implements the handlers — that ordering is the item's entire proof),
  and **item 7**. Items 2, 3, 5 are verify-by-build; item 8 is verify-by-running; item 9 is audit.

## Two repositories, and the second one is not optional to the row

Items 1-6 and 9 are **RoRoRo**. Items 7-8 are **`rororo-ur-task`**, a sibling repo at
`../rororo-ur-task`.

**The host leg is independently releasable and independently useful** — it makes every future plugin
correct by default. But **F-091 does not close until item 8 ships**, because the row's evidence is a
mis-coloured plugin window and no host merge repaints it. The register flip sits in item 9, *after* the
plugin leg. Putting it earlier would be the exact register defect this repo has a `CLAUDE.md` rule
about, one cycle after the v1.18 reflection named net-register targets as the wrong shape.

## Effort

**Total ≈ 5-7 hours.** No new dependencies in either repo, no spike. Heaviest is **item 5** (two
handlers plus a 30-call-site constructor question, see below) and **item 8** (the plugin swap plus a
four-theme eyes-on walk). Nothing here needs a split.

## Two measured facts that shaped this sequence

Both found by reading before planning. Recording them because they each removed a risk the spec left
open.

1. **The harness has already exercised a streaming RPC end-to-end.**
   `SubscribeMemoryPressure_ProductionAccessor_ReceivesRaisedSnapshot`
   ([`EndToEndContractTests.cs:1025`](../src/ROROROblox.PluginTestHarness/EndToEndContractTests.cs#L1025))
   raises on the real bus and reads from `ResponseStream` over the real pipe — exactly the shape item 4
   needs. There was a real risk that streams over a named pipe were untested territory and item 4 would
   quietly degrade to in-process stubs. **They are not, and it must not.** Copy that test's structure.
2. **`new PluginHostService(...)` appears at 30 call sites** — 17 in the harness, 13 in
   `PluginHostServiceTests`. A required 13th constructor parameter edits all 30 for no behavioural
   gain. See item 5's implementation note for how that is handled and why the cheap answer needs a
   guard.

## A note on test filters

`--filter "Foo*|Bar*"` **matches zero tests** — VSTest's grammar has no glob wildcards, and the run
reports success having executed nothing. This checklist uses `FullyQualifiedName~` throughout. That
defect shipped in every Verify field of the v1.17 checklist and a checkpoint could have been signed off
on nothing having run. Do not "simplify" these back.

---

## Checklist

- [x] **1. `ResolvedPalette`, and `ApplyTo` returns what it actually wrote**
  Spec ref: `spec.md > §4.1`, `spec.md > §4.2`, `spec.md > §0.2`
  What to build: `src/ROROROblox.Core/Theming/ResolvedPalette.cs` — an 11-slot record beside `Theme`
  and `ThemeSlots`. Change `ThemeService.ApplyTo` to return `(EdgeRemediation.Decision,
  ResolvedPalette)`, building the palette by **reading the eleven brushes back out of the
  `ResourceDictionary`** after all writes, not by accumulating as it writes. Add `CurrentPalette`
  (`ResolvedPalette?`) and `event Action<ResolvedPalette>? ThemeApplied` to `ThemeService`;
  `ApplyToResources` sets `CurrentTheme` and `CurrentPalette` first, then raises. `ApplyTo` stays
  `static` and dictionary-taking — item 4 and the unit tests depend on resolving a theme with no
  `Application`.
  Acceptance (`prd.md > Story 1.1`, `prd.md > Story 2.2`): the palette carries all eleven slots
  including `InteractiveEdge`; for a theme with an unparseable hex the palette reports **the brush
  actually in place**, not the record's value; `ThemeApplied` fires once per apply, after
  `CurrentPalette` is set.
  Verify: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~ThemeFeed"` green, including
  a test that feeds a deliberately malformed hex and asserts the palette disagrees with the record.
  That test is the whole reason this design was chosen over shipping `Theme` — if it cannot be made to
  fail against an accumulate-as-you-write implementation, the test is wrong, not the design.

- [x] **2. The contract grows, and the host still starts**
  Spec ref: `spec.md > §3.1`, `spec.md > §3.2`, `spec.md > §3.3`
  What to build: `ThemePalette` message (11 snake_case string fields, **no id, no name**) plus
  `rpc GetTheme(Empty) returns (ThemePalette)` and
  `rpc SubscribeThemeChanged(SubscriptionRequest) returns (stream ThemePalette)` on `RoRoRoHost`, next
  to the existing `Subscribe*` block. Add **both** methods to `RpcMethodCapabilityMap` as `null` with
  the reasoning comment. Bump `ROROROblox.PluginContract` `<Version>` 0.7.0 → 0.8.0. **Do not touch the
  wire `contract_version` literal at [`App.xaml.cs:876`](../src/ROROROblox.App/App.xaml.cs#L876).**
  Acceptance (`prd.md > Story 3.1`, `prd.md > Story 3.2`): `AssertExhaustive()` passes with both new
  methods; the app starts; the wire version string is still `"1.0"`; a plugin manifest declaring
  `contractVersion "1.0"` still handshakes.
  Verify: `dotnet build ROROROblox.slnx` 0 errors, then
  `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~CapabilityMap|FullyQualifiedName~Handshake"`
  green. **Deliberately omit a map entry once and confirm the app refuses to start**, then restore it —
  that is a five-minute check that the guard protecting this item is real rather than assumed.

- [x] **3. The bus carries a theme, and an adapter puts it there**
  Spec ref: `spec.md > §4.3`, `spec.md > §4.4`
  What to build: `event Action<ResolvedPalette>? ThemeChanged` on `IPluginEventBus`, implemented in
  `InProcessPluginEventBus` in the same shape as the other four. New
  `src/ROROROblox.App/Plugins/Adapters/ThemeFeedAdapter.cs` — subscribes to
  `ThemeService.ThemeApplied`, caches `Latest`, forwards to the bus, seeded at construction from
  `ThemeService.CurrentPalette`. Wire in `App.xaml.cs` DI. **Do not inject `IPluginEventBus` into
  `ThemeService`** — that points `Theming` at `Plugins` and inverts the direction every sibling bridge
  in `Adapters/` runs.
  Acceptance (`prd.md > Story 2.2`): applying a theme raises `ThemeChanged` exactly once with the
  resolved palette; the adapter's `Latest` is non-null immediately after construction on a booted app.
  Verify: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~ThemeFeedAdapter"` green.

- [x] **4. Write the wire tests, and watch them fail**
  Spec ref: `spec.md > §6`
  What to build: four tests in `EndToEndContractTests.cs`, structured on
  `SubscribeMemoryPressure_ProductionAccessor_ReceivesRaisedSnapshot` (:1025) — real Kestrel, real named
  pipe, real `RoRoRoHostClient`, **no in-process shortcut**: (a) `GetTheme` returns the active palette;
  (b) raising a theme change pushes a new palette to a live subscriber; (c) a subscriber receives the
  current palette on subscribe, before any change; (d) a `contract_version "1.0"` handshake is still
  accepted.
  Acceptance (`prd.md > Story 3.3`): tests (a)-(c) fail with `UNIMPLEMENTED` because item 5 has not
  happened yet. Test (d) passes immediately — it is the regression guard, not a new feature.
  Verify: `dotnet test src/ROROROblox.PluginTestHarness/ --filter "FullyQualifiedName~Theme"` and
  **read the failure text**. Three `UNIMPLEMENTED`s and nothing else. A pass here, a skip here, or a
  failure for any other reason means the test is not reaching the wire — stop and fix that before item
  5, because a gate that cannot fail proves nothing. Five separate instances of exactly that shipped
  during v1.17 and v1.18; this item exists in this position because of them.

- [ ] **5. The handlers, and item 4 goes green**
  Spec ref: `spec.md > §4.5`
  What to build: `GetTheme` returning the adapter's cached `Latest` mapped to `ThemePalette`.
  `SubscribeThemeChanged` copying `SubscribeMutexStateChanged`
  ([`PluginHostService.cs:280-309`](../src/ROROROblox.App/Plugins/PluginHostService.cs#L280-L309)) with
  **two deliberate departures, both of which need a comment saying they are deliberate**: bounded
  channel **capacity 1** with `DropOldest` (state, not occurrences — only the latest palette has ever
  mattered), and **write the current palette before entering the loop** so a subscriber paints on
  subscribe.
  **Implementation note — the 30 call sites.** `PluginHostService` is constructed at 30 places across
  the two test projects. Add the palette source as an **optional trailing constructor parameter
  defaulting to null**, so those 30 are untouched; when null, `GetTheme` returns `FailedPrecondition`
  and `SubscribeThemeChanged` completes immediately. **That cheap answer opens a hole and must not ship
  without its guard:** an optional dependency silently unwired in production is this session's recurring
  failure class in a new costume. Add a test that resolves `PluginHostService` from the real DI
  container and asserts its palette source is **not** null.
  Acceptance (`prd.md > Story 1.1`, `prd.md > Story 2.1`): item 4's three failing tests pass unchanged
  — do not edit them to fit the implementation.
  Verify: `dotnet test ROROROblox.slnx` fully green, unit and harness. Confirm the three previously
  `UNIMPLEMENTED` tests now pass and that **their assertions were not modified** (`git diff` on
  `EndToEndContractTests.cs` since item 4 should show additions only outside those three test bodies).
  → **CHECKPOINT C1.**

- [ ] **6. The author guide stops being the reason this happened**
  Spec ref: `spec.md > §3`, `prd.md > Epic 4`
  What to build: a theming section in [`docs/plugins/AUTHOR_GUIDE.md`](plugins/AUTHOR_GUIDE.md) — how to
  read the palette once, how to subscribe, what each of the eleven slots is for, and a worked snippet.
  State plainly that reading the host's `settings.json` or `themes` folder is **not** a supported
  integration and will break. Note the package version and that the wire version is deliberately
  unchanged.
  Acceptance (`prd.md > Story 4.1`): an author could wire theming from this page alone, without opening
  ur-task's source.
  Verify: read it against `spec.md > §3.1` and confirm all eleven slot names match the proto exactly.
  A guide with a wrong slot name is worse than no guide.

- [ ] **7. ur-task can call the two new methods**
  Repo: **`../rororo-ur-task`**
  Spec ref: `spec.md > §5.2`
  What to build: bump the `ROROROblox.PluginContract` package reference to 0.8.0 and expose both
  `GetTheme` and `SubscribeThemeChanged` through `PluginClient`, following how the existing
  subscriptions are surfaced there. **Do not touch `HostThemeReader` or `HostThemeService` yet** — this item is plumbing
  only, so item 8's diff is purely the swap.
  Acceptance: the project builds against 0.8.0 and both calls are reachable; theming behaviour is
  byte-for-byte unchanged (still the mirror, still the file watcher).
  Verify: `dotnet build` in the ur-task repo, 0 errors. Launch it against a running RoRoRo and confirm
  brand/midnight/magenta-heat still apply and flatline still does not. **Confirming the bug still
  reproduces is the point** — item 8 has nothing to prove otherwise.

- [ ] **8. The mirror dies**
  Repo: **`../rororo-ur-task`**
  Spec ref: `spec.md > §5.1`, `spec.md > §5.2`, `spec.md > §5.3`
  What to build: delete `HostThemeReader`'s `BuiltIns` array and three mirrored palettes,
  `ActiveThemeIdProperty`, `ThemeFileOptions`, `ReadActiveThemeId`, `ParseThemeFile` and
  `ResolveActive`. **Keep `Brand`** (the fallback constant) and **keep `BlendTowards`** (hover is
  derived, not read). In `HostThemeService`: `Start` calls `GetTheme` then holds
  `SubscribeThemeChanged`; delete the `FileSystemWatcher` and its debounce timer. `Apply` is unchanged
  — eight brush keys, replacement, `Freeze()`. Any feed failure is logged at debug and leaves the
  plugin on `Brand`, fully usable. Manifest → `version 0.6.0`, `minHostVersion "1.19.0"`.
  Acceptance (`prd.md > Story 5.1`): **flatline applies to ur-task's window**; all four built-ins apply;
  a user-authored theme still applies; editing the active user theme's colours still repaints; the
  plugin launches and works with RoRoRo closed.
  Verify: with RoRoRo running and ur-task open, switch through **all four built-ins and one
  user-authored theme**, confirming the plugin window follows each. Then close RoRoRo and confirm
  ur-task keeps running on brand without an error dialog. **This is the only proof in the cycle that
  F-091 is fixed** — the suite cannot span two processes, and saying otherwise would be this session's
  recurring failure one more time.
  → **CHECKPOINT C2.**

- [ ] **9. Documentation, security verification, and the row**
  Spec ref: `spec.md > §9`
  What to build: RoRoRo `<Version>` → 1.19.0.0 with the `Package.appxmanifest` in lockstep. **Flip
  F-091 to closed in [the findings register](superpowers/research/2026-08-04-rororo-settings-ui-audit-findings.md)
  citing item 8's commit, not item 5's** — the row's evidence is the plugin window. Update
  `docs/features.md` (it has drifted live three times in two days). Sync `CLAUDE.md`'s file table.
  **Archive `docs/spec.md` → `docs/superpowers/specs/2026-08-11-rororo-plugin-theme-feed-design.md`.**
  Security pass: `dpapi-cookie-blast-radius` agent (the feed adds a new outbound channel — confirm it
  carries nothing but hex), local-path grep, dependency audit, secret scan.
  Acceptance: register shows F-091 closed with the right evidence; versions in lockstep; spec archived;
  no cookie reachable from the theme path; no `c:\Users\` in committed code.
  Verify: `dotnet test ROROROblox.slnx` green; `git log --oneline` shows the register flip after the
  plugin work; confirm the archived spec exists at its new path **before** the next Cart round.

---

## Checkpoints

**C1 (after item 5)** — the wire gate. The feed provably works over a real named pipe, the tests that
prove it were written before the code and observed failing, and no existing plugin is broken. Nothing
visible has changed on screen yet; this checkpoint is about the contract being sound before another
repo depends on it.

**C2 (after item 8)** — the only eyes-on gate, and the only place F-091 is actually proven. Four
built-ins plus a user theme, in a real plugin window, plus the host-closed case.

## What this cycle must not do

- **Do not change the wire `contract_version` string.** It is compared by exact match; changing it
  rejects every existing plugin. Highest-consequence line in the cycle.
- **Do not add `flatline` to ur-task's table** as a shortcut if item 8 runs long. That is a fifth copy
  of the palette and leaves the next built-in broken the same way. If item 8 cannot land, the host leg
  ships alone and **F-091 stays open** — which is a fine outcome and an honest one.
- **Do not flip F-091 in item 5** or anywhere before the plugin leg lands.
- **Do not edit item 4's three tests in item 5.** If they need changing to pass, the implementation is
  wrong or the test was never reaching the wire.
- **Do not start on F-068, F-046 or F-050.** Standing exclusions, unchanged.
