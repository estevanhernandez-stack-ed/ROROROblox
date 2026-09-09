# Dogfood feedback — translation-verification

> From verifying RoRoRo's catalog: 1,017 keys × 6 languages = 6,102 translated
> strings, shipped to the Microsoft Store as v1.27.0.0 on 2026-09-08.
> Issue-ready. Adds to the 12 findings already filed as issue #1.

The pilot earned its keep — it found five real defects we would otherwise have
shipped, including two that no lint could catch. What follows is what a full-catalog
run taught us, and it is mostly about **separating findings that are defects from
findings that are opinions**.

---

## 1. Full-app scale still times out

**Severity: high. Already open as issue #13 — this is confirmation at production size.**

At 1,017 keys × 6 languages the run does not complete. We shipped v1.27 leaning on
in-workflow adversarial reviewers plus a deterministic lint instead, and kept the
verifier for incremental spot-review.

That is a real loss, because the verifier is the only thing in the pipeline that
reads for **voice** rather than for structure. Chunked or resumable runs would put it
back on the critical path.

---

## 2. Terminology preference and defect need different severities

**Severity: high — it is the difference between a usable report and one that gets skimmed.**

The pilot returned 16 findings. Triaged:

| verdict | count | what they were |
|---|---:|---|
| real defect, user-visible | 3 | `Export_SavedBody` |
| real defect, **unreachable string** | 2 | `Shell_StopAll_Running_other`, `Shell_Muted_other` (Polish) |
| false positive | 1 | German `CoreMsg_Compat_Banner` (see #3) |
| terminology preference | 10 | French word choice, all defensible either way |

**Ten of sixteen were preferences.** Not wrong, not worth a translator's time, and
they dominated the report by volume. A reviewer skimming that list has to do the
triage the tool should have done.

The two Polish rows deserve their own line, because an earlier draft of this document
counted them simply as "real defects" and that was misleading. They are genuine
grammar errors — and they are in `_other` arms that a Polish integer count can never
select (see §4). We fixed them anyway in PR #198 for catalog hygiene, and recorded in
that commit that no user could ever see them. So the honest score is **three defects a
user could hit, two in dead strings, one false positive, ten preferences** — which is
a more useful shape than "five real" and is the reason §4 exists at all.

**Ask:** a distinct severity — `preference` vs `defect` — decided by whether the
finding claims the string is *incorrect* or merely *not what I would have written*.
The prompt already knows the difference; the output format does not preserve it.

The useful shape the pilot suggested: findings that survive across independent runs
are defects; findings that appear in one run and not another are preferences. We had
three runs of the same 306 pairs and the split fell out almost cleanly.

---

## 3. Check format-token **multisets**, never token order

**Severity: medium — one confirmed false positive, and the class will recur.**

The tool flagged German `CoreMsg_Compat_Banner` for reordering placeholders relative
to English. The reorder is **required**:

```
en: Roblox updated to {1} … {0}
de: Roblox wurde auf {1} … {0} aktualisiert
```

German is verb-final. `aktualisiert` has to land at the end of the clause, which
moves the placeholders. Any language with different constituent order — German,
Japanese, Turkish — will trip an order-sensitive check on correct translations.

The invariant that actually matters is that the **multiset** of tokens is preserved:
same tokens, same count, none invented, none dropped. Order is a translator's tool,
not a defect. Our `lint-translations.py` checks the multiset and has produced zero
false positives across six languages.

---

## 4. Know which plural categories are reachable

**Severity: medium — the tool reviewed strings no user can see.**

Some findings were about Polish and Russian `_other` forms. For **integer** counts in
those languages, the CLDR selector can never return `other` — it exists for
fractional quantities. Every `_other` entry in pl/ru is a dead string.

Reviewing them is wasted reviewer attention at best, and at worst it invites a
"correction" that propagates the wrong grammar into the forms that *are* reachable.
We nearly did exactly that.

**Ask:** given the key's usage (integer count vs decimal), suppress findings against
unreachable plural arms, or mark them `unreachable` so triage can drop them wholesale.

---

## 5. Cross-language parity is a cheap, high-yield check the tool doesn't do

**Severity: low — offered as a feature idea, since we built a crude version.**

We added advisory parity checks to our own lint: a load-bearing-term list (does every
language render "publicly" as *something*?) and a length-ratio outlier (a translation
under 55% of the median length for that key, floor 40 chars, is usually a dropped
clause).

Both found real things. A third check — **negation parity** — we wrote and then
deleted: four false positives, zero real findings, because languages express negation
structurally in ways a token check cannot see. The deletion is recorded in the source
with the reason, so nobody rebuilds it.

Offering that last part as the useful bit: the negative result is worth as much as
the two that worked.

---

## What worked, and should not be traded away

- **The reviewers made real grammatical fixes rather than rubber-stamping.** Distinct
  ru/pl genitive plural forms (клиент / клиента / клиентов), a French gender-safe
  apposition for a `{0}` that can be masculine *jeton* or feminine *clé*, the pt-BR
  decimal separator matching `{0:F1}`-rendered numbers. None of those are lintable.
- **Rubric noise was zero.** Across 34 opportunities the reviewers never flagged the
  rubric itself — which is why we dropped a planned rubric-variant experiment. That
  data point saved us a workstream.
- **Every finding got traced to the code or the grammar before it was accepted.** The
  German banner was demoted by reading German constituent order and confirming the
  reorder was required — not by a second reviewer pass. Stating that precisely because
  an earlier draft of this document credited adversarial framing for the demotion, and
  that was not what happened. The lesson the pilot actually supports is narrower and
  more useful: **a finding about a language nobody on the team reads is the most
  expensive kind to dismiss, and the only thing that dismisses it safely is tracing the
  grammar.** Which is an argument for the tool carrying its own reasoning in the
  finding, so the reader can check it without becoming a German speaker first.
