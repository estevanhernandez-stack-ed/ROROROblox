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

## Rider: the hand on the About page, and the tap

Added 2026-10-06 at Este's request, after reading the brand kit in `LabShare/626labs-assets/fonts`.
Small, visual, and unrelated to alerts; recorded here rather than folded in silently.

**Two pieces, and only the first is always on.**

1. **The 626Labs wordmark in EsteFont Pro, on the About page.** The hand's one rule is "Este's own
   words, in his hand" — never UI labels, buttons or body copy — and the wordmark is that rule's own
   stated exception, because "626Labs" in the hand *is* the company's signature. It belongs on the
   About box of a 626 Labs product.
2. **An Easter egg behind the About nav item.** Tap it **six or seven times** and a short line in
   Este's voice appears, in his hand, signed.

**The tap count IS the joke, and it is not arbitrary.** "6-7" is the meme the Pet Sim 99 audience
chants constantly — kids say it at each other all day — and the gag is that nobody can pin down
whether it is six or seven.

**The threshold is chosen at random, six or seven, each time the counter starts.** Not fixed, and not
"fires on both" — Este caught that one: if it fires on six and on seven, six always wins and seven
never happens, so "6 or 7" would be a label on a spec that always means six. Randomising is the only
version where the ambiguity is real. Sometimes it pops on the sixth tap, sometimes it asks for one
more, and two kids who compare notes disagree honestly. That disagreement is the meme's social life
and the reason the egg is worth building at all.

Pick the threshold when the counter starts rather than per tap, so a single attempt stays coherent —
the sixth tap doing nothing and the seventh doing something is correct; the sixth tap doing nothing
*sometimes mid-attempt* is a bug. The source of randomness is injected, not `new Random()` at the call
site, so a test can pin it to six and to seven and assert both paths.

A correction worth keeping, because it shows the failure mode: the first pass at this read the gesture
as Android's developer-options Easter egg (seven taps on the build number) and proposed six "so the
number is 626, not Android's". That is a tidy brand joke for an adult who has flashed a phone, and it
is deaf to the actual audience. This is a tool for a Roblox clan. **The audience's joke beats the
brand's joke**, and anyone building this should resist making the number mean something.

**Why an egg and not a theme.** The ask started as an Easter egg *theme*. A theme is the one shape
that cannot work: it would render UI labels in the hand, which is exactly what the single rule
forbids, and mechanically this app's themes are ten colours in a JSON file with no font axis. An egg
that reveals Este's words is the use the brand actually sanctions, and the hand appearing *because
someone went looking* is a better joke than the hand as a skin.

**Constraints the build has to honour.**

- **The font ships as TTF** (`EsteFontPro-Regular.ttf` 34 KB, `-Bold.ttf` 31 KB). WPF cannot use the
  woff2. Embed as a `Resource` and reference by pack URI.
- **No `FontFamily` literal in markup.** `TypeLadderFenceTests` forbids them, so the hand enters the
  type ladder as a token beside the existing display/body/mono families, and the fence's counts move
  in the same commit.
- **Never below 24px, upright, never italic or letter-spaced.** The slant and spacing are drawn into
  the glyphs. The hand's x-height is 0.45em, so it needs roughly 1.2x the size the surrounding type
  would use — which means a ladder size token, against a `DistinctRawSizeCeiling` of 3.
- **Subset the font to the glyphs actually used.** Shipping the full hand inside a public MSIX means
  anyone can extract Este's handwriting, which is 626Labs-owned and marked for 626 Labs work only.
  The wordmark plus one fixed line needs a few dozen glyphs; subsetting takes it to a few KB and
  makes extraction worthless. This is the only part of the rider with a real downside if skipped.
- **The line does not localise.** A handwritten paragraph would be English for all six languages,
  against this app's own posture. A signature and one short line are fine; a paragraph is not, and
  the egg's copy should stay short enough that this stays true.

**Blocked on one thing only: the line itself.** It has to be Este's own words — that is the whole
rule — so it cannot be drafted here. One or two sentences, plus how he wants it signed.

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
