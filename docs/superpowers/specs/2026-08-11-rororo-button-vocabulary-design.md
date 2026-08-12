# RORORO — Technical Spec: one button vocabulary

> **ARCHIVED 2026-08-11 from `docs/spec.md`, at the close of the v1.20.0.0 cycle.** This is the
> cycle's canonical technical artifact. Banner-corrected rather than rewritten, per `CLAUDE.md`:
> what follows below is what was PROPOSED, and this block is what was actually BUILT where the two
> diverged. Rewriting the body top-to-bottom would destroy the /reflect-time framing.
>
> **§2 — the state design changed twice during item 1, both times because the builder looked at it.**
> The spec proposed hover → `RowBgBrush` and pressed → `DividerBrush` on the chrome. Shipped is a
> translucent white *sheen layer* with an outline, at 0.22 hover and 0.38 pressed, over an untouched
> fill. Repainting the chrome is a surface colour: fine under a quiet navy button, and it turned a
> cyan CTA dark navy, reported at C1 as the button dimming to nothing. The first replacement was a
> tint alone, which is weakest exactly where the fill is brightest ("now it's not dimming at all").
> The outline is what carries it, because a boundary appearing does not depend on the fill's
> luminance. Neither version was found by a test.
>
> **§2 — a second template exists that the spec did not anticipate.** `AppToggleButtonTemplate`,
> because a `ControlTemplate`'s `TargetType` must match the control and `ToggleButton` is not a
> `Button`. Found at C2 with the header widget still flashing Aero blue in the middle of the toolbar.
>
> **§2 — and a third.** `AppFilledButtonTemplate`, added at item 8 when the state gate first
> measured a state instead of asserting one was themed. The shared template's disabled trigger swaps
> the label to `MutedTextBrush`, which is correct on a navy fill and lands at 1.29:1–1.95:1 on the
> two bright-filled ranks items 3 and 4 introduced. Three windows opened with a CTA in that state.
> The filled template keeps the dark label and washes the fill toward white instead.
>
> **§5 — the gate shipped, and the item's acceptance was not met by its first version.** That
> version asserted the prohibitions (no literal, no opacity, never repaint the fill) and omitted the
> floor half of option (1) entirely, across four theories whose hand-written rank lists covered 6, 6,
> 8 and 7 of the then-8 ranks. The floor half is what found the defect above.
>
> **§6 — the scanner definition was corrected five times**, every correction prompted by using it
> rather than reviewing it: phantom `<Button.Style>` property elements, `BasedOn` property elements,
> local styles inheriting ranks, too-narrow lookahead, and a control type outside the definition.
> Each version reproduced, which is what made each look right. The branch-point number the cycle is
> sized against (63 across 22 files) is the corrected definition's, not the first one's.
>
> **§0.4 — the fence shipped**, and its abort clause did not fire: 112 declarations, 1 exemption.
> It was widened to `ToggleButton` and `RepeatButton` on the C2 lesson, and immediately found two
> more sites.
>
> **F-050 did not close and was never going to.** See its register row: the exempted pair left the
> gate's field of view when the magenta buttons migrated, and the defect stayed on five badges the
> gate cannot see.

---

## §0 — What the measurements settled

Three of the PRD's four open questions closed on evidence. One of them killed the cheap option, and
one reframed the defect.

### §0.1 The app runs the OS template, confirmed by eye

PRD Epic 0 asked whether the hover defect was real in the running app or an artefact of the render
harness. **It is real.** Este hovered a button on a live v1.19 build and photographed it: pale Aero
blue fill on a dark themed window, beside an unhovered sibling that is correctly grey.

Reading (b) is eliminated. **The v1.17 rendered contrast gate resolves the same template the app
does, so its results stand** — that was the more expensive of the two possibilities and it is not
what happened.

### §0.2 `Style.Triggers` cannot fix this, and the reason is one word

The PRD left open whether to override the inherited template or add `Style.Triggers` on top of it.
The second is cheaper, survives a WPF-UI package bump, and **cannot work.**

Every hover, pressed and disabled setter in the resolved template carries a `TargetName`:

```text
IsMouseOver == True   -> Background   (TargetName=border)
IsMouseOver == True   -> BorderBrush  (TargetName=border)
IsPressed   == True   -> Background   (TargetName=border)
IsPressed   == True   -> BorderBrush  (TargetName=border)
IsEnabled   == False  -> Background   (TargetName=border)
IsEnabled   == False  -> BorderBrush  (TargetName=border)
IsEnabled   == False  -> Foreground   (TargetName=<the button itself>)
```

`TargetName=border` means the setter targets a **named element inside the template**, not the
Button. A `Style.Triggers` entry on the Button's own `Background` sets a different object's
property, so it does not compete with these and does not win — it is not a precedence question, it
is a different element. Only `Foreground` on disabled is reachable, and one of seven is not a fix.

**Decision: the cycle owns the template.** `ControlStyles.xaml` gains a `ControlTemplate` whose
states bind themed brushes.

### §0.3 The app declares 116 plain `<Button>` and zero `<ui:Button>`

Every button in the app is `System.Windows.Controls.Button`. App.xaml merges WPF-UI's
`ControlsDictionary` before `ControlStyles.xaml` deliberately, with a comment saying it exists so
`BasedOn="{StaticResource {x:Type Button}}"` picks up the library's implicit Button style — and the
template that actually resolves is the OS one regardless.

**This spec does not migrate the app to `ui:Button`.** That would be a 116-site control-type swap
with its own regression surface, in a cycle whose point is to stop hand-copying. Owning one template
is smaller, is independent of what the library does or does not style, and is the only option that
survives §0.2 anyway.

### §0.4 One question deliberately left to the build

Whether the hand-copy fence can be written without a disqualifying allow-list (PRD Story 4.1) is not
decidable from here — it depends on how many of the 22 files hold a genuine exception, which is only
known once they are migrated. It is item 8, it is allowed to fail, and failing closes the story with
a recorded finding rather than shipping a gate that passes by exemption.

---

## §1 Stack

No new dependencies. WPF, WPF-UI 4.3.0, the existing `ThemeService` brush-replacement path, and the
v1.17 render harness which §0.1 has now vindicated.

| Piece | Role here |
| --- | --- |
| `Controls/ControlStyles.xaml` | Gains the `ControlTemplate` and the state triggers. Already holds all four ranks. |
| `ThemeService.ApplyTo` | Unchanged. Writes eleven brushes; the template consumes them by `DynamicResource`. |
| `Tests/Rendering/` | `ThemedRender` + `Sta` render offscreen and sample pixels. Extended to non-resting states. |
| `ContrastPairGateTests` | Extended per §5, or explicitly not — see the ruling there. |

**Versions:** app `1.19.0.0` → `1.20.0.0`, csproj and `Package.appxmanifest` in lockstep. No contract
change, so `ROROROblox.PluginContract` does not move.

## §2 The template

PRD ref: `prd.md > Story 1.1`.

One `ControlTemplate` in `ControlStyles.xaml`, set by a base style the four ranks derive from. The
ranks keep their current identities — they differ by edge and weight, which §0.2's evidence does not
disturb — and inherit states from the base.

```xml
<ControlTemplate x:Key="AppButtonTemplate" TargetType="Button">
    <Border x:Name="Chrome"
            Background="{TemplateBinding Background}"
            BorderBrush="{TemplateBinding BorderBrush}"
            BorderThickness="{TemplateBinding BorderThickness}"
            SnapsToDevicePixels="True">
        <ContentPresenter x:Name="Content"
                          Margin="{TemplateBinding Padding}"
                          HorizontalAlignment="Center" VerticalAlignment="Center"
                          RecognizesAccessKey="True" />
    </Border>
    <ControlTemplate.Triggers>
        <!-- Every state targets Chrome by name, which is the whole reason this template exists:
             the inherited one did the same thing to an element we could not reach. -->
        <Trigger Property="IsMouseOver" Value="True">
            <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource RowBgBrush}" />
        </Trigger>
        <Trigger Property="IsPressed" Value="True">
            <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource DividerBrush}" />
        </Trigger>
        <Trigger Property="IsEnabled" Value="False">
            <Setter TargetName="Chrome" Property="Opacity" Value="0.45" />
            <Setter Property="Foreground" Value="{DynamicResource MutedTextBrush}" />
        </Trigger>
    </ControlTemplate.Triggers>
</ControlTemplate>
```

**Why these slots, and why no new ones.** The eleven-slot palette is fixed and adding a twelfth is a
contract change this cycle has no reason to make (invariant 6 — the contract does not grow). Hover
takes `RowBgBrush`, one step up from `NavyBrush` in every built-in and already the app's "surface
above the field" colour. Pressed takes `DividerBrush`, a further step. Both are `DynamicResource`, so
they follow a theme switch by the same mechanism the resting colours already use, with no extra
plumbing.

**Disabled is opacity plus a muted label, not a colour.** A disabled state expressed as a hue fails
the same way F-032's status colours did — flatline has no hue to spend. Opacity reads under every
theme and is the one signal that cannot be confused with a different rank.

**Risk this design accepts, stated rather than discovered:** owning the template means a WPF-UI
update that improves its own Button template will not reach these buttons. That is the cost of
§0.2 and there is no version of this that avoids it while still fixing the defect.

## §3 The migration

PRD ref: `prd.md > Story 2.1`, `Story 2.2`.

Files in descending order of un-migrated sites, so stopping early banks the most debt:
`MainWindow.xaml` (30), `PluginsWindow.xaml` (6), `GamesWindow.xaml` (4),
`RobloxAlreadyRunningWindow.xaml` (4), then the tail of 18 files.

**A migration that changes how a button looks at rest is a regression**, not an improvement, unless
this spec names that site as a deliberate re-rank. There is exactly one:
`PluginsWindow`'s Remove takes `DestructiveButtonStyle` (§4).

**A site needing a look no rank provides opens a row and stops the item.** It does not get
hand-rolled and it does not silently grow the vocabulary mid-sweep.

## §4 F-046

PRD ref: `prd.md > Story 2.3`.

The row stayed open at the end of v1.18 because holding the F-068 line meant not touching
`PluginsWindow`'s Remove, which is a hand-rolled magenta fill and the row's headline evidence. It
takes `DestructiveButtonStyle` — the rank v1.18 defined for exactly this and assigned **by name**,
never by sweep. The by-name list is unchanged: Remove on the account row, Clear history, Stop all
confirm, plus this one. A fifth site needing a judgement call is the signal to stop, as it was then.

## §5 What gets tested

PRD ref: `prd.md > Story 1.2`, `Story 3.1`.

**The gate extends to non-resting states, and this is the ruling the PRD asked for.**
`ContrastPairGateTests` measures foreground against its own fill for resting pairs. A hovered button
is the same measurement against a different fill, so it is an extension rather than a new gate — and
leaving it out would mean the cycle's own new colours are the only ones in the app nobody checks.

The render harness cannot force `IsMouseOver`: it is set by the input system, and
`VisualStateManager.GoToState` returns **False** on this template because it uses property triggers
and has no visual state groups. **This is recorded because a probe during `/prd` did not know it and
produced a confident wrong answer for twenty minutes.** Two options for item 6, in preference order:

1. **Measure the template's trigger setters directly** — resolve the `ControlTemplate`, read each
   trigger's setters, and assert both that no value is a hardcoded literal and that the resolved
   brush pair clears its floor. This is what actually answered §0.2 and it needs no input
   simulation.
2. **Reflection onto `IsMouseOverPropertyKey`** to force the property and then render. Higher
   fidelity, more fragile, and only worth it if (1) proves insufficient.

Start with (1). **If neither can be made to fail against a deliberately broken template, the gate is
not shipped and item 6 closes with that finding** — the same rule Story 4.1 carries.

## §6 The scanner definition

PRD ref: `prd.md > Story 3.1`. The register says 55 un-migrated sites; a scan during `/scope` counted
72; the file count agreed exactly at 22. The disagreement is about what a *site* is.

**This spec's definition, which item 2 writes into a committed script:**

> An **un-migrated button site** is an occurrence of `<Button` or `<ui:Button` in a `.xaml` file
> under `src/ROROROblox.App/`, excluding `obj/` and `bin/`, whose **opening tag** does not contain a
> `Style="{StaticResource …ButtonStyle}"` or `Style="{DynamicResource …ButtonStyle}"` reference.
> A button inside a `ControlTemplate` counts, because it is still a declaration someone maintains.

Run at the branch point and again at the end, both numbers recorded with direction. The register row
records the definition beside the count so the next re-measure compares like with like. **Neither 55
nor 72 is adopted as "the" number** — the script's first run at the branch point is the baseline, and
the older figures are noted as measured under unknown definitions.

## §7 File structure

```text
src/ROROROblox.App/
├── Controls/ControlStyles.xaml          # M template + state triggers (§2); ranks BasedOn it
├── MainWindow.xaml                      # M 30 sites (§3)
├── Plugins/PluginsWindow.xaml           # M 6 sites + Remove -> Destructive (§3, §4)
├── Games/GamesWindow.xaml               # M 4 sites
├── Modals/*.xaml                        # M the tail
└── ROROROblox.App.csproj                # M 1.19.0.0 -> 1.20.0.0 (+ Package.appxmanifest)

src/ROROROblox.Tests/
├── Rendering/ButtonStateGateTests.cs    # + §5 option (1): read the template's own triggers
├── ContrastPairGateTests.cs             # M extended to hovered/pressed pairs
└── ButtonVocabularyFenceTests.cs        # + item 8, allowed to not ship

scripts/count-button-sites.ps1           # + §6, committed so the number is reproducible
docs/spec.md                             # this file -- ARCHIVE before the next round
```

## §8 Key technical decisions

1. **Own the template rather than layer `Style.Triggers`.** *Forced by measurement:* the inherited
   template's state setters carry `TargetName=border` and are unreachable from a Style trigger.
   *Tradeoff:* a WPF-UI improvement to its Button template will not reach us. Accepted; there is no
   alternative that fixes the defect.
2. **Do not migrate to `ui:Button`.** *Tradeoff:* stays on a control type the library may style less
   well. Accepted: a 116-site control swap has its own regression surface, and §0.2 means we would
   still need our own template.
3. **Hover and pressed reuse existing palette slots.** *Tradeoff:* less designer control than
   dedicated hover slots. Accepted: a twelfth slot is a contract change, and every user theme already
   on disk would need updating to supply it.
4. **Disabled is opacity plus a muted label, not a colour.** *Tradeoff:* less distinct than a
   dedicated grey. Accepted: flatline has no hue to spend, and this is the defect class the last
   three cycles have each fixed once.
5. **The state gate reads the template's triggers rather than simulating input.** *Tradeoff:*
   measures declaration, not rendering. Accepted for now because input simulation is not available on
   this template at all, and it is the technique that produced §0.2.

## §9 Open issues

- **The fence may not be shippable.** Item 8 is allowed to close with a finding instead of a gate.
- **F-068's count will not match its history.** §6 adopts a definition rather than a prior number,
  so the closing figure is comparable to the branch point and to nothing before it. Said plainly in
  the row.
- **Borders are untouched** — 60 of 76 still hand-themed, a real debt and a different one.
- **Owning the template pins us to its structure.** If WPF-UI 5 changes what a Button is expected to
  contain, this is the file that finds out.
