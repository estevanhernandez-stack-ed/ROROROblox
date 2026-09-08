# Submission packet — v1.27.0.0

Everything Partner Center needs for this submission, in the order the playbook's Phase 7 asks for
it. Artifacts are built; the clicks are yours.

---

## Read this first — a two-version delta, and the manifest change is the disclosure

Certification last saw **v1.25.0.0**. v1.26.0.0 shipped to the direct-download channel only and
was **never submitted to the Store** — we held it because its localization was incomplete. So this
submission carries two versions of change, and the manifest delta it introduced (six additional
declared languages) reaches a reviewer for the first time here.

No capability change, no network change, no data-handling change since v1.25. The reviewer letter
front-loads all of this so it is not a surprise at validation.

---

## 1. Packages to upload — both of them

- `dist/RORORO-Store-x64-1.27.0.0.msix`
- `dist/RORORO-Store-arm64-1.27.0.0.msix`

Unsigned by design — Partner Center signs after upload. Drag both into the Packages slot; if
validation complains about the version, check the 4th component is `0` (it is).

---

## 2. Notes for certification

Paste the block between the `---` markers from
[`reviewer-letter-1.27.0.0.md`](reviewer-letter-1.27.0.0.md). Reviewer-only; not shown to users.

---

## 3. What's new in this version (public — do not skip)

Paste from [`whats-new-1.27.0.0.md`](whats-new-1.27.0.0.md) — **seven blocks, one per listing
language**. The English block goes in the en listing; each translated block goes in its own
language listing (fr, de, ru, pt-BR, pl, es).

This is the public field Store users read, and it is a *different field* from Notes for
certification above. Both get filled.

Note the audience: the Store is on v1.25 and never received v1.26, so this copy presents the
localization as a whole rather than as a completion. The GitHub release notes tell the
completion story to the direct-download users who actually have v1.26.

---

## 4. Listing changes this submission

The Phase 2 three-surface audit ran against the fresh release notes. Outcome: **two surfaces
edited, one unchanged, and one real drift found and fixed.**

| Surface | Outcome |
|---|---|
| **Short description** | **Unchanged.** Still accurate — "now in 6 languages" is the same claim, and the language count did not change. 199/200 chars. |
| **Long description** | **Edited.** The "Speaks your language" bullet now covers the messages the app composes while you use it (not just the screens) and says switching is instant. The v1.26 wording claimed "every menu, setting, tooltip, and window" before that was true — v1.27 makes the claim accurate rather than aspirational. Paste the refreshed block from `listing-copy.md`. |
| **Product features** | **Edited, no new entry.** The language entry now reads "the whole app, screens and messages alike… switching instantly." No new entry added — v1.27 completes a claim the v1.26 entry already made. Still 18/20 used, 192/200 chars. |
| **Hub page (`docs/index.md`)** | **Drift found and fixed.** The "What you get" list had *no* localization bullet at all — v1.26 shipped localization and this page never got it. Added. Ships with the repo, not Partner Center. |

---

## 5. After submission

- Status moves *In submission* → *Certification* → *Publishing*, typically 24–72h.
- The GitHub Release (Phase 6) is independent and can go live first.
- Clan Discord post (Phase 8) waits until the GitHub Release is un-drafted.
- If the previous submission were still pending, this one replaces it — not applicable here, since
  v1.26 was never submitted and the live Store version is v1.25.
