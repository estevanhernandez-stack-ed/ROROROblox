# Rendering whole windows in the suite — what exists, what is missing, what stays human

> ## ⚠ BANNER CORRECTION — the §5.1 spike ran, 2026-08-12
>
> **Built and green.** `WindowRenderHost.cs`, `ThemedWindowRender.cs`, `AboutMarkRenderTests.cs`.
> Full suite 1,606 + 22. The About mark's eyes-on item is retired.
>
> **§3.1 was wrong, and it was the load-bearing claim.** It said the theme dictionary could be
> merged into the finished window because `DynamicResource` resolves element → window → application.
> It cannot. Window markup takes app styles with **`{StaticResource}`, which resolves at PARSE time
> inside `InitializeComponent()`** — `AboutWindow.xaml:155` takes `SecondaryStrongButtonStyle` that
> way and **26 App XAML files** do the same. With no `Application` and nothing yet on the window the
> parse throws `Cannot find resource named 'SecondaryStrongButtonStyle'` and the window never exists
> to be merged into. **An `Application` is unavoidable**, which the design had ruled out entirely.
>
> The resolution is a split: `WindowRenderHost` owns one `Application` whose `Resources` hold only
> the **theme-independent** vocabulary (WPF-UI + `ControlStyles.xaml`), populated once and never
> mutated; **theme brushes stay on the window**, per-render. That keeps the hazard `ThemedRender`
> names — process-global state changing how other tests resolve a *theme* — out of scope, because
> there is no theme state on the Application to race over. Verified: full suite green, existing
> render gates unaffected.
>
> **§3.2 was half right and its stated reason was wrong.** Render-then-crop is not the safe move.
> `RenderTargetBitmap.Render` does not apply the **root's** own offset either, and About's content
> Grid carries `Margin="32,28"` — so the bitmap was shifted up-left by the margin while
> `TransformToAncestor` reported unshifted coordinates, and the 64×64 crop landed mostly on
> transparent Grid: **2,944 of 4,096 sampled pixels were nothing at all**, and it still produced a
> stable per-theme hash. Replaced with a `VisualBrush` of the target drawn at the origin, which has
> no offset to get wrong in either direction.
>
> **§3.3, §3.4 and §4 held.** Constructor ladder as described; `DrainQueue` covers the async-void
> path; the automatable/human split stands.
>
> **§4's headline assertion was wrong in the other direction, and the spike corrected the test, not
> the app.** "Hash the mark region and compare across themes" can never pass: the **plate is inside
> the region and is supposed to differ** — item 4 bound it deliberately. The assertion has to
> separate the mark's fixed faces from the themed ground. It now compares per-colour pixel counts
> restricted to the eight artwork hexes, with the plate asserted to vary in a sibling clause so the
> identity clause cannot pass by nothing changing.
>
> **5.3 SHIPPED. 5.4 DOES NOT WORK, AND WILL NOT UNTIL F-100 IS FIXED.** History rows now render at
> 96/120/144 DPI across all four themes, and the gutter-exceeds-inset invariant is asserted in
> laid-out pixels rather than in constants. The banner pair was attempted and backed out.
>
> `MainViewModel` has **eight `Application.Current?.Dispatcher.Invoke` call sites** that silently
> no-op in the ordinary suite, because `Application.Current` is null there and every one of them is
> null-conditional. On the render host `Application.Current` is deliberately live, so they become
> real cross-thread marshals: presence, activity and session-expiry events fire on background
> threads and block against a dispatcher already inside a render. Building more than one view model
> there wedges the host past its budget.
>
> That is **F-100**, and it is a larger finding than the test that surfaced it — those eight
> delegate bodies have never executed under test at all. The fix is a dispatcher seam, not an
> Application in the ordinary suite, which would reintroduce the process-global hazard
> `ThemedRender` documents.
>
> The section 3.3 cost estimate was wrong in an unexpected direction: the 29-parameter builder was
> already `internal` and cost nothing, while the blocker turned out to be the view model's coupling
> to a live Application, which the design never considered.
>
> **A third harness bug, found by MainWindow and fixed.** `Arrange` measured at
> `double.PositiveInfinity`, so `TextWrapping="Wrap"` never wrapped and `RenderTargetBitmap` tried
> to allocate a bitmap sized for unwrapped prose. AboutWindow is `NoResize` at 500x460 and never
> showed it. Windows are now measured at their declared size.
>
> **One residue found:** `NavySoftBrush` is declared but paints nothing — it was the Canvas ground
> until item 4 bound that ground to `RowBgBrush`. Seven of eight artwork brushes actually paint.
> Wants a register row, not a silent delete.

**Status:** design, superseded in part by the spike above. Written 2026-08-12 after v1.21 closed
owing four eyes-on checks.
**Verification note, stated up front because this repo has been bitten by the opposite:** everything
in §1 is verified by reading the code. Everything in §3 is derived from that reading plus WPF
semantics and is **not verified by running**. The spike in §5 exists to confirm or kill §3, and its
findings should be banner-corrected onto this file rather than folded in silently.

---

## §0 The correction that reframes the question

The v1.21 close-out said the suite "has no dispatcher" and owed four checks to a human. That
sentence was repeated from `ExpiredRowRedundancyTests`' own header:

> *"Nothing in this repo renders — the suite is headless xUnit with no STA thread and no
> dispatcher."*

**That comment is stale, and it is stale in the direction that costs the most.** It tells every
future reader that pixel verification is impossible here, which is the reason four checks were
handed to a human without anyone checking whether they had to be.

Same defect class as `XamlLiteralColour`'s comment (corrected at v1.21 item 5, which claimed
comment-stripping behaviour that `StripXmlComment` had reversed a cycle earlier) and
`RawTheme`'s camelCase comment (F-053, where documentation caused the bug it described). **A
comment that understates the suite's capability is not harmless; it is a standing instruction to
not try.**

## §1 What already exists, verified by reading

`src/ROROROblox.Tests/Rendering/` is a complete offscreen render harness.

| Piece | What it does |
|---|---|
| `Sta.Run<T>(work, what)` | Fresh STA thread per call, 60s budget, `ExceptionDispatchInfo` marshalling, explicit `InvokeShutdown`. Fresh-per-call is deliberate: WPF caches Dispatcher and resource-attachment state per thread, so reuse would let one theme leak into the next **and the leak would look like a pass**. |
| `Sta.DrainQueue()` | Pushes a `DispatcherFrame` and terminates it from a `DispatcherPriority.Loaded` callback. This is the real dispatcher pump — it flushes `DynamicResource` invalidation and template application, which are queued rather than immediate. |
| `ThemedRender.Resources(theme)` | Builds the dictionary **in App.xaml's own merge order** (WPF-UI themes → controls → `ControlStyles.xaml`) and applies the theme through the shipped `ThemeService.ApplyTo` seam rather than a reimplementation. |
| `ThemedRender.Measure(...)` | Measure → Arrange → `UpdateLayout` → `DrainQueue` → `RenderTargetBitmap` → per-pixel histogram + bounds. Carries a `#FF00FF` **sentinel host** so host-showing-through reports as an obviously wrong colour instead of a plausible ratio. Takes a `dpi` parameter because two shipped geometry defects only existed at fractional scaling. |
| `Sample` | Fill / Foreground / Edge / `SentinelLeaked` / histogram / `BoundsOf(hex)`. |

Three consumers already depend on it: `RenderedStyleGateTests`, `ButtonStateGateTests`,
`SelectionDotGeometryTests`. The last one exists because **a human found two geometry defects by
eye that every colour assertion passed straight over** — the harness grew `Bounds` in response.

**So the dispatcher question is answered. It is built, it is careful, and it is in use.**

## §2 The actual gap

`ThemedRender.Styled(dict, styleKey)` constructs a **synthetic control** from a keyed style:
`new Button { Content = "MMMM", FontSize = 48, Style = s }`.

That is exactly right for what it was built for — proving a *rank* renders correctly in every theme
and state. It is structurally unable to answer any of v1.21's four owed checks, because every one of
them is about **a real window with real content in a real arrangement**:

1. Both banners visible at once, read as two different warnings.
2. The About mark identical across four themes.
3. Three History sessions reading as three rows.
4. The consent sheet with both capability namespaces.

The gap is **window-level rendering**, not dispatcher support.

## §3 The design — `ThemedWindowRender`

A sibling to `ThemedRender`, reusing `Sta` and `Resources` unchanged.

```csharp
internal static Sample MeasureWindow(
    Theme theme, string what, Func<Window> build, double dpi = 96)
```

Four obstacles, each with the approach and the reason. **These are the unverified claims.**

### 3.1 `Application.Current` must stay null — so merge, never assign

`ThemedRender`'s header is emphatic that no `Application` is ever constructed, because
`ThemeService.Apply` reads `Application.Current?.Resources` and an instance would be process-global
state changing how every other test in the assembly resolves a theme.

Consequence: a window's `Background="{DynamicResource BgBrush}"` has no Application to resolve
against. `DynamicResource` walks element → window → application, so injecting the dictionary at
**window** level is sufficient:

```csharp
window.Resources.MergedDictionaries.Add(ThemedRender.Resources(theme));
```

**Merge, do not assign.** `AboutWindow` declares its own `Window.Resources` holding the eight
artwork brushes; assigning `window.Resources = dict` would delete the mark and the About gate would
then be measuring a window that cannot render its own logo.

### 3.2 Do not `Show()` — render the content

A never-shown `Window` has no arranged visual, so `RenderTargetBitmap.Render(window)` samples
nothing. Showing it offscreen works but flashes, needs a real HWND, and is hostile to CI.

Render `window.Content` instead: measure and arrange it in place, then render that element. Resource
lookup still walks up to the Window (its logical parent), so §3.1's merge is what makes this work
without reparenting. Reparenting the content into a sentinel host would break exactly that lookup.

The sentinel wrapper is still wanted for leak detection; the content should be placed **inside** a
sentinel `Border` only if resource lookup is preserved, which needs testing — this is the single
most likely place §3 is wrong.

### 3.3 Constructor cost is the real effort ladder

| Window | Constructor | Effort |
|---|---|---|
| `AboutWindow` | `()` | **Free.** |
| `ConsentSheet` | `(PluginManifest)` | One value object. |
| `SessionHistoryWindow` | `(ISessionHistoryStore, IFavoriteGameStore, IRobloxApi, IStreamerIdentityProvider?)` | Four interfaces; fakes exist in the suite. |
| `MainWindow` | `(MainViewModel)` — and `MainViewModel` takes **29 constructor parameters** | Expensive, but `MainViewModelTests` already has a private builder with optional parameters that fakes all of them. Extract it to `internal` rather than writing a second one. |

**A second fake-set for `MainViewModel` would be the mistake here.** This repo has shipped a fix
into one scanner and missed its copy in another twice; a duplicate harness is that pattern with
better manners.

### 3.4 Loaded handlers, fonts, and the two honest limits

- `SessionHistoryWindow.OnLoaded` is `async void` → `ReloadAsync` → `_store.ListAsync()`. Async
  continuations post at `Normal`, which is **higher** than `Loaded`, so `Sta.DrainQueue()` should
  flush them — provided the fakes complete synchronously. A fake returning a real `Task.Delay` would
  not be covered and would sample an empty list as a confident pass.
- **Fonts.** Space Grotesk / Inter / JetBrains Mono may be absent on a CI agent, and WPF falls back
  silently. Colour assertions are unaffected; **layout and geometry assertions are not**, and a
  glyph-metrics gate that is green on a dev box and meaningless in CI is worse than no gate.

## §4 What this can and cannot assert — the part that matters

This repo's own gates warn twice about instruments that claim more than they measure
(F-098) and about *"adding chrome to satisfy a gate"* being the gate driving the design. So:

**Fully automatable, and genuinely stronger than what a human does:**

- **The About mark is identical across four themes.** Render the 64×64 Canvas region under each
  theme and compare bitmap hashes. A human comparing four screenshots by eye would miss a
  one-channel drift; this cannot. **This is the highest-value item on the list** and it is the check
  that would have caught the `MagentaBrush` face at `:44` a cycle earlier.
- **Three History sessions produce three row bounds, and the rendered gutter exceeds the rendered
  inset.** `Sample.BoundsOf` already supports this. Stronger than `HistoryRowRhythmTests`, which
  asserts the constants — this would assert the pixels those constants produce, including at 125%
  scaling where the current gate is blind.
- **Both banners are simultaneously visible, carry distinct bounds, and each leads with `▲`.**
- **The consent sheet renders both capability tints, and they differ in every theme except
  magenta-heat** — where the design already says they collapse and `NamespaceLabel` carries it.

**Not automatable, and should not be faked:**

- *"Do the two banners read as two different warnings?"* That is a comprehension question about
  prose, not a pixel question. A gate asserting "the strings differ" would be green on two
  indistinguishable sentences and would license removing the human check that actually protects the
  user.
- *"Does the mark look right?"* Identical-across-themes is checkable; on-brand is not.

**The residue is small and it is real.** Roughly the first three of four owed checks become
mechanical; the judgement half of banner legibility stays human. That is a good trade, and claiming
it eliminates the capture walk would be this repo's most familiar failure.

## §5 Recommended order

1. **Spike `MeasureWindow` against `AboutWindow` only.** Parameterless constructor, and the
   highest-value assertion. Confirms or kills §3.1 and §3.2 in an afternoon. If the sentinel wrapper
   breaks resource lookup, that is the finding.
2. **About mark hash-identity gate across four themes.** Retire the eyes-on item from the capture
   walk and record that it was retired.
3. **History rows at 96 and 120 DPI.** Promotes `HistoryRowRhythmTests` from constants to pixels.
4. **Extract `MainViewModelTests`' builder to `internal`, then the banner-pair render.** Largest
   effort, and the one whose payoff is partly human anyway.
5. **Fix the stale comment in `ExpiredRowRedundancyTests` regardless of whether any of this is
   built.** It is one sentence and it is currently telling every reader the wrong thing.

Item 5 is worth doing even if 1–4 are declined.
