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

**Not fixed, on purpose.** The fix is a layout ruling, and the row's layout has been ruled on
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
