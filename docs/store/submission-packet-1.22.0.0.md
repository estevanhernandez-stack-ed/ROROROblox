# Submission packet — v1.22.0.0

Everything needed to complete the Partner Center submission in one pass, in the order the form asks
for it. Internal record; nothing here is pasted into Partner Center except where marked.

---

## Read this first — routine submission, one wrinkle

Certification last saw **v1.21.0.0**; this is **v1.22.0.0**. A normal single-version jump — no gap
to explain this time.

The one wrinkle: the reviewer letter deliberately **restores the v1.4.0.0 letter's precise
plugin-install language**. The v1.21 letter said the Store edition has "no in-app download or
install path," which overstated the certified position — v1.4's letter described the
paste-a-GitHub-URL install flow explicitly and was certified with it. This release adds a
capability to the plugin system (GetAccounts) whose first consumer users will install through that
flow, so the letter goes back to saying exactly what the app does. See the framing note in
[`reviewer-letter-1.22.0.0.md`](reviewer-letter-1.22.0.0.md).

## 1. Packages to upload — both of them

| | x64 | arm64 |
|---|---|---|
| File | `dist/RORORO-Store-x64-1.22.0.0.msix` | `dist/RORORO-Store-arm64-1.22.0.0.msix` |
| Size | 91.19 MB | 86.08 MB |
| `ProcessorArchitecture` | `x64` | `arm64` |

Shared by both: tag `v1.22.0.0` on `main`, identity `626LabsLLC.RoRoRoBlox`, publisher
`CN=177BCE59-0966-4975-9962-10E36652141F`, `PublisherDisplayName` `626Labs LLC` (no space),
version `1.22.0.0`, `runFullTrust` and nothing else, **unsigned** — Partner Center signs on
upload.

**Verified by reading the packed `AppxManifest.xml` back out of both `.msix` files** after packing
(identity, publisher, version, architecture, publisher display name, single capability), same
discipline as the 1.21 packet. The AArch32 advisory Partner Center shows still does not apply —
nothing here declares 32-bit ARM or x86.

## 2. Notes for certification

Paste the fenced block from [`reviewer-letter-1.22.0.0.md`](reviewer-letter-1.22.0.0.md) verbatim.

It leads with the one disclosure-surface change — the new capability-gated GetAccounts plugin RPC
(contract 0.9.0): payload is display name + Roblox user id + is-main and nothing else, no
cookies or credentials ever cross the plugin boundary, consent-gated and enforced per-call. Then
the precise 10.2.2 restatement, then everything else compressed to a line, then the
unchanged-from-1.21 block. Test count in the letter (1,845) is the verified number from this cut.

## 3. What's new in this version — the public field

Paste the fenced block from [`whats-new-1.22.0.0.md`](whats-new-1.22.0.0.md). **Different field
from step 2** — Store listings → [language] → "What's new in this version." This is the step that
historically gets skipped.

The copy deliberately omits the plugin contract bump and the MCP connector — plugin-author and
power-user material that the reviewer letter carries; a public listing field saying "an AI can
drive this app" invites a certification conversation for no customer benefit.

## 4. Screenshots and listing

- The existing 10-screenshot set in [`screenshots/`](screenshots/) carries over. **Known
  staleness:** `03-about` through `07-plugins` show those surfaces as the pre-1.22 pop-up windows;
  in 1.22 they are pages of the tools window, so the chrome differs. Content is otherwise
  accurate. Refresh during the F-098 capture-ui walk if it happens before the submission click;
  not blocking.
- Long description, keywords: [`listing-copy.md`](listing-copy.md), unchanged this release.
- Privacy policy URL: the GitHub Pages render of `docs/PRIVACY.md`, unchanged — no new data
  surface this release.

## 5. What is actually in this release

- **Six utility windows became one non-modal tools window** (Games, Settings, History,
  Diagnostics, Plugins, About as pages; every prior door routes there).
- **Settings apply live** — per-account alert muting and idle threshold previously required a
  restart to reach alert routing; the settings record now has a single in-process owner.
- **First keyboard shortcuts**, generated from one table with tests holding the list, the menu
  hints, and the bindings together.
- **Plugin contract 0.9.0 — GetAccounts** behind the new consent capability; first consumer is
  the separately-distributed open-source `rororo-ur-mcp` connector.
- **RAM headroom warning calibrates from measured local client memory** (local process metrics,
  read-only, never transmitted).
- **UI Automation naming sweep** — 86 unnamed controls at audit; everything the app composes is
  now named, with a test fence.

## 6. Verified before this packet was written

- Full solution green locally on the cut: **1,821 unit + 24 integration passed, 1 skipped**
  (the skip is a single environment-conditional case, not the F-105 render gates — those are back
  on CI as of this cycle).
- Packed manifest identity, version, and capability read back out of both `.msix` files.
- `ROROROblox.PluginContract` **0.9.0 is live on nuget.org** (published via trusted publishing
  this cycle) — the 1.21 packet's "contract package absent" known-open is resolved.

## 7. Known-open, disclosed here so it is not a surprise later

- **`submission-checklist.md` remains 0 of 30 ticked.** Same warning as the 1.21 packet: it is a
  first-submission bringup template nobody maintained; do not use it as a pre-flight gate.
  Ticking it against reality is still owed.
- **Screenshot staleness** per §4 — five frames show pre-shell chrome.
- **F-098** (packaging/capture audit) is the last open register row and is planned to close
  during this submission's Store-run verification.
