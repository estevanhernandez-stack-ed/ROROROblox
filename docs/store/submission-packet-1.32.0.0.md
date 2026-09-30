# Submission packet — v1.32.0.0

Everything Partner Center asks for on this submission, in the order it asks. Certification last saw
**v1.31.0.0**, so this is a single version's delta.

> **DRAFTED 2026-09-29, not yet submitted.** Open before the owner's submit click: the listing
> rulings (section 2), the translation verifier run (section 3), and the package sizes and CI result
> (sections 1 and 7), which fill in when the build runs.

---

## Read this first — the app now relaunches a client on its own, when asked to

The finding a reviewer should take away: v1.32 adds **"Rejoin if it drops out"**, an opt-in per
account (never the main). When that account's client is still open but Roblox's presence API says
it has been out of the game for 3 minutes, RoRoRo ends that client and launches the account again
through the same launch the Launch button makes. At most 3 times an hour per account, then it
switches itself off and tells the user. It also makes **Join via friend** (disclosed with Squad
Launch in v1.11) apply to every launch path.

Verified against `git diff v1.31.0.0..main`, not assumed:

- **`Package.appxmanifest`: no committed change.** The Store build patches `Version` only.
- **One `.proto` change, additive:** `LaunchResult.reason_code = 4`. Contract version stays "1.0";
  `PluginHostService.cs` changes by 6 lines to copy it. No capability, consent or RPC file changed.
- **No input synthesis added.** No added line contains `SendInput`, `keybd_event`, `mouse_event`,
  `SetWindowsHookEx`, `PostMessage`, `SendMessage`, `WriteProcessMemory`, `OpenProcess`,
  `DllImport` or `LibraryImport`.
- **No new URL.** The only added `http` strings in production code are two XAML namespaces.
- **One new process action:** auto-rejoin's `RobloxInstanceStopper.StopAccount`, the routine Recycle
  has used since v1.12 (it ends the tracked client's process). Not the Stop button's graceful
  sequence; the first draft of the letter said it was, and was corrected before it was saved.
- **The signal is the presence poll the app already makes** (`PresenceService`, 25 s, since v1.5).
  No screen reading. Roblox's verification page is never read, clicked, answered or dismissed.

---

## 1. Packages

| File | Size | Architecture |
|---|---|---|
| `dist/RORORO-Store-x64-1.32.0.0.msix` | _fills in at build_ | x64 |
| `dist/RORORO-Store-arm64-1.32.0.0.msix` | _fills in at build_ | arm64 |

Both unsigned — Partner Center signs after upload. Ship **both**.

Identity: `626LabsLLC.RoRoRoBlox`, publisher `CN=177BCE59-0966-4975-9962-10E36652141F`, display name
`626Labs LLC`. Version `1.32.0.0`, fourth component zero as the Store requires.

## 2. Listing

**Audited 2026-09-29 against the fresh release notes, all four surfaces. Outcome: ONE FEATURE
ENTRY EARNED, which needs a merge because the field is full; ONE LONG-DESCRIPTION BULLET PROPOSED;
nothing made false. Nothing applied here** — `listing-copy.md` and `docs/index.md` are the
owner's to edit; the exact text is below.

**Short description — unchanged.** 197/200, no room. Auto-rejoin would not survive a trade against
anything already there, and "multi-launcher" is still the reason anyone installs.

**Product features — one entry EARNED, and the field is at 20/20.** Auto-rejoin is a new control
on every account row, and the thing the clan most wanted from a launcher left running overnight.
The 1.31 packet already named the pair to merge when this day came: the phone-alert and fan-out
entries. Proposed, both under 200:

Replace these two entries:

```
Phone alerts through Pushover or ntfy — an alt drops and your phone buzzes, even with Discord closed
Alerts fan out — desktop, Discord channels, and your phone in any mix, per alert
```

with one (153 characters):

```
Alerts on your phone through Pushover or ntfy, and to desktop and Discord channels in any mix per alert — an alt drops and your phone buzzes
```

and add (130 characters):

```
Rejoin if it drops out — an alt that's kicked or stuck outside the game is closed and joined again on its own, never your main
```

The six translated sheets owe the same merge and the same new entry. Join via friend does NOT earn
an entry: it's an existing feature now working on every path, already claimed under Squad Launch
and Friend Follow.

**Long description — one bullet proposed, after "Memory watchdog + Recycle":**

```
• Rejoin if it drops out. Turn it on per account, and an alt whose Roblox window is still open but has fallen out of the game (an idle kick, a failed join) is closed and joined again on its own. At most three times an hour, never your main, and your own Stop always wins.
```

- The **alerts bullet** lists "Drops, memory warnings, recycle completions, and an every-two-hours
  all-good mark". Not false (it lists, it doesn't say "only"). Optional: add ", auto-rejoin pauses"
  before "and an every-two-hours".
- The **privacy paragraph** is not made incomplete: auto-rejoin sends nothing and contacts no new
  host. Its only message is an alert to a destination the user already set up, which the paragraph
  already covers.
- The **trademark paragraph** still holds: RoRoRo launches the official client unmodified and never
  injects into or hooks it. Ending a process is not altering it.

**Hub page (`docs/index.md`, "What you get") — one bullet proposed**, after "Memory watchdog +
Recycle":

```
- **Rejoin if it drops out** — turn it on per account, and an alt that's been kicked or is stuck outside the game comes back on its own. Never your main, never more than three times an hour.
```

**`docs/PRIVACY.md` WAS edited** (it was made false, and it is not a listing surface):

- The memory-watchdog section said RoRoRo "never closes a client with an open game window without
  asking". Auto-rejoin, once opted into, does close one without a click (only one already out of its
  game). Reworded, with a pointer to the new section.
- **New section, "Auto-rejoin (v1.32 and later)":** what it reads (the presence check it already
  makes), what it does, what it never does (the verification page), what it sends (nothing; its
  alert goes where alerts already go), where the choice is stored (not exported).
- The Alerts trigger line said "Two things only". That was stale since v1.25. It now names all six
  kinds, including the new one, and says it was corrected.

## 3. What's new in this version

`docs/store/whats-new-1.32.0.0.md`, seven blocks: English plus fr, de, ru, pt-BR, pl, es. Each under
1,500 characters. UI names are the app's own translations from `Strings.<culture>.resx`.

**Verifier run: NOT YET DONE.** The dataset `whats-new-1.32.0.0.translations.json` is written with
`sourceCommit` pending. The run needs it at a pushed commit. 1.31 found one real defect in six, and
1.30 found three, so don't paste these unverified.

## 4. Notes for certification

`docs/store/reviewer-letter-1.32.0.0.md`; paste-ready text in
`docs/store/reviewer-letter-1.32.0.0.paste.txt` (3,590 characters; v1.31 was 3,315).

**Partner Center only.** `submit_write.py` never sends `notesForCertification`. Confirm it on the
Partner Center screen.

The letter leads with the auto-rejoin delta and answers the circumvention reading before a reviewer
has to raise it: the verification page is never touched, the signal is Roblox's own presence API,
the relaunch is the user's own launch, opt-in per account, capped. Section 6 is honest that a
reviewer can see the option and the dialog in a minute, but seeing a rejoin fire takes a signed-in
account idle about 20 minutes.

## 5. Age rating, privacy, screenshots

No change to the questionnaire (`docs/store/age-rating.md`): no new content type. Privacy URL
unchanged; **the policy it points at was updated** (section 2) and publishes with GitHub Pages on
merge. Screenshots unchanged: no screenshot shows a row menu or the new dialog.

## 6. GitHub release

Draft at tag `v1.32.0.0`, body taken from the release notes below the edit-log banner. Assets:
Velopack (`RORORO-win-Setup.exe`, `RORORO-win-Portable.zip`, `RELEASES`, `releases.win.json`, both
nupkgs), the signed compat pair, the signed `known-issues.json` pair, `plugins-catalog.json` (Ur Task
**0.9.1**), plus `RORORO-Sideload-x64-1.32.0.0.msix` and `dev-cert.cer`. **No cert rotation this
release.**

Publishing the draft is the owner's click.

**The plugin catalog already offers Ur Task 0.9.1** on the current Latest release (v1.31.0.0). The
catalog had been offering 0.8.0, which silently stalls on schema-4 macros, since before 0.9.0
shipped. The 1.32 release carries the same file.

## 7. Verification

- **Unit tests:** 2,454 at v1.31.0.0 → **2,600** at `main` 5b5c9e9 (PR #223), plus 27 harness tests
  with 1 skipped by design. CI green on x64 and native arm64.
- **Live, 2026-09-29, on the branch build:**
  - A flagged account followed the main from the Launch button.
  - The dialog appeared with the main offline, and Cancel launched nothing.
  - A flagged account that hit the verification page and was idle-kicked was held while the main was
    also down, then rejoined by following on the first tick after the main returned. The page was
    never touched.
  - A plugin `StopAccounts` stayed stopped.
  - **Not seen live:** the unflagged rejoin, because Roblox never idle-kicked that alt in 55 minutes.
    It is unit-tested and shares every step with the flagged rejoin except the target.
- **Sideload MSIX:** _signature check fills in at build._
