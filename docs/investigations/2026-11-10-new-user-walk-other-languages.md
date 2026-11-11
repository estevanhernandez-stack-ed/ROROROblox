# A new user in another language: what breaks, measured without a clean VM

2026-11-10. Prompted by the Store numbers in `docs/cycles/2026-11-10-growth-and-quality/board.md`:
active user-days per acquisition run about 3.5 in the US and about 1.1 to 1.2 in Brazil, Poland and
the UK. Two of those three have a translated interface. The question was whether something breaks
for a new user under those languages or regional settings.

A clean-VM walk as a Store user is still the real test and was not done. This is the part that can
be measured from the test suite.

## Result 1: regional number and date formats do not break anything that matters

**Method.** `FormatCultureProbe` (new, in the test project) sets the process's regional FORMAT
culture from `RORORO_TEST_FORMAT_CULTURE`. That is separate from the display language the
localization tests already switch. The whole unit suite was run under `pl-PL` and `pt-BR`.

**Outcome.** 3,479 of 3,486 pass under both. The same seven fail in each, and all seven are display
text with a decimal comma where the assertion expected a point:

- `MemoryChipFormatterTests` (three tests), `MemoryHeadroomTests`, `ShellLocalizationTests.MemoryFooter_ZeroOneMany_Localized`
- `WebhookPayloadTests.ForAlert_MetricBreach_RendersTheObservedValueWithoutFlatteningIt`
- `AlertFanoutTests.Payload_Recycled_CarriesTheReclaimedRam`

Nothing in the launch link, the settings files RoRoRo writes for Roblox, the account vault, or any
parsed input depends on regional settings. A static sweep agrees: the app sets only the display
language, never the format culture, and every parse in the launch path is a whole number.

**What it does not show.** The seven tests assert English formatting, so the suite cannot run green
under another format culture today. Showing "1,2 GB" to a Polish user is correct; whether a Discord
alert should follow the sender's regional format is a question nobody has ruled on.

## Result 2: a new user's first account row clips its Remove button in five of six translations

**Seen first in captures** made for the crash notice: in French and German, Remove was cut off at
the right edge of each row.

**Measured** with `LabelWrapMeasurementTests.OnAHealthyRow_AtTheDefaultWidth_RemoveStaysInsideTheRow_InEverySupportedLanguage`,
at the window's default size (900 by 600), one healthy account:

| Language | No saved game yet | A game saved |
|---|---|---|
| English | fits, nothing to spare | fits |
| Polish | fits | fits |
| Spanish | 4 DIP of Remove hidden | fits |
| Portuguese (Brazil) | 19 DIP hidden | fits |
| German | 48 DIP hidden | fits |
| Russian | 49 DIP hidden | fits |
| French | 54 DIP hidden | fits |

**Cause.** With no saved game, the row's game picker shows a hint ("Add in the Library") and
stretches to fit it. The translated hints are longer. The picker sits in a row of fixed-size
controls, so the extra width has nowhere to go and the last control, Remove, is cut by the row's
edge. "No saved game" is exactly the state a new user is in.

**Why nobody saw it.** The English fit test at this width passes. The seven-language Remove test
covers the three badge rows, not the plain one. And anyone who has used the app for a day has a
saved game.

**What it does not show.** That this explains the retention gap. Polish fits and retains poorly;
the UK is English. It is a first-impression defect in the translated interface, measured, and
nothing more is claimed for it.

**Fixed later the same evening; see the last section.** What follows is how it stood when it was found. The fix is a layout ruling, and the row's layout has been ruled on
before (the C2 rulings, 2026-11-02). The options:

1. **Let the hint give way.** The hint trims with an ellipsis before Remove clips, at any width.
   This also covers English below 900 px (the window's minimum is 860 and English has no slack).
   Needs the hint's width tied to the space actually left, which is a small piece of layout code.
2. **Shorten the five translated hints.** Cheapest. Fragile: it holds only at the default width and
   only until the next label gets longer.
3. **Cap the picker's width** at its normal size, so the hint is always trimmed when it is long.
   Simple, but it also trims the English hint, which is the one instruction on a new user's row.

Option 1 is the recommendation. Until it is made, the test is a ratchet: it records today's hidden
widths, fails if any grows, and fails if one shrinks without the table being lowered.

## Still not done

A clean-VM walk as a Store user in Portuguese and Polish: install, first run, add an account,
first launch. That is the test this note stands in for.

## The fix (2026-11-10, the owner's ruling: option 1)

The picker gives way. The row's actions are now an `ActionsRunPanel`, a horizontal StackPanel in
which one marked child, the picker's cell, is handed only the width that is left once everything
else in the row has what it needs. The row's grid is a `RowGrid`, a Grid that remembers the width it
was given, which is the one fact the actions column cannot otherwise know (a Grid measures an Auto
column with unlimited width). Both are in `src/ROROROblox.App/Controls/RowActionsLayout.cs`.

| Language | No saved game, default width, before | After |
|---|---|---|
| French | 54 DIP of Remove hidden | none |
| Russian | 49 | none |
| German | 48 | none |
| Portuguese (Brazil) | 19 | none |
| Spanish | 4 | none |
| English, Polish | none | none |

- The English hint still shows in full at the default width. It trims only when there is no room.
- At the minimum width (860) the hint costs Remove nothing in any language. Some languages still cut
  Remove there because their buttons are wider than the row: French hides 36 DIP at 860 with or
  without the hint. That is a separate, older limit and is not changed here.
- Narrowing a window that already fit squeezes the picker again.
- Every pinned row measurement in the suite is unchanged.

**The first attempt was thrown away, and why is worth keeping.** It set the hint's width from a
SizeChanged handler. It worked in a single window, and a probe showed layout coming fully to rest.
But it needed a second layout pass, and in the render tests every window an earlier test built is
still alive on the same thread and re-lays-out on each language switch, so WPF's layout manager
overran its time budget and the handler had not fired when the test measured. Each test passed alone
and failed after another one. Four attempts to make the tests wait for it all failed or timed out.
The rule now runs inside the measure pass, so there is no second pass to wait for: the same tests
went from about 100 seconds and flaky to 10 seconds and stable.

**Left as it was that evening (done since; see below):** the French hint now reads "Ajoutez via la…" on a new user's row, which loses the
word that matters. Shorter hints in the five languages would read better. That is a copy change and
no longer a layout problem.

## The hints were shortened (2026-11-10)

Each of the five hints that trimmed now fits the room its own language's row leaves at the default
width. The verb goes and the place stays; French has room for the word alone.

| Language | Before | Width | Room | After | Width |
|---|---|---|---|---|---|
| German | In der Bibliothek hinzufügen | 145.9 | 93 | In der Bibliothek | 84.6 |
| French | Ajoutez via la Bibliothèque | 138.1 | 79 | Bibliothèque | 67.0 |
| Russian | Добавьте в Библиотеку | 136.3 | 85 | В Библиотеке | 77.6 |
| Portuguese (Brazil) | Adicione na Biblioteca | 115.0 | 96 | Na Biblioteca | 69.4 |
| Spanish | Añade en la Biblioteca | 115.9 | 107 | En la Biblioteca | 79.5 |
| English | Add in the Library | 92.7 | fits | unchanged | |
| Polish | Dodaj w Bibliotece | 96.0 | fits | unchanged | |

Widths are DIP at the hint's own font, measured in the rendered row. "Room" is what the trimmed hint
was showing, so it is a floor on the real room, not the exact figure.

Measured and not used, in case a wording gets swapped: Spanish "Usa la Biblioteca" 86.3,
"Ve a la Biblioteca" 90.0, "Añade en Biblioteca" 103.0; Portuguese "Use a Biblioteca" 82.5,
"Abra a Biblioteca" 88.5; German "Bibliothek öffnen" 87.6, "Zur Bibliothek" 72.5; Russian
"Из Библиотеки" 85.9 (too tight), "См. Библиотеку" 89.3 (too wide); French "Voir Bibliothèque" 89.2,
"Via la Bibliothèque" 97.9, "Dans la Bibliothèque" 107.3 (all too wide: French has 80 DIP at most,
because the picker is already at its minimum width there).

The French hint is a bare noun, which is the weakest of the five. It matches the header button of
the same name on the same screen, and it beats "Ajoutez via la…".
