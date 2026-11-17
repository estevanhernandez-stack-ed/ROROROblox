# Run Report: update-button

Branch `fix/stability-part-two`, two items, attended, 2026-11-17. Both done. Nothing pushed. Suite at the end: 3,986 unit and 27 harness pass, 0 build errors.

## What exists now

The main window's header shows an accent button at the left of the button group, before Library, only while an update is waiting.

| Install | When the button shows | Label | One click |
|---|---|---|---|
| Microsoft Store | Windows' Store service says an update is waiting. Asked once after startup and every six hours | Update available | Opens RoRoRo's page in the Microsoft Store |
| Direct download | The daily check has downloaded and staged an update | Quit to update | Quits RoRoRo through the tray's quit path |
| Dev build | Never | | |

With no update, or when the check fails for any reason, there is no button and nothing else changes. A failed re-check keeps what was last seen, so a Store update already found does not vanish because one later check hit a bad network.

Captures of the real header, drawn off screen by the rendering tests, are in `captures/`: both states in the brand and midnight themes at 96 and 120 DPI, the header with no update for comparison, and the two longest languages (German and Russian) in both states.

## What was and was not exercised

- **Exercised by tests:** the service's three states; a probe that throws, in four exception types, reading as none and logging once; the six-hour re-check through an injected clock, and its stop at shutdown; no Store call at all on an unpackaged build; the view model's visibility, label, tooltip and command per state; the Store click opening the Store link through the shell opener and the staged click asking for the quit; every new key present in all six languages; the header still clearing the centre column in seven languages (worst case Spanish, 402 DIP of room against a 340 maximum); the button, naming, string and brand fences, none of them moved.
- **Exercised by hand:** the Store link itself. `ms-windows-store://pdp/?productid=9NMJCS390KWB` launched the Microsoft Store app on the owner's machine. Which page it landed on was not visible to the session.
- **Not exercised, and cannot be before release:** the Store call. It needs a Store-installed build, and the sideload package cannot be installed beside the Store one. The assumptions that ride on it: the call works from an unelevated WPF app with package identity, needs no window handle, and returns one or more entries when an update is waiting. If any is wrong, no button shows. The first Store release after this one is the live test.
- **Not exercised:** the direct-download button. It needs a Velopack-installed build with an update staged. Tests drive it through the notification the update checker sends.

## What review changed

- **The direct-download tooltip claimed when the update installs.** It said the new version "installs when RoRoRo closes". Whether Velopack applies a staged update at exit or at the next start is on the findings board as unmeasured. The tooltip now says what is true either way: quit RoRoRo and open it again to finish updating. Two strings, reworded in English and six languages and regenerated.

## Things the owner should know

- **Not shown in compact mode.** The whole header is collapsed there. The button returns when the window is expanded.
- **A direct-download build left running learns of an update only at its next start.** Its check runs once after startup, at most once a day. Re-running the staging call mid-session was left alone: its effect on a running session is the same unmeasured question as the tooltip's.
- **The corrupt-vault dialog's Quit changed path.** The view model has one quit seam, and it is now the tray's quit path (`RequestShutdown`), which also marks closes as expected.
- **Five new strings in six languages**, on top of the twelve from the stability run: seventeen for the translation verifier before any tag.
- **`docs/PRIVACY.md` has a new network row** for the Store check. The reviewer letter for the release must say the same.

## Open, waiting on the owner

- ~~A look at the captures against the placement he picked.~~ Done 2026-11-17 13:36: "captures look good".
- ~~Push the branch and open a PR; the translation verifier; the version.~~ Done the same afternoon: PR #295 merged (`db8f3ec`), all 102 translation pairs approved, version 1.40.0.0. Still open: the Store submission and the tag.
