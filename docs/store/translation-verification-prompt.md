# Translation verification — build brief for the Gemini tool-builder agent

> Este is building a translation verification tool (Gemini, on Google Cloud + Firebase) and
> the agent building it needs to know what to build and where the dogfood is. **Paste the
> fenced block below to that agent.** It contains the build brief AND, embedded at the end,
> the review rubric the finished tool runs at verification time — two audiences, one paste.
> RoRoRo's Store-listing translations are the first real dataset; the repo side of the
> contract lives in `localization-plan.md` (Phase A approval gate) and
> `scripts/export-listing-translations.py`.

> **The tool has its own repo:** `github.com/estevanhernandez-stack-ed/translation-verification`
> (source: `src/core/reviewer.js`, `rubric.js`, `ingest.js`, `export.js`, `repo-patcher.js`,
> `approval-gate.js`, `src/mcp/server.js`, `src/oauth/*`). The dogfood findings below were
> relayed there as **issue #1** (2026-09-07), mapped to modules. File future feedback there.

## Dogfood feedback — first connection attempt, 2026-09-05 (relay to the builder agent)

The tool deployed (dashboard + connect portal + 2nd-gen Cloud Function + SSE endpoint) and a
remote agent tried to connect the same day. Three findings, in severity order:

1. **The token surface was unauthenticated.** The `/connect` page rendered for an anonymous
   fetch (no sign-in) with the token slot wired to load — and the bearer token is the
   APPROVAL credential, so an ungated token endpoint hands strangers the one integrity
   property the gate exists for. Token issuance/display must sit behind the OAuth identity
   the portal already mentions. (Este rotated the exposed token on discovery.)
2. **The advertised SSE URL cannot stream.** `https://project-626labs-translations.web.app/sse`
   hangs with zero bytes (20s+): Firebase Hosting rewrites buffer responses through the CDN,
   so SSE never flushes — a platform limit, not a bug in the function. The direct function
   URL (`https://us-central1-project-626labs.cloudfunctions.net/translationVerifier/sse`,
   Cloud Run underneath) responds instantly and is the URL the portal should advertise for
   SSE.
3. **Auth plumbing works** — both `?token=` and `Authorization: Bearer` reach validation
   (clean `{"error":"Unauthorized: Invalid token"}` on a stale token). Consider scoping:
   a read-only token for review sessions vs. the approval token.

## Dogfood feedback — first full review cycle, 2026-09-06 (the tool WORKS; these sharpen it)

The MCP connection succeeded (OAuth per the MCP spec — finding #1 above addressed) and the
full loop ran: ingest → review ×6 languages → repo fixes → re-export → re-review → **36/36
approve** in two rounds. The review quality was real: it caught the reviewer's own author
breaking his own rubric (translated `Settings > Alerts` paths in every language), dropped
claims (auto-update in every short description), and — best catch — a source-side defect:
all six copyright verdicts flagged the translations for carrying the trademark disclaimer,
and the root cause was the ENGLISH block, which had drifted from the certification
requirement. The gate surfaced a real EN inconsistency even though its surface diagnosis
pointed the wrong direction. Findings for the builder, severity-ordered:

4. **`suggestedFix` must be a drop-in replacement for `quote`.** Several fixes were
   full-field rewrites paired with tail-only quotes (e.g. fr shortDescription: quote
   `"statut en direct, thèmes."`, fix = the entire rewritten description) — machine-applying
   quote→fix would duplicate the field's head. The repo's applier assumes the contract as
   written; these had to be applied by hand.
5. **Same defect, different verdicts across languages.** The dropped mutex clause existed in
   all six long descriptions but was flagged in 2; the genericized "Test my phone" button in
   all six whats-new but flagged in 2; the dropped "one failure nothing can announce
   directly" clause in several but flagged in 1 (and only in a later round). Per-pair
   isolated review invites this variance — consider a cross-language consistency pass for
   any issue found in one language.
6. **Default ingest is CDN-stale.** The branch raw URL is cached ~5 minutes on
   raw.githubusercontent, so an ingest right after a push fetched the previous commit.
   Ingesting by commit-pinned raw URL (resolve the branch SHA via the GitHub API first)
   would make ingestion deterministic and match the sourceCommit contract.
7. **The all-languages review call exceeds the MCP timeout** (36 Gemini reviews in one
   request). Per-language calls work; the full run wants a job pattern (return a run id,
   poll status) or internal chunking.
8. **Result payloads mix rounds without stamps.** Responses accumulate every reviewed pair,
   carry stale verdicts for pairs whose text has since changed, and label the whole array
   with the FIRST round's sourceCommit. Stamping each result with the sourceCommit it was
   judged at (and evicting results for pairs whose text changed) would make the state
   readable. (Later rounds behaved better — see #11.)
9. **One hallucinated finding in ~30** (surfaced in the polish pass): pt-br whatsNew was
   flagged for "PC or o app" — a quote that does not exist in the text (the file says
   "o PC ou o app"). It persisted across two review calls, then cleared on a third.
   Ground verdicts server-side: a `contains` check of every issue's `quote` against the
   ingested field text before emitting would turn hallucinations into automatic re-rolls.
10. **Round-to-round strictness lottery.** Identical text approved in rounds 2-3 drew a new
    blocker in round 4 (the tray-icon clause, one language of six sharing the omission), and
    a clause fixed where flagged (es) was flagged elsewhere a round later. Each pass can
    surface a new shared micro-omission in one random language. Grounding plus a
    two-vote-for-blockers scheme would stabilize verdicts; until then, the human approver
    should treat late-round singleton blockers with judgment.
11. **The sourceCommit stamp works after re-ingest** — round 3+ responses carried the fresh
    commit and cleanly reset state (contrast finding #8, observed in round 1's
    accumulate-forever behavior; whatever changed between rounds, keep it).
12. **Review results don't survive a cold start.** At approval time, `batch_approve_passing`
    answered "No review results available" — the ingest and the approval gate persisted
    (Firestore), but the review RESULTS lived in the function instance's memory and died
    with a scale-to-zero recycle. All six languages had to be re-reviewed to repopulate
    before approving (during which the round-6 lottery flagged, then re-roll-cleared, a
    fr field approved five times prior). Persist results beside the gate; a verdict that
    can vanish between review and approval breaks the tool's own workflow.

**Cycle closed 2026-09-07:** `batch_approve_passing` recorded 36/36 (approver
`este-via-claude-desktop-agent`, Este's instruction) at sourceCommit `148676f` —
`allReady: true`, all six languages READY FOR PARTNER CENTER.

---

```
You are building a translation verification tool. Its job: present
translated Microsoft Store listing copy next to its English source,
have Gemini review each translation against a rubric, and let a human
approve or send back every piece — nothing ships to the Store
unapproved. The stack is yours to design on Google Cloud + Firebase.
This brief gives you the first real dataset (the dogfood), the data
contract, the workflow the tool must respect, and the review rubric to
embed in your model calls.

THE DOGFOOD

Project: RoRoRo — a Windows desktop app (626 Labs) that runs several
Roblox clients side by side. Its Store listing was just translated
from English into six languages, and those translations are waiting to
be verified. Real data, real caps, real legal text.

Fetch the dataset from the public repo:

  https://raw.githubusercontent.com/estevanhernandez-stack-ed/ROROROblox/main/docs/store/listing-translations.json

(repo path: docs/store/listing-translations.json — regenerated by
scripts/export-listing-translations.py whenever the copy changes)

THE DATA CONTRACT (what that JSON contains)

- product, storeId, sourceLanguage ("en"), languages
  ["fr","de","ru","pt-br","pl","es"], sourceCommit (the git commit the
  export was generated at), generatedUtc, sourceOfTruth
- fields: six objects, one per Store field:
  - field: shortDescription | longDescription | features | whatsNew |
    copyright | trademark
  - en: the English source of truth
  - translations: { languageCode: translated text }
  - charCap (when present): a HARD Microsoft Store limit on the whole
    block, in characters — shortDescription 200, whatsNew 1500
  - perLineCharCap (features only): 200 per line; each of the 17 lines
    is pasted into the Store as one separate feature entry, so line
    structure must survive round-trips through your UI untouched
  - version (whatsNew only), occasional note

That is 6 fields x 6 languages = 36 reviewable pairs. Text is UTF-8
with real typography (em dashes, « », „ ", Cyrillic); count caps in
characters, and never normalize or re-wrap the text you display or
export.

THE WORKFLOW YOUR TOOL SITS INSIDE

1. The repo is the source of truth. The tool never edits copy; it
   ingests an export and produces verdicts.
2. Gemini reviews each (language, field) pair against the rubric below;
   a human sees the translation beside its English, with Gemini's
   verdict and issues, and decides: approve, or send back.
3. Send-backs leave as a verdicts JSON (shape defined at the end of the
   rubric) — each issue carries a concrete suggestedFix. Those become
   edits to the repo's per-language markdown files; the export is
   regenerated with a new sourceCommit and re-uploaded.
4. Approval state is keyed on (sourceCommit, language, field). A new
   sourceCommit invalidates prior approvals for any field whose text
   changed — never let a stale approval cover new words. Persist this
   state (Firestore is the natural home).
5. The gate: a language may be marked ready for Partner Center only
   when all six of its fields are approved at the current sourceCommit.
   Make "ready" loud and exportable — the human pastes into Partner
   Center from the repo files, trusting your gate.

This repeats every release: the app ships a new "what's new" block per
version, translated into every listing language, so design for many
uploads over time, not one.

THE REVIEW RUBRIC (embed this in the model call that reviews a pair;
the tool's Gemini reviewer acts on exactly these instructions)

--- RUBRIC START ---

You are the translation reviewer for RoRoRo. Judge each (language,
field) pair against its English source. The English is the source of
truth: do not critique it, and do not improve the message beyond what
it says. Readers are Roblox players — young, gaming-fluent, reading
their market's Microsoft Store; the voice is builder-to-builder.
Registers, flag any drift WITHIN a language:
  de = du · fr = vous · es = tú · pt-br = você · pl = ty · ru = вы

Check, in priority order:

1. CLAIM FIDELITY — exactly the claims the English makes, nothing
   added, softened, or dropped. Zero-tolerance items:
   - Privacy/security sentences (password never seen, encryption
     claims, no telemetry, what leaves the machine and when).
     Overpromising safety is the worst possible defect.
   - The trademark disclaimer: not affiliated with, endorsed by, or
     sponsored by Roblox Corporation; the term used only to describe
     compatibility; the official client launched unmodified. Full
     legal sense in every language.
   - The line stating the app's interface is currently in English must
     survive in every longDescription — absence is an automatic
     "revise".
2. HARD LIMITS — count characters against charCap / perLineCharCap.
   Over-cap is an automatic "revise"; any suggestedFix must also fit.
3. UNTRANSLATED TOKENS — these stay exactly as written: RoRoRo,
   RORORO, 626 Labs, Roblox, Windows, Microsoft Store, Discord,
   Pushover, ntfy, Velopack, WebView2, Squad Launch, Friend Follow,
   Recycle (feature name), Discord Join, quoted in-app strings
   ("4h up — 6 accounts in"), URLs, and UI paths like Settings >
   Alerts (the app's UI is English, so the path the reader must click
   is English). Natural grammatical particles around a token are fine.
4. ACCURACY — numbers, feature behavior, defaults, and conditionals
   ("only if you set it up") survive precisely.
5. NATURALNESS — reads like a native Store listing; flag calques and
   wrong gaming vocabulary for that market; suggest natural phrasing.
6. CONSISTENCY — within a language, one concept keeps one term across
   all fields.

Do not rewrite for style when a translation is faithful and natural —
minimal edits only. Do not translate the untranslated tokens. Do not
add claims the English lacks. Do not approve with caveats: if a caveat
matters, it is a "revise".

Output one JSON object:

{
  "sourceCommit": "<echoed from input>",
  "results": [
    { "language": "...", "field": "...",
      "verdict": "approve" | "revise",
      "issues": [
        { "severity": "blocker" | "minor",
          "quote": "<exact problem text, shortest possible>",
          "problem": "<one sentence, naming the rule above>",
          "suggestedFix": "<minimal replacement, fits the caps>" } ] }
  ],
  "summary": { "<language>": "<one sentence>" }
}

Any blocker means "revise"; every "revise" carries at least one issue
with a concrete suggestedFix. Minor-only fields may be "approve" if
shipping as-is would embarrass no one.

--- RUBRIC END ---

WHAT DONE LOOKS LIKE FOR THE DOGFOOD RUN

The 36 RoRoRo pairs load, Gemini reviews them against the rubric, the
human walks the verdicts, and the tool hands back one verdicts JSON
plus a per-language ready/not-ready state. If your tool can take this
dataset to "six languages ready" and its send-backs are precise enough
to apply as file edits without interpretation, it works.
```
