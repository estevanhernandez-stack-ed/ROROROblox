# Submission packet — v1.36.0.0

Everything Partner Center asks for on this submission, in the order it asks. Certification last saw
**v1.35.0.0**, so this is a single version's delta, and the largest in a while: the account-library
cycle (`docs/cycles/2026-11-01-account-library/`), 481 files and +29,135/−1,425 across the repository,
150 files and +19,102/−1,115 under `src/`.

> **DRAFTED 2026-11-05 (checklist item 15), not yet submitted.** Sections 1, 6, 7 and 8 belong to
> item 16 (packages, the GitHub release, verification, ship) and are marked pending. The Store write
> itself needs the owner's explicit word in the session.

---

## Read this first — three new Roblox calls, and nothing else new to assess

The reviewer letter leads with them rather than letting a reviewer find them:
[`reviewer-letter-1.36.0.0.paste.txt`](reviewer-letter-1.36.0.0.paste.txt).

**The new calls** (the account facts lane, `src/ROROROblox.Core/AccountFactsApi.cs`), each made for one
saved account with that account's own cookie:

| Host | Reads | Constant |
|---|---|---|
| `economy.roblox.com` | the account's Robux balance | `CurrencyEndpoint` |
| `premiumfeatures.roblox.com` | whether the account has Plus | `MembershipHost` |
| `friends.roblox.com` | the friends-online count (the host was already used for the friends list) | `FriendsHost` |

Cadence, from `AccountFactsService`: nothing at startup; a 60 s tick refreshes one account whose facts are
older than 10 minutes (`TimerRefreshAge`); opening an account's panel or a launch ending can refresh
sooner, never within 60 s (`MinRefreshAge`); one worker paces these calls so no two land in the same
second. Results are cached only in the DPAPI-encrypted `accounts.dat`.

**Measured against `v1.35.0.0`, not assumed** (2026-11-05, at `origin/main` 9437a90):

- `Package.appxmanifest`: no change on main. Item 16's `finalize-store-build.ps1` stamps the Version
  attribute and nothing else.
- `plugin_contract.proto`: unchanged (`git diff --stat v1.35.0.0..main -- '*.proto'` is empty).
- Interop and input synthesis in added source lines: **zero** matches for `SendInput`, `keybd_event`,
  `mouse_event`, `SetWindowsHookEx`, `PostMessage`, `SendMessage(`, `WriteProcessMemory`, `OpenProcess`,
  `DllImport`, `LibraryImport`, `RegisterRawInputDevices`.
- New URL strings in added non-test source: the three hosts above, and the GitHub repo URL that
  `InstallIdentity` hands Velopack's `UpdateManager` to read `IsInstalled`, a local check that makes no
  call. Everything else is in tests (`example.invalid`, `evil.example` fixtures).

## 2. Listing

**Rewritten, not audited-unchanged.** Spec §7.7: launcher first, alts second.

| Surface | What changed |
|---|---|
| Short description | Leads with the launcher; "alts when you want them". 184/200 in English. |
| Long description | Opens with the one-account story; the multi-account paragraph follows with every claim intact. Three bullets added; tools window says Library; privacy says where the facts come from and where they are kept. |
| Product features | Still 20/20: the two frame-rate entries merged; accounts at a glance added; join-by-link folded into a new Library entry. |
| Captions | Follow the v1.36 carousel order. |

### The three-surface audit: every new claim, traced to the tree

Checked against the code on main, never against a changelog.

| Claim (surface) | Where it is true |
|---|---|
| Robux, Plus, friends online under each account (short, long, features, What's new) | `AccountFactsApi` (three endpoints); `MainWindow.xaml` facts line; `AccountSummary.ShowRobuxBadge` / `ShowMembershipBadge` / `ShowFriendsOnlineBadge`; Plus is on for the app (`membershipEnabled: true` in App wiring) |
| A note of your own (long, features, What's new) | `Account.Note`; `AccountSummary.NoteFirstLine`; the panel's `NoteBox` |
| Open an account: recent games, favourites, private servers, one click (long, What's new) | `AccountPanelViewModel`; the panel's three columns and `Shell_Panel_SeeAll` |
| Session expired / needs verification / limited badge, one click starts the fix (long, features, What's new) | `AccountHealth`; `HealthActionCommand` → `RunHealthActionAsync` (re-authenticate, or open Roblox for the account) |
| One Library: played, favourited, saved, grid or list, search, filters, paste a link, Play as (long, features, What's new) | `LibraryViewModel`, `LibraryCatalog`, `GamesPage.xaml` grid/list toggle, chips, `LibraryPaste` (the paste box), `Library_PlayAs` |
| Your games, FPS caps, phone alerts, themes, in 7 languages (short) | the Library above; per-account FPS combo; alerts (v1.25+); four built-in themes; en plus six cultures (`Strings.<culture>.resx`) |
| Facts read with the account's own session and kept only in the encrypted vault (long privacy, PRIVACY.md) | `AccountFactsService` (cookie in a local for one refresh); `AccountStore` writes `Facts` into the DPAPI blob |
| Tour starts with one account (What's new) | `WelcomeWindow.xaml` (item 10) |
| Idle time counts for the account you played on (What's new) | `ActivityMonitor` 100 ms foreground ring (#221) |
| Background Roblox processes that never opened a window close after two minutes (What's new) | `Tray/IdleTrayClientSweeper`, 30 s timer, `TrayClient.IsIdleTrayClient(minAge: 2 min)` (#222) |
| Quit before updating from the Store (What's new, README) | the update-while-running failure (task `bxranna8z`) |

## 2a. Support contact — prepared, NOT written

Partner Center → the app → **Properties** (or **Store listings → Support contact info**, depending on
which page the console shows):

| Field | Value |
|---|---|
| Support contact email | `support@626labs.dev` |
| (replaces) | `supportt@626labs.dev`, the doubled-t typo live on the public listing (task `0h7s65ljx`) |

After publish, re-read the public catalog entry to confirm the email (tasks `bxranna8z`, `0h7s65ljx`).

**PRIVACY.md contact:** moved from a personal Gmail to `support@626labs.dev` (owner's call, 2026-11-05), matching the Store support contact.

## 3. What's new in this version

English block: [`whats-new-1.36.0.0.md`](whats-new-1.36.0.0.md), 1,033/1,500. The six translations are
in the same file and in each sheet.

**Listing verifier, 2026-11-05, three rounds over all six sheets** (short, long, features, What's new,
copyright, trademark; 36 pairs). Last state at `545a776` for ru, pt-BR, pl and `6c5bf40` for fr, de, es
(unchanged since).

Fixed, all of them defects that **shipped in v1.35 or earlier**, which the full re-read of rewritten
sheets surfaced:

| Sheet | Defect | Fix |
|---|---|---|
| pt-BR | **trademark disclaimer** dropped "trademarked" ("O termo é usado apenas"), in the long description and the trademark field (zero-tolerance) | "O termo registrado é usado apenas" |
| ru | repeat-frequency sentence ungrammatical ("как часто не более каждое") | "как часто может повторяться каждое" |
| ru | memory watchdog feature said "real price" for "real RAM cost" | "реальный расход ОЗУ" |
| ru, pl | the uptime feature lost its "Uptime marks" title | title restored |
| pl | "Drops" read as "Wylogowania altów" (logouts) | "Wypadnięcia z gry" |
| es | plugin grants lost "by name" ("uno a uno") | "cada uno por su nombre" |

Overruled, with the reason:

- **Rule 3 on the app's own labels** (Play as, Quit, Library and the tools-window page names in every
  translated sheet): the interface is localized, and every quoted label is the app's own catalog string
  (`Library_PlayAs`, `Tray_Quit`, `ShellWindow_Games` and so on), the standing ruling since v1.26.
- **"double-click main launch" (fr, pt-BR, es features):** the verifier read it as opening the main
  window. The English long description says "double-click launches your main account", which is what the
  translations say and what the tray does.

The new v1.36 text itself drew no finding beyond those two overrules.

## 4. Notes for certification

[`reviewer-letter-1.36.0.0.paste.txt`](reviewer-letter-1.36.0.0.paste.txt). Every "nothing moved" line in
it is the measured one from the top of this packet.

## 5. Age rating, privacy, screenshots

- **Age rating:** unchanged. No new content and no user-to-user communication.
- **Privacy:** `docs/PRIVACY.md` updated for this release. TL;DR line for the facts; `accounts.dat`
  contents; rows for `economy.roblox.com` and `premiumfeatures.roblox.com`; friends-online in the
  presence row; the Diagnostics-bundle section rewritten to match `BuildSupportBundle`. The three rows
  measured on 2026-10-11 are untouched.
- **Screenshots:** the v1.36 carousel in all seven folders (`docs/store/screenshots/<lang>/`, ten files,
  `README.md` there). Frames 01-04 are test-harness renders of the real XAML with fake accounts;
  06-09 live captures in streamer mode; 05 and 10 kept.

## 6. Packages — pending (item 16)

## 7. Verification — pending (item 16)

## 8. Shipped — pending (item 16)
