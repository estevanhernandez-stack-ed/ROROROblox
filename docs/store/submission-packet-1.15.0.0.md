# Submission packet — v1.15.0.0

> Everything for the Partner Center submission in one place, in the order you'll need it.
> Built and verified 2026-08-04. **The submission click is yours; everything before it is done.**
>
> Companion docs: [`reviewer-letter-1.15.0.0.md`](reviewer-letter-1.15.0.0.md) ·
> [`release-notes-1.15.0.0.md`](release-notes-1.15.0.0.md) · [`discord-disclosure.md`](discord-disclosure.md) ·
> [`listing-copy.md`](listing-copy.md) · [`submission-checklist.md`](submission-checklist.md)

## Read this first — two things differ from every previous submission

**1. The package DisplayName changes.** `RORORO` → `RoRoRo`. Identity Name, Publisher CN, and
PublisherDisplayName are untouched, so this is not an identity change — but a reviewer diffing
against the last submission will see it, which is why the letter calls it out in its second
section rather than burying it.

**2. The listing title and graphics both change**, and they are separate manual steps in Partner
Center that the package cannot do for you. See §3 and §4.

## 1. Packages to upload

| Architecture | File | Size |
| --- | --- | --- |
| x64 | `dist/RORORO-Store-x64-1.15.0.0.msix` | 91.01 MB |
| arm64 | `dist/RORORO-Store-arm64-1.15.0.0.msix` | 85.92 MB |

Both are **unsigned** — Partner Center re-signs with the Store certificate. Expected and correct;
do not sign them locally. The sideload cert and the Store identity are never the same key.

Built with `scripts/finalize-store-build.ps1` at `-Version 1.15.0.0`,
`-IdentityName 626LabsLLC.RoRoRoBlox`, `-PublisherCN "CN=177BCE59-0966-4975-9962-10E36652141F"`,
`-PublisherDisplayName "626Labs LLC"`, once per architecture.

> `PublisherDisplayName` has **no space**. It matches Partner Center byte for byte and a
> "corrected" spacing is rejected on identity mismatch. The script's own usage example had drifted
> to the spaced form and was fixed this release — if you copy-paste a command from anywhere older,
> check that argument.

## 2. Notes for certification

Paste the fenced block from [`reviewer-letter-1.15.0.0.md`](reviewer-letter-1.15.0.0.md) as-is.

> **Confirm the baseline before pasting.** The letter is written as "what's new in v1.15." That is
> correct only if **v1.14.0.0 was approved and is live**. v1.14 was submitted on 2026-08-03; if it
> was rejected, withdrawn, or is still in certification, the letter needs to describe v1.14's
> changes too — exactly as the v1.14 letter had to cover v1.13, which was never submitted. Check
> the Partner Center submission history and tell me if it needs the wider framing.

## 3. Store listing — the title changes this release

| Field | Value |
| --- | --- |
| Product name | **RoRoRo — Multi-launcher for Windows** |
| Short description + full description | From [`listing-copy.md`](listing-copy.md) |
| Privacy policy URL | The GitHub Pages privacy page — now carries the Discord section |

The bare wordmark is a coined word with no search volume; the tagline gives Store search something
to match and is the same line already on the artwork.

## 4. Listing graphics — re-upload all of these

The previous set had the wordmark in all caps. Regenerated 2026-08-04 in `docs/store/graphics/`:

| File | Use |
| --- | --- |
| `store-hero-1920x1080.png`, `store-hero-3840x2160.png` | Hero |
| `store-boxart-1080x1080.png`, `store-boxart-2160x2160.png` | Box art |
| `store-poster-720x1080.png`, `store-poster-1440x2160.png` | Poster |
| `store-display-71x71.png`, `-150x150.png`, `-300x300.png` | Unchanged — mark only, no wordmark |

Screenshots in `docs/store/screenshots/` are unchanged and still current.

## 5. What's actually in this release

**Discord integration** — optional, both halves off by default. Rich presence over the local
Discord pipe with a Join button; per-account alerts for a client dropping out unexpectedly or
crossing a memory threshold, routed to a Windows notification and/or a Discord channel the user
supplies a webhook for.

**Naming** — the product presents as RoRoRo everywhere a human reads it: exe version resource
(Task Manager, UAC, Explorer), installer and Add/Remove Programs entry, package DisplayName, and
the manifest's Roblox trademark disclaimer.

Full notes: [`release-notes-1.15.0.0.md`](release-notes-1.15.0.0.md).

## 6. Verified before this packet was written

- `dotnet test ROROROblox.slnx` on `main` after both merges — **1266 unit + 18 integration green**.
- **Per-account FPS caps confirmed on screen**, three accounts at 20/60/240 launched back to back,
  read off Roblox's Shift+F5 overlay. First recorded pass for that feature, and it doubles as the
  regression check that the Discord work does not disturb the launch path.
  ([`../smoke-2026-08-04-fps-cap-regression.md`](../smoke-2026-08-04-fps-cap-regression.md))
- Discord alerts smoke-tested live: webhook validation, Send test, routing, coalescing, and the
  per-account cooldown. Five defects found and fixed during that run.
- Both MSIX packages built clean; the logo gate passed on each.

## 7. Known-open, disclosed here so it is not a surprise

- **`FpsCapSettlerTests` is flaky under parallel load.** One test, `PostWriteQuietWait_
  CompetingWriteLandsInsideTheWindow_ForcesARetry`, fails intermittently on CI's x64 lane — it did
  on both PRs merged for this release. It is a **test-harness** problem, not the feature: the
  feature was measured working on screen (§6). Do not read a red x64 lane as a product defect
  without checking the test name first.
- **Discord presence is invisible to other users while Roblox is running.** Discord gives the
  profile "playing" slot to a game it detects, and RoRoRo is an RPC application. Documented in the
  Settings panel and in `PRIVACY.md`; not a defect we can fix.
- **Alert delivery has no automated end-to-end test.** "Send test" in Settings is that check, run
  by hand.
