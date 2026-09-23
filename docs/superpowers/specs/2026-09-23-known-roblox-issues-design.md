# Known Roblox issues — design

**Status:** approved by the owner 2026-09-23. Two corrections made the same day while planning, from
reading the code rather than the conversation: the Roblox version source (§4) and how the notice sits
in the main window's notice area (§3). Both are marked *Corrected while planning*.
**Scope:** RoRoRo (ROROROblox repo). No plugin-contract change, no new network host.

## Why

Twice now a clan member has said "I think RoRoRo has a problem" when the fault was Roblox's.

The first time it was a memory leak. Working out that it was Roblox — adding up the cost of many
accounts, RAM allocation and time — is what led to RoRoRo's memory watchdog and Recycle. The second
time, on 2026-09-22, was a Roblox window that froze when dragged or resized until the Windows key was
pressed. A diagnosis on the affected PC traced the freeze into Roblox's own render call inside the
graphics driver, on Roblox 0.739, and matched it to public Roblox DevForum reports.

Both times the answer existed but had no place to live inside RoRoRo. This feature gives it one: a
list of current known Roblox issues, each with a workaround and, where RoRoRo has a feature that helps,
a pointer to that feature. The list can be updated without shipping a new app version.

## What the owner asked for, and what is assumed

**Said:**

- A place in the app listing current known Roblox-side issues, updatable without an app release.
- It should both answer "is it RoRoRo or Roblox?" and warn people when an issue is serious.
- Where RoRoRo mitigates an issue — the memory features for a leak — the note about that feature
  lives next to the issue for as long as the issue exists.
- Entries may state which Roblox versions they affect, but do not have to.
- A serious issue raises a one-time, dismissible notice at the top of the main window.
- The page is called **Known Roblox issues**.

**Assumed, and confirmed during the design conversation:**

- Issues listed are Roblox's, not RoRoRo bugs.
- The owner writes the entries; they reach Store and direct-download installs alike.
- The file is signed, because it is text and links shown to every user.

## Decisions taken while designing

| Question | Decision |
|---|---|
| What is the feature for? | Both: a list people consult, and a notice for serious issues. |
| Do entries name Roblox versions? | Optional per entry. With versions, the notice follows the installed version; without, the entry stands until removed. |
| How loud is the notice? | A notice at the top of the main window, dismissed once per entry, plus a count on the menu item. No Windows toast, no phone alert. |
| How does the file reach users? | A separate signed file on the latest GitHub release, published by the existing `compat` workflow. |
| What is it called? | Known Roblox issues. |

Two transports were rejected. Folding entries into `roblox-compat.json` would make every wording fix
an edit to the file that carries the mutex name, so a malformed entry could block a mutex update. An
unsigned file on GitHub Pages would let anyone who can change that file show every user a fake "fix,
download here".

## 1. The file and publishing

**Location in the repo:** `known-issues.json` at the repo root, beside `roblox-compat.json`.

**Shape:**

```json
{
  "schemaVersion": 1,
  "issues": [
    {
      "id": "window-freeze-on-drag-2026-09",
      "postedAt": "2026-09-23",
      "notify": true,
      "title": "The Roblox window freezes when you drag or resize it",
      "symptom": "Dragging or resizing a Roblox window stops responding until you press the Windows key.",
      "workaround": "Press the Windows key, then Escape. Updating Roblox helps.",
      "rororoHelps": { "feature": "fps-caps", "text": "Set this account's frame-rate cap below your monitor's refresh rate." },
      "links": [ { "label": "Roblox DevForum thread", "url": "https://devforum.roblox.com/t/4032374" } ],
      "robloxVersions": { "fixedIn": "0.740" }
    }
  ]
}
```

The entry above illustrates the shape. Its `rororoHelps` claim (frame-rate caps below the refresh
rate) comes from DevForum threads and one PC's diagnosis, and should be checked before it is
published as a real entry.

| Field | Required | Meaning |
|---|---|---|
| `schemaVersion` | yes | `1`. A higher number is a format this app does not know; see §2. |
| `issues` | yes | May be empty. An empty list is how every issue is retired. |
| `id` | yes | Stable, unique. Dismissals are remembered against it, so a new issue needs a new id. |
| `postedAt` | yes | `yyyy-MM-dd`. |
| `notify` | yes | `true` makes the entry eligible for the main-window notice. |
| `title`, `symptom`, `workaround` | yes | Plain text, English. |
| `rororoHelps` | no | `{ "feature": <key>, "text": <string> }`. Keys in §4. |
| `links` | no | `[{ "label", "url" }]`. `url` must be `https://`. |
| `robloxVersions` | no | `{ "from"?: <version>, "fixedIn"?: <version> }`, at least one of the two. |

Unknown fields are ignored, so a later format can add fields that older apps skip.

**Validation — one checker, two callers.** The rules that decide whether a document is valid live in
Core as a single validator. `tools/CompatSigner` references Core and runs it before signing; the app
runs it after verifying the signature. The workflow therefore cannot sign a file the app would
reject. The validator refuses: a missing required field; a duplicate `id`; a non-`https` link; a
version that is not two to four dot-separated numbers with a three-digit second number, which is how
Roblox writes versions (`0.740`, `0.740.0.7400838`), so `0.74` is refused because it reads as a
different number from `0.740`; and a `robloxVersions` object with neither bound.

**Publishing.** Edit `known-issues.json`, commit, then Actions → **compat** → Run. The workflow gains
steps to validate and sign `known-issues.json` and to upload it with its `.sig` beside
`roblox-compat.json`, to the release marked Latest.

**Every new release carries it.** `release.yml` attaches `known-issues.json` and its `.sig` on every
tag, as it already does for `roblox-compat.json` and `plugins-catalog.json`. The app reads only from
the latest release, so a release without the file would silence the page the day it ships.

**Signing key.** The existing `ROBLOXCOMPAT_SIGNING_KEY` signs both files, and the app verifies both
against the key it already pins (`RobloxCompatSigningKey`). A second key would add a secret to rotate
without separating any real trust: both files are published by the same person from the same repo.
`RobloxCompatSigningKey`'s own comment states "one key per trust surface", and it means separate
*products* — it was written to keep this feed apart from 626-mod-launcher's. Two feeds of one product
from one repo are one trust surface; the comment is updated to say it now signs both.

## 2. In the app: download, cache, failure

**`KnownIssuesFeed`** in `ROROROblox.Core`, modelled on `RobloxCompatChecker`: a typed `HttpClient`
with exactly one applicable constructor taking `ILogger<KnownIssuesFeed>` (the rule that
`TypedHttpClientRegistrationTests` guards), the `RORORO/<version>` User-Agent, and a fetch of
`releases/latest/download/known-issues.json` plus `.sig`. The signature is verified against the raw
bytes before anything is parsed.

**No new network destination.** The file is fetched from the same GitHub release host the app
already contacts for `roblox-compat.json`. Nothing about the user is sent.

**Size limit.** A response over 256 KB is refused before its signature is checked.

**Cache.** After a download verifies, the exact bytes and signature are written to
`%LOCALAPPDATA%\ROROROblox\known-issues.cache.json` and `.cache.json.sig`. At startup the cache is
loaded first and **verified again** before use, so the page has content immediately and offline,
and a hand-edited cache is ignored. Store and direct-download builds share this folder, which is
harmless: the file is identical for both.

**When it downloads.** Once after the startup gate has been answered, on the thread pool, next to
plugin autostart, and so outside the load-bearing theme → mutex → gate sequence. Then every four
hours while RoRoRo runs, on a `TimeProvider`-driven timer so tests can advance time. Results reach the
UI through `IUiDispatcher`.

**Failure keeps what you had:**

| What happens | What the user sees | Log level |
|---|---|---|
| No network, timeout, 404 | The last verified entries | Debug |
| Signature does not verify | The last verified entries | **Warning** |
| Fails validation, or `schemaVersion` newer than supported | The last verified entries | Warning |
| Oversized response | The last verified entries | Warning |
| Valid document with an empty `issues` list | An empty page | Information |

**It logs what it did.** The level in the table above is for the line describing the failure itself.
Separately, every download, whatever its outcome, ends with one Information summary line, such as
"Known issues: 3 entries, 1 with a notice, from the release" or "…, kept 3 cached entries: signature
did not verify". At one attempt every four hours that is a handful of lines a day. The metric path showed on 2026-09-21 what a path that writes nothing costs: no
incident on it could be reconstructed.

## 3. What people see

**The page.** A new Tools page, **Known Roblox issues**. `ShellPage` gains `KnownRobloxIssues`,
appended **last** in `RailOrder`. The rail binds Ctrl+1…Ctrl+N by position, so the new page takes
Ctrl+7 and every existing page keeps its shortcut.

Each entry shows:

- the title, the posted date, and a small **Roblox** tag, so it reads as Roblox's problem at a glance;
- the symptom, then the workaround;
- a **RoRoRo can help** block when `rororoHelps` is present and its key is known, with a button that
  goes to that feature (§4);
- the links, opened in the browser through `ShellOpener`, with the full address visible;
- a version line when `robloxVersions` is present: "Your version (0.739) is affected", "Fixed in 0.740
  — you have 0.740", or "Couldn't read your Roblox version".

Sort order is `notify` entries first, then newest `postedAt` first. With no entries the page says "No
known Roblox issues right now." In every state it shows when the list was last checked, such as
"Checked 2 hours ago": that line is what tells people an empty page is current rather than broken.

**The notice.** The existing status notice area at the top of the main window shows the newest entry
that has `notify: true` and applies to this PC (§4), and that has not been dismissed. It has two
buttons: **See known issues**, which opens the page, and close. When more than one entry applies it
says "2 known Roblox issues" instead of one title. Closing it records every entry id it was showing;
only an id not seen before brings it back.

**Existing warnings take precedence.** *Corrected while planning:* the notice area is not one slot but
a stack of independently collapsing rows (status text, idle summary, the contested-singleton warning,
the frame-rate-cap warning), so nothing displaces anything by appearing. The known-issues row is
therefore **suppressed while the contested-singleton warning is showing**, because that warning is
about RoRoRo working at all and must not compete for attention; alongside the other rows it simply
stacks. It is added to the area's collapse condition so the strip still disappears when every row
is empty.

**The count.** The menu item for the page shows how many entries apply to this PC. Zero shows no count.

**Dismissals** are kept in `%LOCALAPPDATA%\ROROROblox\known-issues-dismissed.json`, a list of ids. It
is state, not a preference: putting it in `SettingsBlob` would bring the new-setting obligations (an
`IAppSettings` member, four private test fakes, `SettingsReachabilityTests`) for a value no control
edits. Ids that no longer appear in the feed are pruned when the file is next written.

**A known limit.** The notice lives in the main window. Someone who keeps RoRoRo in the tray sees it
the next time they open the window. That follows from choosing a window notice over a toast, and is
recorded here so it is a decision rather than a discovery.

## 4. Links, versions, languages

**Feature keys, version 1:**

| Key | Where the button goes |
|---|---|
| `memory-watchdog` | Tools › Settings, scrolled to the memory watchdog switch |
| `fps-caps` | The main window, brought forward. The frame-rate cap is per account on each row |
| `recycle` | The main window, brought forward. Recycle is a button on each account row |

An unknown key shows its `text` with no button, so an entry written for a newer app still reads on an
older one.

**Versions.** *Corrected while planning:* the version compared is the one a launch **will run** —
`RobloxCompatChecker.GetHandlerRobloxVersion()`, the `FileVersion` of the binary the `roblox-player`
handler points at — falling back to `GetInstalledRobloxVersion()` only when the handler read returns
null (absent, strap-owned, or unreadable). The installed read orders version folders by write time,
which F-104 measured as a coin flip during a launch batch; it answers "what is on disk", while this
feature asks "what will I get". Both live in Core, and this feature adds no third copy of either.

- An entry **applies** when it has no `robloxVersions`; or when the installed version is at or above
  `from` (if given) and below `fixedIn` (if given); or when the installed version cannot be read, in
  which case it applies and the page says so.
- Versions compare as `System.Version` after normalising. *Corrected while planning:* Roblox's binary
  reports its `FileVersion` as `0, 740, 0, 7400927` — commas and spaces, read off every version folder
  on the owner's PC on 2026-09-23 — and `Version.TryParse` rejects that form outright. A small Core
  helper strips the spaces and turns commas into dots first, so a PC on `0, 740, 0, 7400927` reads as
  `0.740.0.7400927` and is not affected by an entry with `fixedIn: "0.740"`.
- The same raw string reaches `RobloxCompatChecker.CheckAsync`, which calls `Version.TryParse` on it
  directly, so its version-drift banner can never fire. That is a pre-existing defect, recorded here
  and **deliberately not fixed by this feature**: `knownGoodVersionMax` is still `0.729.24`, so
  repairing the parse would show a drift banner to every user on 0.740 the day it shipped. It needs its
  own decision.
- The version decides **only the notice and the count**. The page always lists every entry, because
  the running client can be older than the installed one: the frozen window on 2026-09-22 was 0.739,
  launched from Chrome, while 0.740 was installed.

**Languages.** The page's headings, buttons, empty state and notice are translated into all seven UI
languages through the existing string catalog pipeline, with product nouns (RoRoRo, Roblox) left in
English. Entry text is English. In a non-English UI a line under the page title says the issue
descriptions are in English. This is the same call v1.29 and v1.30 made for alert sentences, both of
which certified.

## 5. Testing and guards

### Feed and cache

- A download whose signature verifies is parsed and cached; one that fails is never parsed or cached.
- Changing one byte of the cached file makes it rejected at load. This test is checked by
  deliberately breaking verification and confirming the test then fails.
- Each row of the failure table in §2 has a test, including the log level it writes.
- An oversized response is refused before verification.
- An empty `issues` list clears the page; a newer `schemaVersion` keeps the cached entries.

**Validator** — one set of cases run against the shared Core validator: each refusal in §1, plus a
valid document with every optional field.

**Versions** — `from` only, `fixedIn` only, both, neither, installed exactly at `fixedIn`, Roblox's
four-part installed form against a two-part bound, and an unreadable installed version.

**Notice** — picks the newest applicable `notify` entry; switches to the count wording when several
apply; stays closed after closing; returns for a new id; yields to the contested-singleton warning.

**Feature keys** — each known key routes where §4 says; an unknown key shows text with no button.

### Guards kept, and one added

- `TypedHttpClientRegistrationTests` resolves `KnownIssuesFeed`.
- `AccessibleNamingFenceTests` keeps its unnamed ceiling (asserted as equality): every new button is
  named.
- `ButtonRankFenceTests` and `ButtonStateGateTests`: new buttons use the existing styles, with no
  self-painting and no Opacity hover.
- `ThemedStatusColourTests` and `TypeLadderFenceTests`: no new colour literals and no new raw font
  sizes. Themed brushes are referenced with `DynamicResource` only.
- New strings exist in all seven culture files.
- **New:** a test that fails if `release.yml` stops attaching `known-issues.json` and its `.sig`. No
  such guard existed for `plugins-catalog.json`, which is how it sat at Ur Score 0.3.5 for months.
- Any test that builds a `MainViewModel` disposes the window decorator and calls
  `StopPeriodicRefresh()`, as every such test must.

## Out of scope

- RoRoRo detecting an issue on its own. The memory watchdog already covers the leak case; general
  detection is its own project.
- A Windows toast, or any phone or Discord alert, for a known issue.
- Translated entry text.
- Per-account version matching. The installed version stands for the PC.

## Risks and open items

- **The first real entry's mitigation needs checking.** The frame-rate-cap advice for the window
  freeze rests on DevForum reports and one machine; confirm it before publishing that entry.
- **Running clients can be older than the installed version.** The page lists every entry for this
  reason; only the notice and the count follow the installed version.
- **A new release without the file silences the page.** Mitigated by the `release.yml` attachment and
  its guard test.
