# C1 — the routing collapse, checked live

2026-10-06, 17:55–18:13, on Nebuchadnezzar, against the branch build (v1.32.1 assembly, logged as
`v1.32.1`) at `41405b2` — items 1, 2 and 3 of the v1.33 checklist. One account launched
(`ItsjustesteAgain`), one Roblox client, ~1,020 MB private. Crossings were forced by hand-editing
`memoryCapMb`, which is a supported way in and the same method Sunday's memory-toggle smoke used.

## What passed

### Desktop unticked means silence

Memory warnings route to Discord ("Mine") alone on this install. A forced crossing at **17:58:31**
dispatched to `"Mine"` and produced **no `Local` line at all**.

Before this cycle that same crossing raised a Windows balloon regardless, because
`WireMemoryWarningTray` consulted nothing — measured on 2026-10-05, where the dispatcher logged
"routed nowhere" under the cooldown while a balloon went out anyway.

### Idle reaches the desktop through the dispatcher

```
18:07:50  Alert → "Local": ItsjustesteAgain went idle (1 account(s)).
```

This single line carries most of item 3: the new `AccountIdle` kind, raised from the view model
rather than a private presenter, routed by `AlertDispatcher`, landing on the desktop **because
`IdleDestinations` defaults to `[Local]`**, with item 1's wording. `IdleAlertPresenter` no longer
exists, so nothing else could have produced it.

Idle was chosen deliberately as the Desktop-path proof: it is the kind item 3 rewired, and it
defaults to `Local`, so it exercises the route without touching the routing grid. It also cost
nothing but patience — no synthesized input, so the idle clock stayed honest.

### The quiet period actually suppresses

With the period at **5 minutes** and crossings forced roughly every 2 minutes:

| Crossing | Dispatched? |
|---|---|
| 18:06:01 | yes |
| 18:08:31 | **no — inside the window** |
| 18:10:01 | **no — inside the window** |
| 18:12:01 | yes |

Two crossings happened and said nothing. That is the behaviour being bought, and it is visible as a
crossing line with no dispatch beside it.

## What did not pass, and was not claimed

**The first cadence run was worthless and is recorded rather than deleted.** The re-arm cycle takes
~90 seconds, so with a 1-minute quiet period every crossing landed *outside* the window and fired
legitimately: four crossings, four dispatches, nothing proven. A fixture shaped so the invariant
cannot fail. The second run fixed it by making the period (5 min) longer than the re-arm cycle, which
is the only way suppression is observable at all.

**The "every time" control is weaker than designed.** Crossing C fired at 18:12:01 with the period at
`0` — but it also landed 6 minutes after the previous dispatch, so a 5-minute period would have
allowed it too. It therefore does not isolate `0`. The suppression evidence above stands on its own;
this control does not add to it. Worth redoing properly in item 11's smoke with a crossing forced
inside a long window.

## What C1 could not check

- **"Exactly one balloon, not two."** Not observable in a log. It is covered structurally instead, by
  `OnePathToTheScreenFenceTests`, which fails with a file:line offender if anything calls
  `ShowMemoryWarning` on the crossing path or if the presenter returns.
- **Click-to-focus.** Knowingly inert between items 3 and 4 — see the spec's click-to-focus section.
  Item 4 restores it generalised. Deliberately not tested here; it would fail by design.
- **The balloon's appearance and sound.** Still the stock Windows balloon until item 4. Judging the
  look at C1 would have been premature, which is why C1 asks for no visual judgement.

## Noticed, unrelated to this cycle

**The app started tray-resident with no window, and relaunching the exe did not surface one.**
`CLAUDE.md` says the single-instance guard surfaces an existing window; it did not. UI Automation
found the process's window with an empty name and no toolbar buttons, so the Settings page could not
be driven, which is why Desktop could not be ticked by hand for the second half of the destination
test. Either the doc is stale or this is a real defect, and from a user's side it reads as "I clicked
it and nothing happened". **Worth its own look, outside this cycle.**

## State afterwards

Settings restored and diffed against the pre-run backup: identical apart from `alertCadenceMinutes`,
which is now explicitly `5` where it was absent — the same value the absent key defaults to. The
launched account and its client were left running.

## Correction, 19:25 — the account was the wrong one, and Este caught it

`ItsjustesteAgain` cannot join a game directly; it has to follow someone in. Launching it straight at
a place left it sitting at Roblox's verification screen, never reaching `InGame`, until it exited on
its own. So the client those first readings came from was stranded, not playing: **1,022 MB** against
the **2,354 MB** the same account uses once it is actually in a game.

Relaunched with `follow_main`, it was in the game in **18 seconds**, and a crossing forced against the
real figure behaved identically — crossed at 2,354 MB against a 1,500 MB cap, dispatched to `"Mine"`
alone, no `Local` line.

**What this changes in the findings above: nothing, and that is worth stating precisely rather than
either dismissing the point or over-conceding it.** Routing, suppression and the idle route are all
independent of whether a client is in a game; the memory figures were real private bytes of a live
process either way. What it does change is representativeness — a stranded client is not the case
this feature exists for, and **item 11's full smoke must use an in-game client**, which is now
running.

One honest wrinkle it exposes: the idle alert that proved the Desktop path fired for an account that
was **offline**, not playing. The route it proved is the same route, so the proof stands. But an idle
alert about an account that never got into a game is arguably noise, and it would have behaved the
same before this cycle — so it is not a regression, just a case nobody has looked at. Worth a question
in item 11: should an account that is not in a game be eligible for an idle alert at all?

**Also open, and not called a regression without evidence:** the direct launch produced a plain
`target=Place` dispatch with no join-via-friend decision in the log at all. Either the account is not
flagged in RoRoRo — in which case the app did exactly what it was told — or the plugin launch path
reaches `RobloxLauncher` without passing `FlaggedLaunchRule`, which would be a v1.32 regression.
Deciding needs the account's flag state, which lives in the encrypted vault and is only visible in the
row menu. **Check before the cycle ends.**

### Resolved, 19:37 — not a regression

Este checked the row menu: **"Join via friend" is not ticked** on `ItsjustesteAgain`. So the direct
launch logging a plain `target=Place` with no join-via-friend decision was the app doing exactly what
it was told, and `FlaggedLaunchRule` is not being bypassed by the plugin path. v1.32's feature is
intact.

The useful half is the inverse: that flag exists for precisely this account's situation — one that
cannot get into a game on its own — and it is not set. Ticking it would make every launch path follow
the main automatically instead of stranding the client at the verification screen. A one-click
configuration change, not a code change, and Este's call.

For whoever tests next: **check the flag or use `follow_main` when picking a test account.** Choosing
by "which one is free" is how this run spent twenty minutes on a client that was never going to play.
