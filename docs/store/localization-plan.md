# Localization plan — listing languages first, UI localization by phases

> Este's cross-app ruling, 2026-09-05 (same decision as 626-mod-launcher; his measurements of
> this repo, same day). Referenced from `release-playbook.md`. This file is the plan of record;
> the phases land as their own cycles.

## The rule (verbatim intent)

Three separate things get called "languages":

1. **Listing languages** — translated description and screenshots in Partner Center.
   Translation only, no code.
2. **Package declared languages** — the manifest's `<Resource Language="en-us" />`. Free to
   edit, and **a lie unless the UI is actually translated**.
3. **The app's UI language** — real localization.

**Add the first. Never add the second without the third.** The manifest stays `en-us`-only
until the UI genuinely speaks another language.

## Measured state (Este, 2026-09-05)

- 395 hardcoded string literals in the XAML; no localization infrastructure of any kind.
- ~113 English literals inside `ROROROblox.Core`.
- Neither of the mod launcher's blockers exists here: no `InvariantGlobalization` flag, and
  nothing strips satellite-language content out of the publish. That whole remediation step
  is skipped.

One framing correction for this repo, which changes the tooling target and nothing else:
RoRoRo is **WPF** (.NET 10 + WPF-UI), not WinUI/Windows App SDK. `x:Uid` + `.resw` is the
WinUI yardstick; the WPF equivalents are `.resx` resources referenced from XAML. The
measurement's meaning is unchanged — zero localization infrastructure either way — but the
`/vibe-lingual` adapter this repo needs is a **WPF adapter**, a sibling of the mod launcher's
WinUI one, not the same artifact.

## Phase A — listing languages (no code; can start any release)

Partner Center → Store listings → add a language; each added language gets its own
description, features, and what's-new fields. Screenshots may carry over from the default
language at first (the UI is English regardless — honest either way).

- **Open decision (Este):** the language set. Nothing is measured about the clan's locales;
  pick small and deliberate rather than broad.
- Translate from `listing-copy.md`'s three blocks (short description, long description,
  features) — the audited-every-release surfaces. Keep the trademark disclaimer in every
  language; it is a certification surface.
- **Standing per-release cost:** each `whats-new-X.Y.Z.0.md` must be translated for every
  listing language at Phase 2 time, and Phase 7 pastes each language's block. The Phase 2
  listing audit covers the translated listings the same as the English one.
- The privacy policy stays English unless separately translated; the URL is shared. If a
  reviewer asks, the letter says listing translation precedes UI translation.

## Phase B — the Core boundary (decision made; first code phase)

Core produces English display copy with no stable key beside it —
`CookieCaptureResult.Failed(string Message)` is the shape. Core is pure and cannot reach the
UI resource system, so **it hands over a key and the data to fill it, never prose**. The
in-repo precedent is `WebhookUrlVerdict`, which carries a `Kind` enum alongside its
`Message`: that is the rule now, for every new Core-produced string a viewer sees, and the
migration pattern for the existing ones.

Not every Core string is UI copy — the traps, named so nobody localizes them:

- **Discord webhook payloads** go to a channel, not a viewer. They stay English. Never
  localized to anyone's locale.
- **Phone alert envelopes:** the ntfy `Title: RoRoRo` header is static ASCII by transport
  design (HTTP header; account names deliberately never touch the envelope) — it stays
  exactly as it is. Alert title/body localization is a Phase C question, decided then.
- **Log lines and diagnostics** stay English; they are read by whoever debugs, not the user.

## Phase C — the WPF adapter, then the sweep (strictly after B)

Do not hand-sweep the 395. `/vibe-lingual` is the loop — extract, wire, translate, guard,
per-file backups, safe re-runs. It won't handle this XAML natively; it has an adapter seam
and reports not-yet-implemented rather than mangling anything. Build the **WPF adapter**
against that seam, then let the tool sweep.

Repo-specific hazards the sweep must respect, or the suite goes red:

- The copy fences (`PreferencesCopyTests` first-person rule, `WindowTitleConventionTests`,
  `AccessibleNamingFenceTests`) read source-tree strings today. They must learn to read the
  default-language resource baseline **in the same commit** the strings move — the standing
  fence ratchet rule.
- `ContrastPairGateTests` and the settings-reachability fence are content-independent but
  tree-reading; verify against the tree after the sweep, per the register rule.
- Themed/branded strings ("RoRoRo", "Imagine Something Else.", theme names per the findings
  register ruling) are proper nouns, not copy — excluded from extraction.

Only after C ships a genuinely translated UI does the manifest gain `<Resource>` entries for
those languages — never before (the rule above).

## Order

Core boundary (B) → WPF adapter → tool sweep (C). Phase A is independent of all of it and
can start as soon as the language set is picked.
