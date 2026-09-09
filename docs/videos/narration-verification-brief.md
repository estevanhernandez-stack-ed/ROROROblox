# Narration verification run — brief

The launch video's voiceover translations, reviewed as ADVERTISING copy.
Paste-ready framing for the translation-verification tool (same rite the
Store listings passed at 36/36); dataset is this folder on branch
`feat/localized-taglines`.

## Dataset

- Source: `launch-video-narration-en.md` (7 VO lines + overlays + caption)
- Under review: `launch-video-narration-{fr,de,ru,pt-BR,pl,es}.md`

## Framing — this is ad copy, not listing copy

The listing rubric applies (claim fidelity, untranslated tokens,
accuracy, naturalness, consistency), with these adjustments:

1. **Hard limits differ:** the budget is the ~110% syllable ceiling
   named in the EN kit, not charCap. Two accepted overruns are flagged
   at point of use (es and pt-BR `trust`, 115%/111%) — owner accepted
   2026-09-08, absorbed by slide dwell at render. Do not send those back
   for length.
2. **Spoken register:** these lines are read aloud by a voice, not
   typeset. Flag anything that is natural on a page but awkward in the
   mouth (clusters, homophone stumbles, spoken-digit renderings of
   "six-two-six Labs").
3. **Advertising interpretation:** each line is a marketing claim in
   that market. Flag renderings that read as stronger than the English
   (an intensifier that becomes a guarantee), anything a market's ad
   norms would read as a safety/approval claim, and register mismatches
   with the market's ad conventions.
4. **The coined taglines get the full ad lens.** `Imagine autre chose.`
   (fr), `Imagina algo diferente.` (es, tú — the usted alternate
   `Imagine algo diferente.` is the flagged tradeoff: brand-word
   letter-identity vs register consistency; adjudicate), `Imagine algo
   diferente.` (pt-BR). These are first-ever renderings of the brand
   tagline: check for unintended second readings, collisions with known
   slogans in that market, and whether each stands as a closing line.
   de/pl/ru deliberately keep the English tagline — do not send that
   back as untranslated.
5. **Cross-language consistency pass** (dogfood finding 5): any issue
   found in one language gets checked in all six before verdicts close.

## Gate

Same as listings: a language is render-ready only when every line
approves. Verdicts JSON + per-language ready state; suggestedFix must be
a drop-in replacement for its quote.
