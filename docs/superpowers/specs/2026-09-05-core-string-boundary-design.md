# Core string boundary — keys and data out of Core, prose stays in the App

> **Phase B of** [`docs/store/localization-plan.md`](../../store/localization-plan.md)
> (Este's cross-app ruling, 2026-09-05). Spec written 2026-09-05 from a source survey of the
> actual sites, not the ~113-literal estimate. Status: **approved for build** — "let's spec
> out the core moves" is the brief.

## The problem, one sentence

Core composes English sentences a viewer eventually reads (`LaunchResult.Failed($"Failed to
obtain auth ticket: {ex.Message}")`), and Core is pure — it can never reach a resource
system, so every one of those sentences is a wall Phase C cannot translate through.

## The rule

**Core hands over a key and the data to fill it, never prose.** A Core result carries an
enum `Kind` plus structured fields (an exception's text, a URL, a count — data, not
sentences); the App owns every sentence a viewer reads, in exactly one place, so Phase C
converts that one place to `.resx` lookups and Core never changes again. The in-repo
precedent is `WebhookUrlVerdict`'s `Kind` — half the pattern; Phase B finishes it by making
the App stop reading such types' `Message` at all.

## Where App prose lives: one catalog

`src/ROROROblox.App/Localization/CoreMessageCatalog.cs` — a static class of pure formatting
methods, one per Core kind-enum (`For(LaunchFailureKind, string detail)` → string). No
lookup framework, no resources yet: plain `switch` expressions returning today's exact
English strings, unit-tested. This is deliberately boring — Phase C's sweep rewrites the
switch bodies into resource reads and touches nothing else. UI code (view-models, modals,
code-behind) calls the catalog; it never interpolates a Core kind into a sentence inline.

## The inventory (surveyed 2026-09-05)

| # | Site | Today | Move |
|---|---|---|---|
| 1 | `LaunchResult.Failed(string Message)` — composed in Core `RobloxLauncher` ×2 (auth-ticket failure, `Process.Start` failure) | The anti-pattern, in Core proper | Becomes `Failed(LaunchFailureKind Kind, string? Detail)`; catalog formats; the two `RobloxLauncher` sites pass kind + `ex.Message` as detail |
| 2 | `SessionHistoryStatus.Placeholder(outcome)` → `(Headline, Detail)` and `ClearFailed(string)` | Core composes prose from an enum it already has | The switch moves into the catalog keyed on `SessionHistoryOutcome`; `ClearFailed` becomes kind + detail |
| 3 | `WebhookUrlVerdict(Kind, NormalizedUrl, Message)` | Kind exists; Core still writes `Message` | App formats from `Kind` via catalog; `Message` property deleted (compile error finds every reader) |
| 4 | `PhoneCredentialVerdict(Kind, Normalized, Message)` | Same half-migrated shape | Same move as #3 |
| 5 | `CookieCaptureResult.Failed(string Message)` | Record lives in Core; all five composition sites are already App-side (`CookieCapture`, `CookieCaptureWindow`) | Becomes `Failed(CookieCaptureFailureKind Kind, string? Detail)`; the five sites pick kinds; catalog formats. Smallest risk — prose never lived in Core, only the shape invited it |
| 6 | Core exception types (`AccountStoreCorruptException`, `CookieExpiredException`, `GlobalBasicSettingsWriteException`, `AccountTransportException`, `InvalidThemeException`, `ClientAppSettingsWriteException`) | `.Message` is English | **Policy, not migration:** exception messages are diagnostics and stay English. Any UI surface showing one to a viewer leads with a catalog headline and demotes `ex.Message` to detail. Sites that show raw `ex.Message` as the whole story get flipped as they're touched, not swept |

### Explicitly NOT Phase B (and why)

- **Alert payload composition (`WebhookPayload`)** — the survey found the wrinkle: the same
  `payload.Title`/`payload.Body` feeds the Discord channel (never localized, per the plan's
  trap list), the tray toast (viewer!), AND the phone envelope (viewer!). Splitting
  composition per destination is real routing work, not a string move — it is **the first
  item of Phase C**, recorded here so nobody half-migrates it in B and breaks the channel
  exemption. Until then alert bodies stay English everywhere, which is what the shipped
  listings promise anyway.
- **Log lines, `ITrayService`'s string parameters** (a sink — its callers are App-side and
  already go through composed prose; they'll route through the catalog as their sources
  migrate), **plugin-facing strings** (the contract is versioned separately).
- **Any translation.** Phase B ships zero non-English words. It makes Core locale-agnostic;
  that's all.

## The fence

`CoreStringBoundaryFenceTests`, landing **first**, before any migration: it greps
`ROROROblox.Core` sources for the mechanical signatures of viewer prose (`string Message` on
records, `$"` interpolations returned through result types, the known prose helpers) and
asserts the finding set **equals** an explicit allow-list of today's six inventory rows —
the repo's ratchet idiom (`AccessibleNamingFenceTests`' equality ceiling). Each migration PR
deletes its row from the allow-list in the same commit; a new Core prose site fails the
fence the day it's written. Exit is an empty allow-list, and the fence stays forever as the
boundary's guard — the `UseCookies=false` lesson says the dangerous regressions are the ones
a green suite can't see.

## Ripples the repo already warns about

- Test fakes constructing `Failed("...")` (`MainViewModelTests` ×2, `AlertDispatcherTests`,
  `DiscordTestHarness`, `PhoneAlertTests`) stop compiling per type migrated — updated in the
  same commit, per the `IAppSettings` ripple convention.
- No settings, no modals, no windows, no RPCs — none of those fences move.
- `PhoneWiringFenceTests` source-reads the dispatcher factory; #3/#4 don't touch it, but run
  the suite after each type, not once at the end.

## Order of work (one PR per row is fine; one PR total is fine too)

Fence first → #1 `LaunchResult` (the only true in-Core composition, highest value) →
#2 `SessionHistoryStatus` → #3/#4 the two verdicts (smallest diffs) → #5 `CookieCaptureResult`
→ #6 policy pass over surfaces showing raw `ex.Message`. Suite green after each.

## Exit criteria

Fence allow-list empty and asserted; `CoreMessageCatalog` is the only place App composes
Core-sourced viewer prose, with today's exact English preserved byte-for-byte (no copy
changes ride along — the copy fences prove it); suite green on x64 + arm64. Then Phase C's
adapter has exactly one file to teach resources to.
