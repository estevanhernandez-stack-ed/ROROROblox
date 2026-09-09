# Localization wave 2 — the recommendation, measured

> Written 2026-09-08 against the Partner Center geographical-spread report, 93 markets,
> **1,536 total installs**. Companion to `localization-plan.md`, which set wave 1 and left
> wave 2 conditional on "if wave 1 moves numbers." This file is the input for that call, not
> the call itself.

## What wave 1 actually covers

Wave 1 is fr, de, ru, pt-BR, pl, es, shipped in v1.27.0.0.

| Language | Installs | % of total |
| --- | ---: | ---: |
| French (FR + BE) | 77 | 5.0% |
| German (DE + AT + CH) | 64 | 4.2% |
| Portuguese (BR + PT) | 59 | 3.8% |
| Spanish (12 markets) | 48 | 3.1% |
| Polish | 39 | 2.5% |
| Russian (RU + CIS) | 38 | 2.5% |
| **Wave 1 total** | **325** | **21.2%** |
| Wave 1 + English markets | 1,186 | 77.2% |

**Uncovered: 350 installs, 22.8%.** That is larger than any single wave-1 language, and larger
than French and German combined. It is also spread across roughly fifteen languages, which is
the whole problem with wave 2.

## The ratio held, which is the finding that matters

The 2026-09-05 measurement read ~43% non-English on a 577-install sample. This report reads
**43.9% on 1,536**, using the same definition the plan used, which puts the core English markets
plus the Philippines on the English side.

The sample nearly tripled and the ratio did not move. The original call was not an artifact of
thin data, and the case for wave 2 does not rest on a fluke.

## Wave 2 candidates, ranked by volume

| Candidate | Installs | % total | Note |
| --- | ---: | ---: | --- |
| Chinese Traditional (TW + HK) | 26 | 1.7% | zh-TW was 8 at the wave-1 call and the plan named it "first in line." It has **more than doubled**. |
| Danish | 22 | 1.4% | Highest single uncovered market. See the Nordics question below. |
| Vietnamese | 21 | 1.4% | Named as a wave-2 candidate in the plan. |
| Ukrainian | 16 | 1.0% | The plan left this as "a goodwill call left to Este" at 7 installs. Now larger than Spain. |
| Italian | 16 | 1.0% | **Never named in the plan.** Not wave 1, not the wave-2 shortlist. |
| Dutch | 16 | 1.0% | **Never named in the plan.** NL only; Belgium is already counted under French. |
| Japanese | 15 | 1.0% | **Never named in the plan.** |
| Romanian (RO + MD) | 14 | 0.9% | |
| Czech | 13 | 0.8% | |
| Norwegian | 12 | 0.8% | Nordics. |
| Thai | 11 | 0.7% | Named as a wave-2 candidate in the plan. |
| Swedish | 11 | 0.7% | Nordics. |
| Korean | 11 | 0.7% | |
| Turkish | 10 | 0.7% | |
| Indonesian | 9 | 0.6% | Named as a wave-2 candidate in the plan. |

**The top six together are 117 installs, 7.6% of total.** Wave 1's smallest language, Russian at
38, is larger than any single wave-2 candidate. That is the shape of the decision: wave 2 is not
six more Frances, it is a long tail.

## Three things the plan did not anticipate

**Italian, Dutch, and Japanese were never named**, in wave 1 or on the wave-2 shortlist, and all
three now sit at or above Spain's 8 installs. Spain shipped in wave 1. Whatever criteria put
Spanish in should be re-applied to these three, or the criteria should be restated.

**zh-TW earned its "first in line" label.** It was 8 at the wave-1 call and named the leading
wave-2 pick. At 26 across Taiwan and Hong Kong it is now the largest uncovered cluster. Treat
Hong Kong carefully: the traditional script is shared, the register is not, and shipping one
catalog for both is a judgment call rather than a free win.

**Ukrainian is no longer a footnote.** The plan deferred it at 7 installs as a goodwill question
alongside Russian. At 16 it outranks Spain, the Netherlands, and Italy. The goodwill argument
and the volume argument now point the same direction, which they did not before.

## The Nordics question, restated

Denmark, Norway, Sweden, and Finland are **50 installs across four languages**, 3.3% of total.
Denmark alone is the largest uncovered single market at 22.

The plan skipped them on English proficiency, and that reasoning has not weakened. Four
catalogs, four review cycles, and four ongoing maintenance obligations to reach a population
that demonstrably converts on the English listing already. **Recommend holding the skip**, and
recording it here so it stops being re-litigated every time Denmark tops the uncovered list.

The same logic still covers the Philippines at 82 installs, which remains the second-largest
single market overall.

## Recommendation

**Wave 2 is three languages: zh-TW, Vietnamese, and Ukrainian.**

- **zh-TW** is the largest uncovered cluster, it doubled since the last call, and the plan
  already named it first in line. It is the pick with the clearest prior commitment.
- **Vietnamese** is a named wave-2 candidate, it is the second-largest single uncovered market
  after Denmark, and Southeast Asia is where the plan's own audience argument said growth was.
- **Ukrainian** crossed from goodwill to volume, and it pairs with Russian, which already ships.

**Hold Italian, Dutch, and Japanese for a wave 3 decision**, not because they are small but
because they arrived unmodelled. They should be chosen deliberately or the selection criteria
should be rewritten to include them, and doing that properly is worth more than adding three
catalogs on momentum.

**Do not treat this as urgent.** Wave 1 shipped four days ago and its effect on acquisition is
not measurable yet. The plan's own condition was "wave 2 if wave 1 moves numbers," and nothing
here tests that. The right next measurement is the same report pulled again once v1.27 has been
live long enough to show a mix shift, compared against this file as the baseline.

## The cheaper win that is not a language

**Screenshot images still carry over from English** on all six wave-1 listings. A localized
listing currently shows ten English frames under a description promising the app speaks the
reader's language. That is a known gap, already recorded in `localization-plan.md`, deferred
from v1.27 because recapture is 10 shots across 6 languages against real accounts and running
clients.

Closing it reaches **every one of the 675 non-English installs already counted**, against 117
for the entire top-six wave-2 list. If there is effort available for localization before the
next measurement, it belongs here rather than in a seventh language.

## Method

Total and shares computed directly from the geographical-spread CSV, 93 markets, 1,536 installs.
Languages are grouped by market, so multilingual markets are assigned to their dominant language
and Belgium counts once, under French. English side is the core six plus the Philippines, which
is the definition `localization-plan.md` used when it set wave 1.
