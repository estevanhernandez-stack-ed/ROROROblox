# Submission packet — v1.37.0.0

Everything Partner Center asks for on this submission, in the order it asks. Certification last saw
**v1.36.0.0**, so this is a single version's delta, and a small one: 47 files and +2,267/−40 across the
repository, 28 files and +1,604/−14 under `src/`. Four changes, all from the 2026-11-10 evidence sweep
(`docs/cycles/2026-11-10-growth-and-quality/board.md`).

> **DRAFTED 2026-11-10, not yet submitted.** Sections 6, 7 and 8 are filled as each step happens. The
> Store write itself needs the owner's explicit word in the session.

---

## Read this first: one new interop call, one registry read, one Store link

The reviewer letter leads with them rather than letting a reviewer find them:
[`reviewer-letter-1.37.0.0.paste.txt`](reviewer-letter-1.37.0.0.paste.txt).

| What | Where | Why |
|---|---|---|
| `AssocQueryStringW` (shlwapi), read-only | `src/ROROROblox.Core/RobloxLaunchHandlerProbe.cs` | Asks Windows whether anything is registered for the `roblox-player:` link, so a launch that cannot start shows RoRoRo's dialog instead of Windows' "you'll need a new app" message |
| `HKEY_CLASSES_ROOT\roblox-player\shell\open\command`, read-only | the same file | The same check, the classic registration |
| `ms-windows-store://review/?ProductId=9NMJCS390KWB` | `src/ROROROblox.Core/RatingAsk.cs` | Opened only when the user presses **Rate RoRoRo** |

**Measured against `v1.36.0.0`, not assumed** (2026-11-10, at `main` 47b1054):

- `Package.appxmanifest`: no change on main. `finalize-store-build.ps1` stamps the Version attribute
  and nothing else.
- `plugin_contract.proto`: unchanged (`git diff --stat v1.36.0.0..main -- '*.proto'` is empty).
- Interop and input synthesis in added source lines under `src/ROROROblox.App` and
  `src/ROROROblox.Core`: **zero** matches for `SendInput`, `keybd_event`, `mouse_event`,
  `SetWindowsHookEx`, `PostMessage`, `SendMessage(`, `WriteProcessMemory`, `OpenProcess`,
  `LibraryImport`, `RegisterRawInputDevices`. **One** `DllImport`, the `AssocQueryStringW` above.
- New URLs or link schemes in added source lines: **one**, the Store review link above.
- Registry access in added source lines: **one**, the read above.
- New network hosts: **none**.

## 1. Version

**1.37.0.0.** A MINOR bump per the playbook's table: two user-visible features (the rating ask, three
FPS caps) and two fixes.

## 2. Listing

**Listing audited against the v1.37 release notes on 2026-11-10: short description, long description
and product features are unchanged.**

| Surface | Checked for | Outcome |
|---|---|---|
| Short description | "FPS caps" is the only claim this release touches | Still true with thirteen caps. Unchanged. |
| Long description | The FPS-cap bullet; the privacy and security paragraph | The cap bullet names no numbers. The privacy paragraph is about the password and the cookie; `rating-ask.json` holds neither and is never sent. Unchanged. |
| Product features (20/20) | Whether the rating ask, the launch check or the caps earn an entry | None is a selling claim. The frame-rate entry says caps can be "raised, lowered or lifted per client", which still holds. Unchanged. |
| Hub page (`docs/index.md`) | The "What you get" list | Not re-read this release. |

### The product name: the owner's change, by hand

The owner approved testing a new English product name on 2026-11-10:
**`RoRoRo — Multi-Instance Launcher & Account Manager`**, replacing
`RoRoRo — Multi-launcher for Windows`. Reason: Store search finds the listing for "rororo" and "roblox
launcher" only (measured 2026-11-10 with `scripts/store-analytics/store_search_rank.py`), and the one
generic query that finds it is the one whose second word is in the title. The name cannot carry the
word Roblox (10.1.1.1 rejection, v1.1.0.0).

- **Product names are never written through the API** (the title-collapse incident; the console
  hard-blocks it). The name is reserved under **Manage app names** and picked in the English listing's
  **Product name** field, both in Partner Center, by the owner.
- English only for the test. The other six listings keep their reserved names.
- After the listing is live: re-run the search-rank script and compare with the 2026-11-10 baseline.
- `apps/rororo-live/config.toml` in the listing console carries the reserved names; update its `en`
  entry once the reservation exists.

## 3. What's new in this version

[`whats-new-1.37.0.0.md`](whats-new-1.37.0.0.md), seven blocks, each under the 1,500-character cap
(833 to 973 characters). The headline and four bullets are new text; the two closing paragraphs in
every language are v1.36's, unchanged. The six language sheets carry the same blocks.

**Listing verifier:** recorded in section 7.

## 4. Notes for certification

[`reviewer-letter-1.37.0.0.paste.txt`](reviewer-letter-1.37.0.0.paste.txt), 72 lines. Partner Center
only; `submit_write.py` never sends it.

## 5. Age rating, privacy, screenshots

- **Age rating:** unchanged. No new content and no user-to-user communication.
- **Privacy:** `docs/PRIVACY.md` gains two rows in the stored-files table. `rating-ask.json` is new in
  this release. `last-exit.json` has existed since v1.35 and was missing from the table; it is added
  now. No network row changes.
- **Screenshots:** unchanged. The rating line shows only after three days of use and the Roblox-needed
  dialog only on a PC without a Roblox desktop client, so neither belongs in the carousel.

## 6. Packages

Pending.

## 7. Verification

- **App strings, 2026-11-10.** Nine new keys in six languages (54 pairs) through the hosted
  translation verifier at commit `4c7a2cd`: 54 approve, 0 revise.
- **Tests.** 3,486 unit and 27 harness pass on `main` at 47b1054 (Release). `ci.yml` green on x64 and
  arm64 for every merged PR (#268, #270, #272, #273).
- **Mutation checks.** The Roblox-needed dialog tests, the rating-ask rule tests and the rating-ask
  view-model tests were each broken on purpose and seen to fail. One test, "the sentence leads the
  body", passed with its rows swapped on the first attempt and was rewritten.
- **Listing verifier on What's new:** pending.
- **Security agents and the release smoke:** pending.

**Not verified, and said so in the release notes:** the launch check on a PC whose only Roblox is the
Store edition; the rating line on a real Store install; the new FPS caps on a live client.

## 8. Shipped

Pending.
