# Submission packet — v1.29.0.0

Everything Partner Center asks for on this submission, in the order it asks. Certification last saw
**v1.28.0.0**, which certified and published on 2026-09-12, so this is a single version's delta.

---

## Read this first — nothing new crosses the network, and the plugin contract did not move

`Package.appxmanifest` is **byte-identical** to v1.28.0.0 — verified, `git diff
v1.28.0.0..v1.29.0.0` on that file returns nothing. Same `runFullTrust` capability, same declared
languages, same protocol declarations, same startup task. After Phase 3 patches it, the only
difference will be `Version="1.29.0.0"`.

The two things a reviewer could reasonably ask about both have short, favourable answers:

- **`plugin_contract.proto` appears in the diff, and the change is comment-only.** No field, no
  field number, no message, no RPC. `git diff v1.28.0.0..v1.29.0.0` on that file shows one comment
  block corrected — the cooldown it described for all alert kinds is now per account and metric id
  for metric breaches. So: no new capability, no new `RpcMethodCapabilityMap` entry, no consent
  change. A plugin built against v1.28 is unaffected.
- **Every Discord webhook POST now sends `allowed_mentions` with an empty parse array.** That is a
  restriction on a call already disclosed at v1.4, not a new call: it stops anything inside an
  alert — a rule name, a metric id, an account name — from notifying a channel. It narrows what the
  app can do, which is the direction a reviewer wants.

Unchanged and worth restating in the letter: the shipped binary still **gathers no metric itself**,
holds **no address, field path or vendor name** for any third-party data source (`NoVendorNameFenceTests`
fails if one appears), contacts **no new host**, and synthesizes **no input** and injects into
nothing.

---

## 0. The version bump has not run yet — do not skip Phase 3

At tag `v1.29.0.0` both `src/ROROROblox.App/ROROROblox.App.csproj` (`<Version>`) and
`src/ROROROblox.App/Package.appxmanifest` (`<Identity Version>`) still read **1.28.0.0**. That is
expected — `scripts/finalize-store-build.ps1` patches both in Phase 3 — but it means a package
built without that script carries a 1.28 identity and Partner Center will reject it as a duplicate
version. Run the script; do not hand-edit either file.

## 1. Packages to upload — both of them

Build with `scripts/finalize-store-build.ps1 -Version 1.29.0.0`, x64 first and then
`-Architecture arm64`. Ship both; a submission with only one silently narrows who can install.

## 2. Notes for certification

Paste the fenced block in `reviewer-letter-1.29.0.0.md` — the one below the `---` marker, from
`Hello reviewer,` to `626 Labs LLC`. Reviewer-only; not shown to users. **Partner Center only —
the API cannot write this field.**

**That letter is still owed at the time of this packet.** It is a routine release, so it takes the
short shape (the v1.7.0.0 letter is the model, not the v1.4 full-defense one): deltas stated in
full, everything else one line, and the v1.4 clause-10.2.2 defense referenced rather than
re-litigated. It should lead with the two items in *Read this first* above — the comment-only proto
change and the mention-restricting `allowed_mentions` — then one line each for no manifest change,
no new host, and no change to input handling.

## 3. What's new in this version (public — do not skip)

Paste from [`whats-new-1.29.0.0.md`](whats-new-1.29.0.0.md) — **seven blocks, one per listing
language**. The English block goes in the en listing; each translated block goes in its own language
listing (fr, de, ru, pt-BR, pl, es). Ten rows, seven blocks: German serves two rows and French
three. Prefer the listing console (`submit_write.py plan` / `apply`, runbook
`store-listing-console/docs/api-update-listings.md`); it writes all ten in one call and refuses
anything over cap.

This is the public field Store users read, and it is a *different field* from Notes for
certification above. Both get filled.

Audience note: the Store is on v1.28 and this audience has it, so there is no catching-up to do.
Two things about the blocks are deliberate. The plugin caveat carries forward from v1.28 — the
wording and grouping changes only appear for someone running a plugin that reports numbers, and
nothing in this package reports one, so each block says which sections need a plugin and which
apply to alerts people already have. And **every translated block states that the alert sentence is
still English**: `WebhookPayload` is Core and composes in English for the toast, both webhooks and
the phone, so a Polish reader told "alerts now say what fired" would otherwise read an English
sentence and take it for a translation miss.

---

## 4. Listing changes this submission

Audited 2026-09-15 per the standing Phase 2 rule (Este, 2026-09-05), against
[`whats-new-1.29.0.0.md`](whats-new-1.29.0.0.md), the spec's APPROVED DEVIATIONS banner and the
2026-09-15 entry in `docs/decisions.md`. `release-notes-1.29.0.0.md` is still owed and is the other
Phase 2 artifact; nothing in it should change this table, but re-read the table against it when it
is written.

| Surface | Outcome |
|---|---|
| **Short description** | **Audited, unchanged.** It names the six languages, the multi-launcher claim, and a list ending "phone alerts, live status, themes." v1.29 adds no language, no destination and no top-level capability, and makes none of those nouns wrong. There is also no room: the recorded count in `listing-copy.md` says 197/200, the string actually measures **199/200** — still under cap, but the note is two characters optimistic and worth correcting the next time that block is touched. |
| **Long description** | **Audited. One proposed edit, the owner's call — recommended.** The privacy paragraph is the thing to check, since v1.25 is the precedent where a shipped feature made it incomplete. It still holds as written: no new host, no new destination, and `allowed_mentions` restricts a call already named there. The alerts bullet enumerates the alert kinds ("Drops, memory warnings, recycle completions, and an every-two-hours all-good mark") and v1.29 adds no kind, so the enumeration stays correct. What is newly worth saying is the mention restriction — the bullet's own use case is a Discord channel, and for a clan channel "nothing in an alert can ping it" is a trust claim that is now true for every alert kind. Paste block below. **Metric alerts stays unclaimed**, for the reason in the features row. |
| **Product features** | **Audited, no new entry. 19/20 used, one slot free.** Nothing in the list is made wrong by v1.29. Metric alerts is still deliberately not claimed — but **the condition the v1.28 packet set has moved, and the owner should know it.** v1.28 deferred on "nothing shipping in v1.28 reports a number"; that is no longer strictly true. Ur Score 0.3.2 writes rule labels, and the live 8-account run on 2026-09-15 that motivated this whole release was a real reporter feeding a real RoRoRo. What has **not** moved is installability: `docs/store/plugins-catalog.json` lists four plugins (Ur Task, Ur OCR, Ur AFK, Ur MCP) and Ur Score is not among them, so a Store user still cannot get a reporter from inside the app. The v1.24 standard is "works for a user," not "exists," so the entry waits for the release that adds Ur Score to the catalog. Draft entry, ready for that release, below. |
| **Hub page (`docs/index.md`)** | **Audited, unchanged — the same close call as v1.28, with the same answer.** The "Alerts wherever you want them" bullet enumerates four kinds (drops, memory warnings, recycle completions, the two-hourly all-good mark) and there is still a fifth it does not name. Left alone: listing a kind nobody can currently trigger without an out-of-catalog plugin reads as a claim, not as a completeness fix. Add it in the same release as the feature entry. |

### Proposed long-description edit (owner approves before it is applied)

Replace the alerts bullet in `listing-copy.md`'s long-description block with this. One sentence
added, before the fresh-install sentence; nothing else in the block moves.

```
• Optional alerts — desktop, Discord, or your phone. Route each alert to any mix of desktop notifications, a Discord webhook you create, and your phone through Pushover or ntfy. Drops, memory warnings, recycle completions, and an every-two-hours all-good mark while your accounts run. Alerts post with mentions switched off, so nothing in one can ping the channel it lands in. A fresh install makes no alert calls at all — nothing fires until you set a destination up.
```

**Reason to take it:** the shared clan channel is the listed use case, and this is now true of every
alert kind, not a metric-alerts detail. **Reason to leave it:** it is a hardening rather than a
feature, and that bullet is already the longest in the block. Either answer is defensible; the
audit's job is to put it in front of the owner rather than decide it.

### Draft feature entry — held for the release that ships a catalog reporter

Not to be added this submission. Recorded here so it does not have to be rewritten later.

```
Metric alerts — set your own thresholds on numbers a plugin reports, and one read across every account arrives as one alert instead of one per account
```

150 characters, inside the 200 cap. It would take the list to 20/20 — the last free slot — so the
release that spends it should be the one that makes the claim true for a Store user.

### Translated listing entries that would need re-approval

`listing-approvals.json` and everything under `docs/store/translations/` were **not touched** by
this audit; translations are approved through the verifier's own hash flow. What that flow would
owe, if the owner accepts the above:

- **If the long-description edit is accepted:** `longDescription` for **de, es, fr, pl, pt-br, ru**
  — six entries — go stale, because the approval pins an `enHash` of the English block. The same
  sentence is owed in `listing-copy-de.md`, `-es`, `-fr`, `-pl`, `-pt-br`, `-ru`, then re-verified
  and re-approved. Nothing else in the file is affected: `shortDescription`, `features`,
  `copyright` and `trademark` keep their current `enHash` because their English did not change.
- **If it is declined:** zero entries go stale from this audit.
- **Independent of the owner's answer:** `whatsNew` for all six languages is new copy every release
  and has no approval for the v1.29 blocks. Worth knowing that the six approved `whatsNew` pairs in
  the file were generated on 2026-09-07 against source commit `03c2fc2` — they are already three
  releases stale, so the file is not tracking what is actually pasted into the ten rows.
- **Deferred with the feature entry:** taking the draft entry later invalidates `features` for all
  six languages the same way.

---

## 5. After submission

- **Mixing the API and Partner Center on one submission is fine** — measured on v1.28, submission
  1152921505701878932. Write the ten what's-new rows through the API, upload the packages and paste
  the reviewer letter by hand, then commit. Neither tool sees the other's edits until that commit,
  so verify each half from the side that made it and do not trust an API read of a submission in
  flight. Do not open the submission in Partner Center once it is in certification.
- Once it publishes, update `apps/rororo-live/config.toml` in the store-listing-console repo: `ref`
  to the commit carrying this copy, `version` to `1.29.0.0`. Then `console.py` and a `plan` should
  report **nothing would change** — that is the check that the repo and the Store agree.
- Still outstanding from the v1.27 cycle, unchanged, and still needing Partner Center by hand rather
  than the API (whose `title` readout is unreliable in both directions): the **Russian and Polish**
  reserved names were reserved and never applied, and **Spanish** has no correct reservation — the
  only one is *Multi-inicializador*, reserved in error, which means "initializer".
- Owed outside Partner Center, from this release's own execution record: the 17-scenario smoke
  harness has not been run live against a real build, so the new one-read grouping scenario has not
  yet been proven able to go red, and nobody has looked by eye at a real Windows toast for a group
  of four or more accounts. Neither blocks the submission; both are worth closing before the clan
  post.
