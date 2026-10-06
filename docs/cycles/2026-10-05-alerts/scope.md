<!-- Cart cycle #5 on this repo. Written at docs/cycles/2026-10-05-alerts/ rather than docs/scope.md
     because that path holds the retired v1.18/v1.21 snapshot CLAUDE.md lists as history. Downstream
     Cart commands take this path explicitly. -->

# RoRoRo v1.33 — the alerts cycle

## Idea

Alerts become something you set, in one place, that make one noise you chose. Today they are three
cards, one hardcoded five-minute constant, and a Windows balloon nothing in the app can govern.

## Who It's For

Este first, then the Pet Sim 99 clan, on the same bar as always: a common Windows user who should
never have to read a log to understand why their computer just made a noise.

The trigger was his, 2026-10-05: *"We need more options for how often alerts go off, and then we
also need to consolidate the alert section. For memory alerts, I'm still getting memory alerts even
though I have memory watching off. And if there's any way we don't have to use the terrible Windows
alert sound, I would appreciate it."* Three complaints and a bug, in one breath.

## The measured starting point

Everything below is verified against the tree and, where marked, against the running app on
2026-10-05. Full evidence: [`docs/investigations/2026-10-05-alerts-section-audit.md`](../../investigations/2026-10-05-alerts-section-audit.md)
and [`docs/smoke-2026-10-05-memory-watch-live-toggle.md`](../../smoke-2026-10-05-memory-watch-live-toggle.md).

- **One throttle exists in the whole app.** `AlertRouter.Cooldown`, five minutes, `static readonly`,
  no setting, keyed per account and kind. Its strict `>` comparison is why a repeat at exactly 5:00
  vanishes and the one at 6:00 fires — measured in Este's 10/4 log, and it reads as random.
- **The tray balloon answers to nothing** (measured live). `WireMemoryWarningTray` raises a Windows
  balloon, with the stock sound, on every crossing — no destination check, no mute check, no
  cooldown. At 15:21:57 the dispatcher logged "routed nowhere" under the cooldown while the balloon
  fired anyway. On Este's install memory warnings route to Discord alone, so **every desktop balloon
  he has ever seen for memory came from a path with no switch and no throttle.**
- **The section is three cards and two stores.** "Idle accounts", "Alerts" (a 460-line card), and
  "Memory" under one nav item. The toggle that turns memory watching on sits ~460 lines of markup
  from the row that says where its alerts go. The metric opt-in writes `settings.json` while the four
  destination boxes beside it write encrypted `discord.dat`. Idle alerts have no routing row at all.
- **Idle alerts bypass the router entirely** — a direct tray toast, no cooldown, no destinations.
- **Per-rule timing is hand-edited JSON.** `windowMinutes`, `threshold`, `alertWhenBelow`,
  `tellMeWhenItRecovers` have no UI.
- **F-102 is alive in the memory checkbox** (measured live). It is wired to `Click`, and
  `TogglePattern.Toggle()` — what every screen reader, voice access and automation path uses —
  raises `Checked`/`Unchecked`, never `Click`. UIA flipped the box to Off, reported Off, and saved
  nothing. A real mouse click saved fine.
- **Fan-out multiplies everything.** One event can be four notifications.

## Goals

1. **A cadence you can see and set.** The five-minute constant becomes a visible global minimum with
   a per-kind override, and every alert path obeys it — including the ones that bypass the router
   today.
2. **One alerts section.** Every alert concern reachable without hunting: what fires, how often,
   where it goes, and the memory numbers that drive it.
3. **Our own notification, with our own sound.** A themed balloon via the supported
   `ShowCustomBalloon` API; silent, a bundled chime, or the system sound, chosen by the user. No
   stock Windows notification sound unless asked for.
4. **The desktop stops being a special case.** It becomes a real destination that obeys the grid and
   the cadence like every other one.
5. **A toggle tells the truth to everything that asks it**, including a screen reader.

## What "Done" Looks Like

- Settings → Alerts shows one section. For each alert kind: whether it fires, how often at most,
  where it goes. The memory numbers sit with the memory row they drive.
- Setting the global cadence to 30 minutes means no alert kind speaks more than twice an hour,
  measured, including idle and including the desktop balloon.
- Unticking Desktop for a kind means no popup for that kind. Full stop.
- The alert sound is whatever the user picked, and the default is not the Windows notification sound.
- Flipping any alerts toggle through a screen reader saves, and a fence test fails if a new
  `Click`-wired toggle appears.
- A clean-VM smoke run reproduces each of the above by hand.

## What's Explicitly Cut

- **No new alert kinds.** Nothing new to be notified about; this cycle is about governing what exists.
- **No metric-rule editor UI.** Surfacing `metric-rules.json` as a form is its own cycle — it is
  plugin-authored data with an owner field, and a half-editor is worse than the file.
- **No real Windows toast / Action Center work.** It needs an AUMID shortcut plus a COM activator for
  the direct-download build, which is the two-build-shapes class of bug that bit v1.23. Revisit when
  there is a reason beyond sound, which the custom balloon already solves.
- **No quiet hours / do-not-disturb.** Tempting next to cadence, but it is a second feature with its
  own edge cases, and cadence is what was actually asked for.
- **No change to the five-minute default.** The number becomes visible and settable; it does not move
  underneath anyone who liked it.
- **No phone or Discord redesign.** Those destinations work. This is about what governs them.

## Open forks, with my recommendation

These are design calls the artifacts cannot answer. Each carries the assumption I will build on
unless Este says otherwise.

1. **Does the desktop balloon become fully routable?** **Answered yes by Este, 2026-10-05 23:02** — it obeys the grid and the
   cadence, so unticking Desktop means silence. The risk: memory pressure is the one alert that says
   "your machine is about to choke", and a user who unticks Desktop and never looks at Discord has
   silenced it. Mitigation: the tray **badge** stays unconditional and free — it is a colour change,
   not a popup or a sound — so the state is always visible without ever interrupting.
2. **Do idle alerts join the grid?** *Assumed yes.* They are the only kind with a section of their
   own and the only kind the alert system does not handle. Joining means they gain destinations and
   a cadence, and lose nothing.
3. **Cadence granularity.** *Assumed* a global "at most once every" plus a per-kind override, per
   Este's pick on 2026-10-05. Values: 1, 5, 15, 30, 60 minutes, and "every time". Per-kind defaults
   to "follow the global".
4. **The default sound.** *Assumed* a short bundled chime, not silence — an alert that makes no
   sound is a log entry. Silence stays one click away.

## Rider: tell a plugin why it was started

Added 2026-10-05 at Este's request, relayed through the Ur Score session. Not an alerts change — it
rides this cycle because it is additive, roughly ten lines plus tests, and Ur Score's tray mode is
waiting on it.

**The ask:** Ur Score should start in the tray and keep score when RoRoRo opens, without opening its
window. Today `DefaultPluginProcessStarter.Start(pluginId, exePath)` passes no arguments and no
environment, with `UseShellExecute = false`, and all four launch paths — autostart, the Launch click,
post-install, and the update relaunch at `PluginsViewModel.cs:293` — funnel through one
`PluginProcessSupervisor.Start(plugin)`. So a plugin cannot tell them apart.

**The decision: an environment variable, `RORORO_LAUNCH_REASON`, not a command-line flag.** Values:
`autostart`, `manual`, `install`, `update`.

Why not the proposed `--autostart` argument: an unknown argv entry is not inert. A WPF plugin that
treats arguments as file paths, or anything that validates argv, can break on a flag it never asked
for — and these are third-party executables we do not control. An environment variable is invisible
to every plugin that is not looking for it, which makes it the only version of this that is safe to
roll out to an existing plugin population without asking each one first. `UseShellExecute = false` is
already set, which is precisely the condition under which `ProcessStartInfo.Environment` works.

Why not the manifest field plus a "Start in tray" toggle on the Plugins list: the choice is the
plugin's, not the host's. Ur Score already has settings; "when RoRoRo starts me, do X" belongs there,
beside the other things Ur Score does on its own behalf. A host-side toggle would also need a
manifest field, a contract bump, docs, and a row of UI to express something the host does not act on.

Why a reason rather than a boolean: `update` is a real fourth case the ask did not name — on an
update relaunch a plugin should return to the mode it was in, not force a window — and a vocabulary
costs the same as a flag today while a flag costs a second flag later.

Carried by **v1.33**. A plugin that does not read the variable is unaffected, and Ur Score treats its
absence as "open the window", so it works against today's RoRoRo too. `contractVersion` does not move;
nothing in the gRPC surface changes.

## Rider: the hand on the About page, and the egg that already exists

Added 2026-10-06 at Este's request. Small, visual, unrelated to alerts; recorded here rather than
folded in silently.

**Correction first, because it changes the whole rider.** The first two passes at this specced an
Easter egg from scratch — tap count, randomisation, testability — without reading the tree. **The egg
already ships.** `About/AboutPage.xaml.cs:21-26` reveals "Koii 4 eva" after clicking the VERSION
NUMBER (not the nav item), and `_eggTarget = Random.Shared.Next(6, 8)` already randomises six-or-seven
per shell lifetime. Este's "won't it always fire on six?" was answered in the code before either of us
looked, and the "randomise it" insight written here as a proposal was describing shipped behaviour.
The lesson is the cheap one: read the tree before designing, especially when the owner says "right
now it has X" — that was him telling me it exists.

### What is actually already there

- Reveal text `AboutPage.xaml:103-112`, bound to `AboutPage_Koii4Eva`, localized in all six cultures.
- Magenta (`MagentaBrush`) at `HeadingFontSize`, SemiBold.
- **It already glows:** a `DropShadowEffect`, colour `#F22F89`, blur 14, no offset, opacity 0.85.
- Hidden until fired (`Visibility="Collapsed"`, `Opacity="0"`).
- Target randomised per shell, so the egg survives navigating away and back.

### What this rider actually adds

**Both marks are wordmarks, not styled text** (Este, 2026-10-06). That one decision removes most of
the work this rider originally carried.

1. **"Koii 4 eva" becomes a wordmark in Este's hand.** His own words for his clan, drawn rather than
   set — the one use of the hand the brand sanctions.
2. **The 626Labs wordmark WITH "LLC" at the bottom of the About page.** Not the signature: the
   brand kit reserves the full lockup for "where the legal entity is meant", and an app's attribution
   line is exactly that. "626Labs" in EsteFont Pro Bold, "LLC" in Space Grotesk 600 at about a third
   of the hand's size, baseline-aligned, 40px tall minimum. Ships as
   `626labs-wordmark-{dark,light,duo}.svg`.
3. **Gradient over the glow it already has.** The reveal already carries a magenta `DropShadowEffect`;
   the ask is to make it ridiculous, and this is the one screen in the app where that is correct.

**No font embedding.** As assets, neither mark needs EsteFont Pro inside the binary, which deletes
three constraints at once: no TTF in the package, no subsetting to stop the handwriting being
extracted from a public MSIX, and no new `FontFamily` token fighting `TypeLadderFenceTests`.

**Follow the avatar pattern, which this repo already has.** `StreamerMode/Avatars/sources/*.svg` are
kept as sources with the consumed `*.png` committed beside them; WPF has no SVG renderer, so this is
the established answer here rather than a new one. Export at the scales the splash screen uses.

**The gradient wants an `OpacityMask`, not a baked asset.** Export the wordmark as a transparent PNG,
use it to mask a `LinearGradientBrush`, and keep the existing drop shadow for the glow. The colours
then come from theme brushes instead of being frozen into the image, so the egg follows the theme and
one asset serves all four.

**Decided: one mark, not seven** (Este, 2026-10-06). `AboutPage_Koii4Eva` carries a resx entry in all
six cultures plus the neutral, and **all seven values are byte-identical: `Koii 4 eva`** — the
catalog pipeline's `PRODUCT_NOUNS` guard did its job and no translator touched a clan's name. So the
localization was seven rows that only ever said one thing, and retiring them costs nothing: a drawn
mark says it once, in Este's hand, for everyone. Retire the entries deliberately in the same commit
that lands the asset rather than leaving them orphaned, and the guard stays as it is — it was right.

### Constraints the build still has to honour

- **The font ships as TTF** (`EsteFontPro-Regular.ttf` 34 KB, `-Bold.ttf` 31 KB, in
  `LabShare/626labs-assets/fonts` and in the design skill). WPF cannot use the woff2.
- **No `FontFamily` literal in markup** — `TypeLadderFenceTests` forbids them, so the hand enters the
  type ladder as a token and the fence's counts move in the same commit.
- **Never below 24px, upright, never italic or letter-spaced**; the slant and spacing are drawn into
  the glyphs. The hand's x-height is 0.45em, so it wants ~1.2x the surrounding size.
- **Subset before shipping the hand in a public MSIX.** Less pressing if the wordmark comes in as SVG
  and the only live text is a dozen glyphs, but a full handwriting font inside a public package is
  extractable and it is 626Labs-owned.
- **Nothing tests the egg today** (`grep` for `EasterEgg|eggTarget|Koii4Eva` across the test project
  finds only build artefacts). `Random.Shared` at the field initialiser cannot be pinned, so neither
  branch is assertable. Making the source injectable is a small change and the only way the egg does
  not silently break.

## Loose Implementation Notes

Non-binding; `/spec` decides.

- The cadence almost certainly belongs where `AlertRouter.Cooldown` is, promoted from a constant to a
  value the router is given, with `IAppSettings` carrying the global and the per-kind map. The router
  already keys per account and kind, so the shape is there.
- The two bypass paths (`IdleAlertPresenter`, `WireMemoryWarningTray`) have to route through the
  dispatcher for any of this to be true. That is the structural heart of the cycle, and it is also
  what makes the sound work meaningful — one notification path, one place to govern it.
- `ShowCustomBalloon` is public and supported on the shipped `Hardcodet.NotifyIcon.Wpf 2.0.1`;
  `BalloonFlags.NoSound` is only reachable through a non-public overload, so the custom balloon is
  the honest route rather than reflection.
- Fences that move in the same commit: `SettingsCommitOnEnterFenceTests` (8, equality),
  `AccessibleNamingFenceTests` (unnamed 1 and scanned floor 120, both equality),
  `AlertsStatusLinePositionFenceTests` (markup order), `StreamerSecretFieldFenceTests` (5),
  `PreferencesCopyTests` (copy rules), `SettingsReachabilityTests` (new keys need a reachable
  control). A new fence for `Click`-wired toggles is part of the deliverable.
- The clean-VM smoke matters more than usual here: three of the four complaints are about what the
  machine does to a person, and none of it is visible from a green test suite.
