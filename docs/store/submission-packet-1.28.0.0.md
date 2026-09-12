# Submission packet — v1.28.0.0

Everything Partner Center asks for on this submission, in the order it asks. Certification last saw
**v1.27.0.0**, which published, so this is a single version's delta.

---

## Read this first — the disclosure is a plugin call, not a manifest change

`Package.appxmanifest` is **byte-identical** to v1.27.0.0 — verified, `git diff v1.27.0.0..main` on
that file returns nothing. Same `runFullTrust` capability, same declared languages, same protocol
declarations, same startup task. Nothing to flag at validation.

The one thing worth disclosing is a **new call on the plugin interface**: a plugin may report a
number, and RoRoRo compares it against thresholds the user set and may raise a notification. A
reviewer reading that could reasonably ask what is being collected. The answer is specific and
favourable, so the letter leads with it rather than burying it:

- the shipped binary **gathers nothing** and contacts no new endpoint;
- it holds **no address, field path or vendor name** for any third-party data source, and a test in
  the build fails if one appears;
- the capability is **declined unless granted** at the consent sheet;
- input handling is unchanged — still nothing synthesized, nothing injected.

---

## 1. Packages to upload — both of them

Build with `scripts/finalize-store-build.ps1`, x64 first and then `-Architecture arm64`. Ship both;
a submission with only one silently narrows who can install.

## 2. Notes for certification

Paste the fenced block in [`reviewer-letter-1.28.0.0.md`](reviewer-letter-1.28.0.0.md) — the one
below the `---` marker, from `Hello reviewer,` to `626 Labs LLC`. Reviewer-only; not shown to
users.

## 3. What's new in this version (public — do not skip)

Paste from [`whats-new-1.28.0.0.md`](whats-new-1.28.0.0.md) — **seven blocks, one per listing
language**. The English block goes in the en listing; each translated block goes in its own language
listing (fr, de, ru, pt-BR, pl, es).

This is the public field Store users read, and it is a *different field* from Notes for
certification above. Both get filled.

Audience note: the Store is on v1.27 and this audience has it, so unlike last time there is no
catching-up to do. Every block carries the plugin caveat — metric alerts is the headline and nothing
in this package reports a number, so a block that announced it without saying so would send people
hunting for a switch that appears to do nothing.

---

## 4. Listing changes this submission

| Surface | Outcome |
|---|---|
| **Short description** | **Audited, unchanged.** It names the six languages and the multi-launcher claim; v1.28 adds no language and changes neither. |
| **Long description** | **Audited, unchanged.** The privacy paragraph was the thing to check, since v1.25 is the precedent where a shipped feature made it incomplete. It still holds: a metric alert travels the destinations the user already configured — their Discord webhook, their phone service — and adds none. RoRoRo itself fetches nothing new, so "nothing leaves your machine except…" remains true as written. |
| **Product features** | **Audited, no new entry. 19/20 used.** Metric alerts is deliberately **not** claimed yet. The standard is this sheet's own, recorded at v1.24: a feature earns a listing claim when it actually works for a user, "which is what earns them a listing claim now and not before." Nothing shipping in v1.28 reports a number, so the switch does nothing for a Store user today. It earns its entry in the release that ships alongside a plugin that makes it real. |
| **Hub page (`docs/index.md`)** | **Audited, unchanged, and this one is a close call.** The "Alerts wherever you want them" bullet enumerates the kinds — drops, memory warnings, recycle completions, the two-hourly all-good mark — and there is now a fifth. Left alone for the same reason as the feature entry: listing a kind nobody can currently trigger reads as a claim, not as a completeness fix. Add it in the same release as the plugin. |

**The QR code is not claimed anywhere either.** It is a setup convenience on an existing feature,
not a feature, and the phone-alerts entry already covers the capability it makes easier.

---

## 5. After submission

- Do not open this submission in Partner Center after it is submitted, and do not mix the API path
  into it. This release is the **browser** path, because it carries packages and the listing API is
  text-only by design.
- Once it publishes, update `apps/rororo-live/config.toml` in the store-listing-console repo: `ref`
  to the commit carrying this copy, `version` to `1.28.0.0`. Then `console.py` and a `plan` should
  report **nothing would change** — that is the check that the repo and the Store agree.
- Outstanding from the v1.27 cycle, unchanged and still needing Partner Center by hand, not the API
  (whose `title` readout is unreliable in both directions): the **Russian and Polish** reserved
  names were reserved and never applied, and **Spanish** has no correct reservation — the only one
  is *Multi-inicializador*, reserved in error, which means "initializer".
