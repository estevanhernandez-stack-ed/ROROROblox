# RORORO — Technical Spec: the host tells plugins what colour it is

**This file is the canonical technical artifact for the v1.19.0 cycle.** `docs/spec.md` is overwritten
every Cart round; **archive this into `docs/superpowers/specs/` before the next round.** v1.17's was
nearly lost that way and v1.18's was archived just in time
([`2026-08-10-rororo-settings-remediation-design.md`](superpowers/specs/2026-08-10-rororo-settings-remediation-design.md)).

Implements [`docs/prd.md`](prd.md). Closes F-091 across two repositories.

**Anchor:** a plugin should never need to know where the host keeps its themes.

---

## §0 — What changed between `/prd` and here

Two of the PRD's four open questions closed on inspection, and one of them was the fork the scope
deliberately refused to default. Both closed the same way: **the mechanism already existed.**

### §0.1 The discovery fork dissolved. `minHostVersion` is already the answer, and it was built for this

The PRD asked how a plugin discovers whether the host supports the feed, and offered two designs:
catch `UNIMPLEMENTED` and fall back, or add a capability list to `HostInfo`.

**Neither is needed.** `PluginInstaller` already refuses an install whose manifest declares a
`minHostVersion` newer than the running host
([`PluginInstaller.cs:87-101`](../src/ROROROblox.App/Plugins/PluginInstaller.cs#L87-L101)), with a
user-facing message: *"This plugin requires RoRoRo X or newer. You're running Y. Update RoRoRo and try
again."* `MarketplacePlan` greys the entry out in the catalogue before the user even clicks
([`MarketplacePlan.cs:72-80`](../src/ROROROblox.App/Plugins/MarketplacePlan.cs#L72-L80)).

The decisive evidence is the comment sitting directly above that gate, which names this exact
scenario as the reason the gate exists:

> *…so the user gets a "this needs a newer RoRoRo" message instead of a downstream symptom (the
> plugin's gRPC client failing because it expects a method this host doesn't expose, for instance).*

A plugin that needs the feed declares the host version that ships it. That is the whole protocol.
**No new RPC, no new vocabulary, no error-code semantics, and nothing for a future addition to
retrofit.** Adding a feature-advertisement mechanism next to a working version gate would have been a
second answer to a solved question, and a growing untyped string vocabulary to maintain alongside
`PluginCapability`.

**The one gap, and how it is covered without new surface.** `minHostVersion` guards *install*, not a
later host downgrade — a user who sideloads an older RoRoRo after installing the plugin is outside the
gate. The plugin needs a tolerant failure path there. It already needs one for "RoRoRo is not running
at all," which is a normal and supported state (§5.3). One path covers both. **Belt and braces, zero
new contract surface.**

This is the second consecutive cycle where the highest-consequence open fork answered itself on
inspection — v1.18's was `SectionHeadingStyle` already shipping with the exact margin the missing level
needed. Both times the cost was a single read.

### §0.2 The palette is eleven slots, not ten, and it must be the *applied* values

The scope and PRD both said "every slot the host theme defines," reasoning from the `Theme` record's
ten authored colours. **Reading `ThemeService.ApplyTo` changes the answer, and the reason matters more
than the count.**

`ApplyTo` writes **eleven** brushes
([`ThemeService.cs:284-312`](../src/ROROROblox.App/Theming/ThemeService.cs#L284-L312)). The eleventh,
`InteractiveEdgeBrush`, is **derived** rather than authored: whether it is derived at all depends on
`EdgeRemediation.Decide`, which reads `theme.IsBuiltIn` and a per-theme answer persisted in settings.
It is not in the `Theme` record and never will be — the comment there says so explicitly, calling out
that deriving rather than adding an eleventh slot means *"every user theme already on disk is covered
without its author touching anything (invariant 6 — the contract does not grow)."*

Worse for the naive design: **`ApplySlot` returns early when a hex will not parse and leaves the
previous brush in place** ([`ThemeService.cs:320-327`](../src/ROROROblox.App/Theming/ThemeService.cs#L320-L327)).
The same file states the consequence outright — *"the record can say one thing while the app shows
another"* — which is precisely why `ContrastPairGateTests` measures the produced brushes rather than
the record.

So a feed carrying the `Theme` record would ship a plugin two defects:

1. **No interactive edge.** A plugin wanting one would have to re-implement `ContrastGuard` and
   `EdgeRemediation` — a *sixth* copy of host logic, in a different repository. That is the defect this
   cycle exists to remove, one level up and worse.
2. **Values the host is not displaying.** On an unparseable hex the record and the screen disagree, and
   the plugin would faithfully render the wrong one.

**Decision: the feed carries what `ApplyTo` produced, read back from the resource dictionary.** The
contrast gate already reached this conclusion for the same reason; the feed inherits its reasoning
rather than re-litigating it.

---

## §1 Stack

No new dependencies in either repository. Everything below is already in the tree.

| Piece | Already present |
| --- | --- |
| Contract | `ROROROblox.PluginContract` — protobuf + Grpc.Tools 2.68.0, Google.Protobuf 3.28.3 |
| Host transport | Kestrel over a named pipe, `PluginHostStartupService` |
| Host theming | `ROROROblox.Core/Theming/` (`Theme`, `ThemeSlots`, `ThemeStore`), `App/Theming/ThemeService` |
| Plugin bridge | `App/Plugins/Adapters/` — the established seam between RoRoRo singletons and plugin interfaces |
| Integration proof | `ROROROblox.PluginTestHarness` — real Kestrel, real named pipe, real `RoRoRoHostClient` |
| Plugin side | `rororo-ur-task`, WPF, `HostThemeService` already applies palettes by brush replacement |

**Versions.** Two numbers, and only one moves — restated here because the register row blurred them and
the PRD had to correct it:

| Number | Now | After | Why |
| --- | --- | --- | --- |
| `ROROROblox.PluginContract` NuGet package | 0.7.0 | **0.8.0** | New messages and RPCs |
| Wire `contract_version` string | `"1.0"` | **`"1.0"`** | Exact-match at `PluginHostService.cs:70`; changing it rejects every existing plugin |
| RoRoRo app | 1.18.0.0 | **1.19.0.0** | Cycle release |
| ur-task | 0.5.0 | **0.6.0**, `minHostVersion: "1.19.0"` | Separate repo, separate release |

## §2 Architecture overview

```text
RoRoRo host process                                    plugin process (ur-task)
───────────────────────────────────────────────        ──────────────────────────────

 Settings picker ──┐
                   ├──> ThemeService.ApplyToResources
 startup ──────────┘            │
                                ├─ ApplyTo(resources, theme, edgeAnswer)
                                │     writes 11 brushes, derives the edge
                                │     reads back ──> ResolvedPalette   [Core]
                                │
                                └─ raises ThemeApplied(ResolvedPalette)
                                          │
                    ThemeFeedAdapter  <───┘        [App/Plugins/Adapters]
                     · caches Latest
                     · forwards to the bus
                                          │
                    InProcessPluginEventBus.ThemeChanged
                                          │
                    PluginHostService                            gRPC / named pipe
                     · GetTheme ───────────── unary ─────────────>  paint once on connect
                     · SubscribeThemeChanged ─ stream ───────────>  repaint on change
                                                                        │
                                                                 HostThemeService.Apply
                                                                  replaces 8 brushes
```

**The two RPCs are not redundant.** `GetTheme` answers *"what colour are you right now"* for a plugin
that connects mid-session; the stream answers *"tell me when that changes."* A theme is state, and the
contract already draws this line — `GetRunningAccounts` pairs with the launch and exit streams while
`SubscribeMemoryPressure` has no paired read, because pressure is an occurrence.

## §3 Contract changes

PRD ref: `prd.md > Epic 1`, `prd.md > Epic 2`.

### §3.1 `ThemePalette`

```proto
// The host's ACTIVE palette, as applied. Resolved colours only -- no id, no
// name, no file path. A plugin that receives this has nothing to look up,
// which is the point: looking things up is what F-091 was.
//
// These are the values ThemeService.ApplyTo actually wrote, read back from the
// resource dictionary rather than copied from the Theme record. On an
// unparseable hex ApplySlot leaves the previous brush in place, so the record
// and the screen can disagree -- the screen wins here, same rule the contrast
// gate follows.
message ThemePalette {
  string bg = 1;
  string cyan = 2;
  string magenta = 3;
  string white = 4;
  string muted_text = 5;
  string divider = 6;
  string row_bg = 7;
  string row_expired_bg = 8;
  string row_expired_accent = 9;
  string navy = 10;
  // Derived, not authored: EdgeRemediation decides whether the authored divider
  // clears WCAG 1.4.11's 3:1 against the surface and substitutes when it does
  // not. Absent from the Theme record by design. A plugin cannot compute this
  // without a sixth copy of host logic, which is why it ships on the wire.
  string interactive_edge = 11;
}
```

Every field is `#RRGGBB`. Snake_case field names per the file's existing convention; **no `id` field,
at any point, for any reason.**

### §3.2 RPCs on `RoRoRoHost`

```proto
  // Theming (v1.19). Additive: the wire contract_version stays "1.0", so a
  // plugin that never calls these is unaffected and still handshakes.
  rpc GetTheme(Empty) returns (ThemePalette);
  rpc SubscribeThemeChanged(SubscriptionRequest) returns (stream ThemePalette);
```

Placed next to the existing `Subscribe*` block. `Empty` and `SubscriptionRequest` already exist.

### §3.3 Capability map — the entry that keeps the host bootable

PRD ref: `prd.md > Story 3.2`.

```csharp
["GetTheme"] = null,                  // free read -- a colour is not sensitive
["SubscribeThemeChanged"] = null,     // free stream -- see below
```

**These entries are not optional and not cosmetic.** `RpcMethodCapabilityMap.AssertExhaustive()` walks
the generated service descriptor at startup and **throws if a contract method has no entry**, and
`CapabilityInterceptor` independently fails closed on an unmapped method at call time. Both were built
deliberately after `UpdateUI` and `RemoveUI` once shipped ungated. Adding the RPCs without the map
entries does not produce a permissive hole; it produces an app that will not start.

**Ungated is a deliberate first, and it is affirmed rather than inherited.** Every stream that exists
today is capability-gated and every ungated entry is a one-shot read, so `SubscribeThemeChanged` is the
first ungated stream. Affirmed because the gate exists to fence things that can cause harm — a
capability the user can decline is a capability that lets a plugin be *denied the ability to look
correct*, which is a worse outcome than any risk of knowing a hex code. The stream carries no account
data, no identity and no host state beyond colour.

## §4 Host implementation

### §4.1 `ResolvedPalette` — Core

**New:** `src/ROROROblox.Core/Theming/ResolvedPalette.cs`. Core, beside `Theme` and `ThemeSlots`,
because Core has no UI dependency and both the App layer and the contract-facing layer need it.

```csharp
/// The eleven brush values a theme resolves to once applied, keyed by ThemeSlots.
/// Distinct from Theme on purpose: Theme is what an author wrote, this is what the
/// app is showing. They differ when a hex will not parse (ApplySlot leaves the old
/// brush) and they always differ by InteractiveEdge, which is derived.
public sealed record ResolvedPalette(
    string Bg, string Cyan, string Magenta, string White, string MutedText,
    string Divider, string RowBg, string RowExpiredBg, string RowExpiredAccent,
    string Navy, string InteractiveEdge);
```

### §4.2 `ThemeService.ApplyTo` returns what it wrote

`ApplyTo` currently returns `EdgeRemediation.Decision`. It gains a second return value: the palette
**read back out of the dictionary** after all eleven writes.

Read-back rather than accumulate-as-you-write, deliberately: read-back is the only version that is
correct when `ApplySlot` early-returns on an unparseable hex, and being correct in exactly that case is
the entire reason this design was chosen over shipping the `Theme` record.

`ApplyToResources` then stores the result on a new `CurrentPalette` property beside the existing
`CurrentTheme`, and raises `ThemeApplied(palette)` **after** both are set, so a subscriber can never
observe a palette the service has not finished adopting.

Three members land on `ThemeService`, named here so `/checklist` does not have to infer them:

| Member | Shape | Why |
| --- | --- | --- |
| `ApplyTo` | returns `(EdgeRemediation.Decision, ResolvedPalette)` | Still `static` and dictionary-taking — §6 depends on that |
| `CurrentPalette` | `ResolvedPalette?` | What §4.3 seeds its cache from; null only before the startup apply, which no plugin can observe (§4.5) |
| `ThemeApplied` | `event Action<ResolvedPalette>?` | Plain event, no plugin types — the adapter is what knows about plugins |

### §4.3 `ThemeFeedAdapter` — App/Plugins/Adapters

**New:** `src/ROROROblox.App/Plugins/Adapters/ThemeFeedAdapter.cs`.

`ThemeService` raises a plain `event Action<ResolvedPalette>? ThemeApplied` and knows nothing about
plugins. The adapter subscribes, caches `Latest`, and forwards to the bus.

**Why an adapter and not an injected bus:** `Plugins/Adapters/` exists for exactly this — bridging
RoRoRo singletons to plugin interfaces — and every sibling in that folder does it this way. Injecting
`IPluginEventBus` into `ThemeService` would point `Theming` at `Plugins`, inverting the direction every
other bridge runs. Matching the established pattern first, deviating only on purpose.

The cache is load-bearing, not an optimisation: it is what lets `GetTheme` answer at any moment,
including before the user has ever changed a theme. Seeded at construction from
`ThemeService.CurrentPalette`.

### §4.4 `IPluginEventBus` gains a fifth event

```csharp
/// The host's active palette, raised on every theme application including the
/// one at startup. Carries resolved colours, never an id -- a plugin that
/// receives an id would need somewhere to look it up, which is F-091.
event Action<ResolvedPalette>? ThemeChanged;
```

`InProcessPluginEventBus` implements it in the same shape as the other four.

### §4.5 `PluginHostService` — the two handlers

`GetTheme` returns the adapter's cached `Latest`, mapped to `ThemePalette`.

`SubscribeThemeChanged` copies `SubscribeMutexStateChanged`
([`PluginHostService.cs:280-309`](../src/ROROROblox.App/Plugins/PluginHostService.cs#L280-L309)) —
bounded channel, handler subscribe, `await foreach` write, unsubscribe in `finally` — with **two
deliberate departures:**

1. **Capacity 1, not 64.** PRD Story 2.1 requires a stalled plugin to catch up to the *current*
   palette and never replay a backlog. With `DropOldest`, capacity 1 makes that behaviour structural
   rather than a comment: the channel can only ever hold the newest palette. A 64-deep queue of
   superseded themes is not useful to anyone.
2. **The current palette is written first, before the loop.** A subscriber gets painted immediately
   rather than waiting for the next change. This makes `GetTheme` optional for a plugin that only ever
   subscribes, while remaining necessary for one that wants a palette without holding a stream.

**Startup ordering is proven, not assumed** (PRD edge case #1). `ThemeService.ApplyAtStartup()` runs at
[`App.xaml.cs:167`](../src/ROROROblox.App/App.xaml.cs#L167); `PluginHostStartupService.StartAsync` is
dispatched at [`App.xaml.cs:1998`](../src/ROROROblox.App/App.xaml.cs#L1998). A theme is always applied
before the pipe accepts a connection, so there is no "no palette yet" state to represent.

## §5 Plugin implementation — `rororo-ur-task`

PRD ref: `prd.md > Epic 5`. Separate repository, separate release, **not required for the host leg to
ship.**

### §5.1 What is deleted

`HostThemeReader`'s `BuiltIns` array and the three mirrored palettes, plus `ActiveThemeIdProperty`,
`ThemeFileOptions`, `ReadActiveThemeId`, `ParseThemeFile` and `ResolveActive` — the five host-storage
couplings named in scope. `BlendTowards` **stays**: hover is derived from `RowBg`, not read from the
host, and no contract change affects it.

### §5.2 What replaces it

`HostThemeService.Start` calls `GetTheme` once, applies, then holds `SubscribeThemeChanged` and applies
on each palette. The `FileSystemWatcher` and its debounce timer go with the reader — the host now tells
the plugin, so the plugin no longer watches. `Apply` is unchanged: still eight brush keys by
replacement, still `Freeze()`, still hover-by-tint. Seven slots map straight across and three arrive
unused, which is the point of shipping all eleven.

### §5.3 The fallback — the PRD's third answer, and it is the right one

The PRD asked whether ur-task keeps its disk reader as the no-host fallback or collapses to the `Brand`
constant, and flagged that a third answer might exist. **It does: keep the `Brand` constant, delete the
reader.**

The disk reader cannot survive, because every coupling it embodies is what the cycle removes — keeping
it "just for the offline case" keeps all five. Collapsing to `Brand` costs exactly one thing: a plugin
launched while RoRoRo is closed paints brand rather than the user's last theme. That is acceptable
because it is **already the behaviour for flatline today**, it self-corrects the moment the host comes
up and the plugin connects, and the alternative is retaining the entire defect for a transient state.

This same path covers a downgraded host (§0.1): a failed `GetTheme` or `SubscribeThemeChanged` is
treated as "no host feed," logged at debug, and the plugin stays on `Brand` and fully usable. **A
theming failure must never take the plugin down** — that posture is unchanged from the current reader,
which falls back to `Brand` on every error path.

## §6 What gets tested, and where

`prd.md > Story 3.3` requires the proof to run over the wire rather than in-process. It can, because
the harness already exists.

| Test | Project | Proves |
| --- | --- | --- |
| `ApplyTo` returns the eleven applied values | `Tests` | Read-back, not record-copy |
| Unparseable hex → palette reports the brush actually in place | `Tests` | §0.2's whole argument |
| `InteractiveEdge` present and equals the remediated value | `Tests` | The derived slot ships |
| Startup apply raises `ThemeChanged` once | `Tests` | Story 2.2 |
| `AssertExhaustive` passes with both new methods | `Tests` | Story 3.2, and the host boots |
| `GetTheme` over a real pipe returns the active palette | **Harness** | Story 1.1 |
| Switching themes pushes a new palette to a live subscriber | **Harness** | Story 2.1 |
| A subscriber is painted on subscribe, before any change | **Harness** | §4.5 departure 2 |
| Handshake with `contract_version "1.0"` still accepted | **Harness** | Story 3.1 — the highest-consequence line in the cycle |

The `ThemeChanged`-on-startup test needs a palette without an `Application`; `ApplyTo` is already
`static` and dictionary-taking *precisely so a theme can be resolved with no `Application` and no
`ThemeService` instance*, so a bare `ResourceDictionary` is enough.

## §7 File structure

```text
ROROROblox/
├── src/
│   ├── ROROROblox.PluginContract/
│   │   ├── Protos/plugin_contract.proto        # M ThemePalette + 2 RPCs (§3.1, §3.2)
│   │   └── ROROROblox.PluginContract.csproj    # M 0.7.0 -> 0.8.0
│   ├── ROROROblox.Core/Theming/
│   │   ├── Theme.cs                            # . ThemeSlots unchanged, 11 keys already
│   │   └── ResolvedPalette.cs                  # + applied values, 11 slots (§4.1)
│   ├── ROROROblox.App/
│   │   ├── Theming/ThemeService.cs             # M ApplyTo read-back + ThemeApplied (§4.2)
│   │   └── Plugins/
│   │       ├── IPluginEventBus.cs              # M fifth event (§4.4)
│   │       ├── InProcessPluginEventBus.cs      # M implement it
│   │       ├── RpcMethodCapabilityMap.cs       # M two ungated entries (§3.3)
│   │       ├── PluginHostService.cs            # M GetTheme + SubscribeThemeChanged (§4.5)
│   │       └── Adapters/ThemeFeedAdapter.cs    # + cache + forward (§4.3)
│   ├── ROROROblox.Tests/ThemeFeedTests.cs      # + unit rows from §6
│   └── ROROROblox.PluginTestHarness/
│       └── EndToEndContractTests.cs            # M four wire rows from §6
├── docs/plugins/AUTHOR_GUIDE.md                # M theming section (Epic 4)
└── docs/spec.md                                # this file -- ARCHIVE before next round

rororo-ur-task/                                  # separate repo, separate release
├── src/Theming/
│   ├── HostThemeReader.cs                      # M keep Brand + BlendTowards, delete the rest (§5.1)
│   └── HostThemeService.cs                     # M feed replaces the watcher (§5.2)
├── src/PluginHost/PluginClient.cs              # M expose the two calls
└── manifest.json                               # M 0.6.0, minHostVersion 1.19.0
```

`+` new · `M` modified · `.` unchanged, listed for orientation

## §8 Key technical decisions

1. **The feed carries applied brushes, not the `Theme` record.** Eleven slots including the derived
   `InteractiveEdge`, read back from the resource dictionary. *Tradeoff:* the palette is no longer a
   direct projection of an on-disk shape, so a reader has to know "applied" and "authored" are
   different things. Accepted because the alternative ships a plugin either a missing slot or a colour
   the host is not displaying, and because `ContrastPairGateTests` already established this exact rule
   for the same reason.
2. **`minHostVersion` is the discovery mechanism.** No capability list, no `UNIMPLEMENTED` handling as
   a protocol. *Tradeoff:* a host downgraded after install is outside the gate. Accepted because the
   plugin needs a tolerant failure path anyway for the host-not-running case, and one path covers both.
3. **Both RPCs are ungated, and it is the first ungated stream.** *Tradeoff:* a deliberate break in an
   established pattern. Accepted because a declinable colour capability means a plugin can be denied
   the ability to look correct, and the stream carries nothing but hex codes.
4. **Stream capacity 1 with `DropOldest`, and the current palette written before the loop.**
   *Tradeoff:* diverges from the four existing streams' capacity 64. Accepted because those carry
   occurrences where every item matters and this carries state where only the last one does — making
   the requirement structural beats documenting it.
5. **A bridge adapter, not an injected bus.** *Tradeoff:* one more small type. Accepted because it
   keeps `Theming` unaware of `Plugins` and matches every sibling in `Adapters/`.
6. **ur-task drops the disk reader entirely rather than keeping it as an offline fallback.**
   *Tradeoff:* a plugin started while RoRoRo is closed paints brand instead of the user's last theme.
   Accepted because keeping the reader keeps all five couplings for a transient state that
   self-corrects on connect.

## §9 Open issues

- **Neither leg closes F-091 alone.** The row's evidence is a mis-coloured plugin window. The host leg
  is releasable and useful — it makes every *future* plugin correct by default — but the row stays open
  until ur-task 0.6.0 ships. `/checklist` must not place a register flip in the host leg.
- **The three hand-synced palette copies inside RoRoRo are untouched.** The plugin mirror was the
  fourth; this removes only that one. Still an open issue, still out of scope.
- **`RowBadgeSpec.color_hex` is the same defect inverted** — a plugin choosing a colour painted into the
  *host's* window. Declined at `/prd` on evidence: `WpfPluginUIHost` is a stub that logs and renders
  nothing, so nothing is mis-coloured today. It becomes real when that UI does.
- **The plugin leg's verification is eyes-on and cross-repo.** Four themes in ur-task's window is the
  proof, and no harness spans both processes. The host leg's harness coverage does not extend here, and
  saying otherwise would be this session's recurring failure one more time.
- **`AUTHOR_GUIDE.md` documents a contract no third party consumes yet.** ur-task is the only plugin.
  Worth writing anyway — the guide's absence is why the coupling was invented in the first place.
