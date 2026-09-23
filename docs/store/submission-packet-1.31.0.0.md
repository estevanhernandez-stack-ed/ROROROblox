# Submission packet — v1.31.0.0

Everything Partner Center asks for on this submission, in the order it asks. Certification last saw
**v1.30.0.0**, which certified and published on 2026-09-21, so this is a single version's delta.

> **DRAFTED 2026-09-23, not yet submitted.** Nothing open before the owner's submit click. The
> verifier run on the what's-new translations (section 3), the package sizes and the CI result were
> all filled the same day.

---

## Read this first — one addition, and it only reads

The finding a reviewer should take away: v1.31 adds **one read-only page and a one-time notice**, fed
by a **signed text file from a host the app already contacts**. It sends nothing about the user,
installs nothing, runs nothing, and its buttons only move you around inside RoRoRo.

- **Known Roblox issues** is the seventh page of the Tools window (Ctrl+7; also a Tools menu item,
  with a count when entries apply to this PC). Each entry: what happens, what to do, and — where
  RoRoRo has a feature for it — a button to that feature.
- **A serious entry raises one notice** at the top of the main window (See known issues / close),
  closed once per entry. It yields to the contested-singleton warning.
- **The list is `known-issues.json` + `.sig` from `releases/latest/download/`**, the same address as
  `roblox-compat.json`. Fetched after the startup gate and every four hours; verified against the
  already-pinned compat key before parsing; 256 KB cap; last verified copy kept.

Verified against `git diff v1.30.0.0..main`, not assumed:

- **`Package.appxmanifest`: no committed change at all.** The Store build patches `Version`, so the
  uploaded package differs from v1.30's by that attribute alone. Same `runFullTrust`, languages,
  protocols, startup task.
- **No `.proto` in the diff**, and no file matching `RpcMethodCapabilityMap`, `PluginCapability`,
  `PluginHostService` or consent. A plugin built for v1.30 runs untouched.
- **One added production URL**: `KnownIssuesFeed.FeedUrl` on `github.com/…/releases/latest/download/`.
  Every other added URL is a `devforum.roblox.com` link in a test, or a XAML namespace.
- **No input synthesis added.** No added line contains `SendInput`, `keybd_event`, `mouse_event`,
  `DllImport`, `LibraryImport`, `SetWindowsHookEx`, `PostMessage`, `SendMessage`,
  `WriteProcessMemory` or `OpenProcess`.
- **Links are https-only** (`KnownIssuesParser` refuses anything else) and open in the browser on
  click only. **Buttons are a fixed compiled set**: three keys, two destinations (Settings › memory
  watchdog, the main window); an unknown key shows text with no button.

Unchanged and worth restating: the binary synthesizes **no input**, injects into nothing, gathers
**no third-party game data** itself, and contacts **no new host**.

---

## 1. Packages

| File | Size | Architecture |
|---|---|---|
| `dist/RORORO-Store-x64-1.31.0.0.msix` | 106.7 MB | x64 |
| `dist/RORORO-Store-arm64-1.31.0.0.msix` | 100.5 MB | arm64 |

Both unsigned — Partner Center signs after upload. Ship **both**.

Identity: `626LabsLLC.RoRoRoBlox`, publisher `CN=177BCE59-0966-4975-9962-10E36652141F`, display name
`626Labs LLC`. Version `1.31.0.0`, fourth component zero as the Store requires.

## 2. Listing

**Audited 2026-09-23 against the fresh release notes, all four surfaces: short description, long
description, product features, and the hub page. Outcome: ONE FEATURE ENTRY EARNED, one hub bullet
proposed, one privacy question for the owner. Nothing applied here** — `listing-copy.md` and
`docs/index.md` are the owner's to edit; the exact text is below.

**Carried from the 1.30 audit, still open and first in line:** the frame-rate feature entry
(`Per-account frame-rate caps, raised, lowered or lifted entirely per client`) is live in `en-us`
only; the six translated sheets serve 18 entries, not 19 (`listing-copy.md`, features preamble).
Translating that one entry into six sheets closes it. Worth doing on this submission, since the new
entry below needs the same six-sheet pass anyway.

**Short description — unchanged.** 197/200, no room, and Known Roblox issues is not the reason
anyone installs a multi-launcher. It would not survive a trade against any word already there.

**Product features — one entry EARNED.** A new user-facing page with its own menu item and notice,
answering a question the clan has actually asked twice. Proposed, 125 characters:

```
Known Roblox issues — Roblox-side problems, what to do, and the RoRoRo feature that helps, kept current without an app update
```

**This takes the 20th and last slot** (19 of 20 used in English today). After it, any new entry
means merging or retiring one — the two alert-fan-out / phone-alert entries are the obvious pair.
The six translated sheets need it too (they would go 18 → 19, or 19 → 20 with the frame-rate fix).

**Long description — the feature does NOT need a bullet; one question for the owner.**

- The "One tools window" bullet names six pages ("Games, Settings, History, Diagnostics, Plugins and
  About"). There are seven now. The sentence is not false — it lists, it does not say "only" — so
  this is optional. If wanted, insert `Known Roblox issues` before `and About`, in all seven sheets.
- **Privacy paragraph: not made incomplete by 1.31, but it has a pre-existing gap 1.31 widens by one
  file.** It says nothing leaves your machine except Roblox-side calls and alerts you set up. It has
  never mentioned the GitHub fetches (update check, compat feed), and 1.31 adds a third of the same
  kind: same host, a plain download, nothing about the user sent. That is unlike v1.25, where a new
  destination received the user's own content. **Owner's call.** If it is to be closed, the
  proposed replacement for the sentence that begins "No telemetry. No analytics. Nothing leaves your
  machine except …" (the rest of the paragraph unchanged; six sheets owe the same edit):

  ```
  No telemetry. No analytics. Nothing about you leaves your machine except the Roblox-side calls during launch — the same calls Roblox.com makes from your browser — and, only if you set them up yourself, alerts to your own Discord webhook or to the phone push service you chose (Pushover or ntfy). RoRoRo also downloads its own signed files from its GitHub releases — updates, the Roblox compatibility settings, and the known Roblox issues list — and sends nothing about you to get them.
  ```

**Hub page (`docs/index.md`, "What you get") — one bullet proposed**, after "Memory watchdog +
Recycle":

```
- **Known Roblox issues** — problems in Roblox itself, what to do about them, and a button to the RoRoRo feature that helps. A signed list that stays current without an app update.
```

**`docs/PRIVACY.md` WAS edited** (it was made incomplete, and it is not a listing surface): the
TL;DR GitHub bullet names the known-issues list; the `github.com` Releases row names
`known-issues.json`, its signature check, and the four-hour refresh; the storage table gains
`known-issues.cache.json` + `.sig` and `known-issues-dismissed.json`. The `api.github.com` row, which still said RoRoRo "does not download
or install updates on its own" (false since auto-update was wired on 2026-09-05), was corrected in
the same commit.

### Owner's rulings, 2026-09-23, and what was applied

Applied to all seven sheets: feature entry 20 as drafted above; the privacy sentence replaced as
drafted; `Known Roblox issues` added to the tools-window page list; the frame-rate entry translated
into the six sheets (each now 20 features). The hub bullet was declined.

**Listing verifier, at `a44e5ad` then `35035a1`** (long description + features, six languages; the
other four fields were already approved and unchanged). The new text drew one finding: French dropped
"that sits beside your accounts". Fixed. The run also surfaced defects in long-live text, fixed in
`35035a1`: Spanish dropped "while your accounts run"; Russian and Polish turned "silence means
something's wrong" into "check your PC"; German lost the Uptime marks name and "with Discord closed";
Russian said a client "costs" RAM like a price, and its tray bullet lacked its heading.

**Overruled:** five findings that the tray "double-click main launch" was mistranslated as
launching the main account. It does launch the main account (`App.ActivateMainFromTray` runs
`StartMainCommand`); the translations are right and the English is the vague one. **Left as minor,
not chased:** a second pass raised new style points on text it had passed on the first (the Uptime
marks name in ru and pt-BR, "by name" in Spanish's plugin sentence). The verifier is not
deterministic across passes, and these are wording, not claims.

## 3. What's new in this version

`docs/store/whats-new-1.31.0.0.md`, seven blocks — English plus fr, de, ru, pt-BR, pl, es. Each under
1,500 characters (English 899, longest French 1,140).

**Verified by the translation verifier at commit `5628edc`, both shapes, 18 pairs: 5 approve clean,
13 revise. One real defect, fixed; the rest are two known false positives, overruled.**

- **Fixed:** Polish section 2 said "to działa *nowa* funkcja" (the *new* feature), a word the English
  does not have. Now "to działa ta funkcja".
- **Overruled, 6 pairs (section 1):** the verifier wants "Tools" left in English "because the app
  interface is in English". It is not, since UI localization wave 1: each translation uses that
  culture's own `MainWindow_Tools` value (Outils, Werkzeuge, Инструменты, Ferramentas, Narzędzia,
  Herramientas), which is what a reader in that language sees on the button. The verifier's
  RoRoRo profile predates UI localization; worth an issue on the verifier repo.
- **Overruled, 6 pairs (section 3):** the "page is in your language; descriptions are in English for
  now" line has no English counterpart. Deliberate and precedented (v1.29, v1.30 certified with the
  same call); an English reader does not need telling that English text is English.

The Polish fix was re-run on its own at `f888737`: section 2 approves clean.

UI names in the translated blocks are the app's own (`MainWindow_KnownRobloxIssues`,
`MainWindow_Tools_2` from `Strings.<culture>.resx`), not re-translated.

## 4. Notes for certification

`docs/store/reviewer-letter-1.31.0.0.md`; paste-ready text in
`docs/store/reviewer-letter-1.31.0.0.paste.txt` (3,240 characters, shorter than v1.30's 3,927).

**Partner Center only.** `submit_write.py` never sends `notesForCertification`, and the field read
back empty through the API on v1.28 even after the letter was pasted — confirm it on the Partner
Center screen or not at all.

Section 6 says, for the first time in four versions, that the reviewer **can** see the change on a
fresh install: the list is already live on the v1.30.0.0 release, and the serious entry applies to
any PC.

## 5. Age rating, privacy, screenshots

No change to the questionnaire (`docs/store/age-rating.md`): the new content is Roblox troubleshooting
text written by the publisher, not user-generated. Privacy URL unchanged; **the policy it points at
was updated** (section 2) and publishes with GitHub Pages on merge. Screenshots unchanged.

## 6. GitHub release

Draft at tag `v1.31.0.0`, body taken from the release notes below the edit-log banner. Assets:
Velopack (`RORORO-win-Setup.exe`, `RORORO-win-Portable.zip`, `RELEASES`, `releases.win.json`, both
nupkgs), the signed compat pair, **the signed `known-issues.json` pair (new, attached by
`release.yml` and guarded by a test)**, `plugins-catalog.json`, plus
`RORORO-Sideload-x64-1.31.0.0.msix` and `dev-cert.cer`. **No cert rotation this release.**

Check the draft carries `known-issues.json` + `.sig` before publishing: the app reads only from the
release marked Latest, so a 1.31 release without them silences the page for everyone, 1.31 users
included.

Publishing the draft is the owner's click.

## 7. Verification

CI on `main` at `79fd599`, x64 and native arm64: **2,454 unit + 27 harness pass, 1 harness skip by
design, 0 failures.** v1.30 recorded 2,329 unit + 27 harness. Sideload MSIX signed by
`CN=177BCE59-0966-4975-9962-10E36652141F`, matching the packed manifest's Publisher.
