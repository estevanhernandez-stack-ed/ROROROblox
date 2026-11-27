# Submission packet — v1.41.0.0

> **PREPARED 2026-11-26 on `cart/v141-item13`, NOT SHIPPED.** The cycle (`docs/cycles/2026-11-26-roblox-feeds/`)
> is still being built: items 9 to 12 are in flight, so every line below that depends on the shipped build
> says so. Nothing here has been written to Partner Center or any Store API. Certification last saw
> **v1.40.0.0**. Sources: the cycle spec section 11, `prd.md`, and the tree at `acfb0af`.

---

## Open items for Este, before anything is submitted

1. **Does the new long-description sentence touch what Store listing variant B varies?** Variant B is a
   Partner Center product-page experiment, live 2026-11-22 to 2027-02-20, 50/50, across all languages
   (no listing-image changes until it ends; this release changes none). The new sentence edits the long
   description in seven languages. I cannot see from the repo what variant B varies. If it varies the
   description, the edit contaminates the read; if it varies images or the short description, the edit
   is safe. Needs your answer (spec section 20). If the answer is "it touches it", the fallback is to
   hold the sentence and ship the privacy page, README and reviewer letter only, since the sentence is
   a courtesy and the letter and PRIVACY.md carry the legal weight.
2. **The Neb log search has not come back** (spec section 20: `Select-String` for
   `Pre-warm: holding the batch` in Neb's logs). `whats-new-1.41.0.0.md` therefore says the false hold
   "could show", with an HTML comment above the block saying to change it to "was showing" if the count
   is non-zero. Apply to all seven blocks.
3. **The listing verifier did not judge anything this session.** `ingest_translations` loaded
   `docs/store/listing-translations-1.41-delta.json` (12 pairs: long description and What's new, six
   languages). `run_translation_review` returned `judgedPairs: 0` three times: `bothShapes` stopped on
   "budget" with nothing judged, and a single-language call for `fr` and for `de` stopped "complete"
   with nothing judged. Retrying with the same arguments was told to be useless, so I stopped. **The
   orchestrator needs to run it** (the delta ingests cleanly: no cap violations). The six
   translations of the privacy sentence and of the What's new blocks are hand-written and unreviewed
   until then.
4. **A sentence already on the listing is not true, and was not true before this release.** The long
   description's privacy paragraph says "Nothing about you leaves your machine except the Roblox-side
   calls during launch". `docs/PRIVACY.md` and the tree say the Roblox calls run continuously while
   RoRoRo runs (presence about every 25 seconds per saved account, `Core/Diagnostics/PresenceService.cs`).
   I did not edit it: it is published text in seven sheets and the owner approves paragraph edits.
   Proposed English, if you want it fixed in the same pass: "Nothing about you leaves your machine except
   the Roblox-side calls while RoRoRo runs". The six sheets would follow.
   **Resolved 2026-11-27 on Este's word ("fix it").** The clause now reads "the Roblox-side calls
   RoRoRo makes for you", which names no timing at all, in all seven sheets with dated notes. The
   delta was re-exported; verifier result is recorded under open item 3.
5. **Re-measure the reviewer letter's section 3 on the release branch.** Its numbers are from `acfb0af`
   (section 7 has the commands).
6. **Check the switch's label.** The letter and `PRIVACY.md` quote `Show Roblox's status and game
   activity` (spec assumption 32). Item 9 owns the string; match it before anyone pastes.

---

## 1. Version

**1.41.0.0**, a MINOR bump from 1.40.0.0 by the playbook's table: new user-visible notice line, new
default-on network host. Not confirmed by the owner yet. Three places must agree before the tag: the
csproj `<Version>`, `Package.appxmanifest` (both stamped by `finalize-store-build.ps1`) and the tag.

## 2. Listing

**Audited against the tree on 2026-11-26.** Short description and product features: unchanged. Long
description: one sentence added to the privacy paragraph, in all seven sheets (not yet owner-approved;
open items 1 and 3). The auto-update bullet and the features list are not touched: nothing in this
release changes what they claim, and the Roblox line and the game counts are new surfaces that earn no
listing claim until the cycle ships and a screenshot can show them (variant B holds images to
2027-02-20).

**Added to the end of the privacy paragraph (English; the six sheets carry their own translations):**

> RoRoRo also checks Roblox's public status page, which a third party (Status.io) hosts for Roblox, sends nothing about you to do it, and has a switch in Settings to turn it off.

Long description 6,083 characters in English after the edit (cap 10,000). Every sheet's block is under
the cap (the export script enforces it).

### The three-surface audit

Every claim on the three surfaces, against the tree at `acfb0af`. Each "where" was opened and the symbol
read in this session; none is taken from the feature ledger, the release notes or `docs/features.md`
(which was used only to find files). A claim whose evidence is a test or a manifest says so.
`PASS` means the code that does the thing exists where named. It does not mean the feature was run
live in this session.

**Short description (184/200).**

| Claim | Where in the tree | Result |
|---|---|---|
| "your games" (the Library) | `src/ROROROblox.App/Games/LibraryViewModel.cs:62` `LibraryViewModel` | PASS |
| "FPS caps" | `src/ROROROblox.Core/FpsCapSettler.cs:60` `FpsCapSettler` | PASS |
| "alerts to your phone" | `src/ROROROblox.App/Notify/PushoverSender.cs:33`, `NtfySender.cs:24` | PASS |
| "themes" | `src/ROROROblox.App/Theming/ThemeService.cs:19` | PASS |
| "in 7 languages" | English `Strings.resx` plus `Strings.{de,es,fr,pl,pt-BR,ru}.resx` under `src/ROROROblox.App/Properties/` (7 files); `Package.appxmanifest:35-41` declares the same seven | PASS |
| "several saved accounts side by side" | `src/ROROROblox.Core/MutexHolder.cs:22` `MutexHolder` | PASS |
| "one click each" | row launch, `MainViewModel.LaunchAccountAsync` (`src/ROROROblox.App/ViewModels/MainViewModel.cs`) | PASS |

**Long description, opening paragraphs.**

| Claim | Where in the tree | Result |
|---|---|---|
| Account added "through Roblox's own login page" | `src/ROROROblox.App/CookieCapture/CookieCaptureWindow.xaml.cs:12` (`roblox.com/login` in WebView2) | PASS |
| "every game you play in one Library, one click into a game or a saved private server" | `Games/LibraryViewModel.cs`, `LibraryFilter` (`Recent`, `Favourites`, `PrivateServers`) at `:19`; `Play` at `PlayAsync` | PASS |
| "an alert on your phone if the game drops" | `src/ROROROblox.Core/Discord/AlertTrigger.cs:13` (dropped-out trigger), routed by `Core/Discord/AlertRouter.cs:54` to `App/Notify/PhoneAlertSender.cs` | PASS |
| "Robux, Plus and friends online at a glance" | `src/ROROROblox.Core/AccountFacts.cs:9`, `AccountFactsService.cs:70` | PASS |
| "tells you when it needs attention" | `src/ROROROblox.App/ViewModels/AccountHealth.cs:15` `AccountHealth` | PASS |
| "any game link you paste" | `src/ROROROblox.App/Games/LibraryPaste.cs:38` `LibraryPaste` | PASS |
| several clients "each signed in as a different saved account you own" | `Core/AccountStore.cs`, `Core/RobloxLauncher.cs` | PASS |

**Long description, bullets.**

| Claim | Where in the tree | Result |
|---|---|---|
| Multi-instance, "holds the Roblox singleton mutex" | `Core/MutexHolder.cs:12`, `:22` | PASS |
| DPAPI-encrypted vault; "a copy ... moved to another PC will not decrypt" | `Core/AccountStore.cs:1076` `ProtectedData.Protect` (CurrentUser scope) | PASS |
| "passphrase-protected export" | `Core/Transport/AccountTransportService.cs:68` (`AesGcm`; PBKDF2 in the same file) | PASS |
| Live status: "which account is in which game" | `Core/Diagnostics/PresenceService.cs:37` | PASS |
| "who is idle and for how long" | `Core/Diagnostics/ActivityMonitor.cs:9`; the row chip at `App/ViewModels/AccountSummary.cs:682-689` | PASS |
| "per-account frame-rate caps that stick" | `Core/FpsCapSettler.cs:6` (settles the write across a quiet window) | PASS |
| Accounts at a glance: Robux, Plus, friends online, "a note of your own" | `Core/AccountFactsService.cs:70`; `Core/Account.cs:28` (`Note`) | PASS |
| "recent games, favourites and private servers" in an opened account | `App/ViewModels/AccountPanelViewModel.cs:135` | PASS |
| Session expired, needs verification, limited by Roblox: "each has its own badge, and one click starts the fix" | `App/ViewModels/AccountHealth.cs` (`Limited` at `:8`); `Core/SessionLimitedException.cs` | PASS for the badges; the "one click starts the fix" half is not re-checked here and rides on the v1.36 smoke |
| Library "in a grid or a list" | `Games/LibraryViewModel.cs:187` `IsGridView`, `:193` the List mirror | PASS |
| "Search it, filter it, paste a game link, and choose which account plays" | `LibraryViewModel` class doc (typing filters `Visible`), `LibraryFilter` `:19`, `LibraryPaste.cs`, `Games/AccountPickerFlyout.xaml.cs` | PASS |
| Squad Launch | `App/SquadLaunch/SquadLaunchWindow.xaml.cs:15` | PASS |
| Friend Follow | `App/ViewModels/MainViewModel.cs:2040`, `FriendFollowWindow` (`:295-297`) | PASS |
| Memory watchdog "learns what a Roblox client actually costs in RAM" | `Core/Diagnostics/MemoryWatchdog.cs:16`, `ClientFootprintLearner.cs:32` | PASS |
| Recycle "puts it back in the same server it was in" | `Core/Diagnostics/AccountRecycler.cs:35` `RecycleAsync(accountId, LaunchTarget target, ...)` | PASS (it takes the target; the live same-server proof is v1.14's, not redone here) |
| Rejoin: "At most three times an hour, never your main" | `Core/Diagnostics/AutoRejoinMonitor.cs:91` `BudgetWindow` 1 hour, `:98` `BudgetPerWindow = 3`, `:160` `candidate.IsMain` skipped | PASS |
| "your own Stop always wins" | `AutoRejoinMonitor` `state.Paused` at `:160` | not re-read for the Stop path; carried |
| One tools window with seven pages; F1 shows the list | `App/Shell/ShellWindow.xaml.cs:19`; `App/Input/KeyboardVocabulary.cs:87` (F1 opens the shortcuts list); pages: `Games/GamesPage.xaml`, `Preferences/SettingsPage.xaml`, `Diagnostics/DiagnosticsPage.xaml`, `Plugins/PluginsPage.xaml`, `KnownIssues/KnownRobloxIssuesPage.xaml`, `About/AboutPage.xaml`; History is opened at `App/App.xaml.cs:1866` | PASS (seven pages: Library, Settings, History, Diagnostics, Plugins, Known Roblox issues, About) |
| Themes: "Four built-in" | `Core/Theming/ThemeStore.cs` `BuildBuiltIns`: `brand`, `midnight`, `magenta-heat`, `flatline` (`:229`, `:245`, `:269`, `:286`) | PASS |
| "one that carries no meaning in colour alone" | `flatline` theme; `docs/features.md` row and `ContrastGuard`; the tests pin it | PASS for the theme's existence; the claim's wording is the v1.22 audit's |
| "a builder ... share it as a file", "ten colours" | `App/Theming/ThemeBuilderWindow.xaml.cs:16`; `Core/Theming/ThemeShare.cs:49` | PASS |
| Optional alerts to desktop, Discord webhook, phone via Pushover or ntfy | `App/Discord/DiscordWebhookSender.cs:39`; `App/Notify/PushoverSender.cs`, `NtfySender.cs`; `Core/Discord/AlertRouter.cs:54` | PASS |
| "an every-two-hours all-good mark" | `Core/Discord/UptimeMarkTracker.cs:16` `MarkInterval = TimeSpan.FromHours(2)` | PASS |
| "A fresh install makes no alert calls at all" | `App/Notify/PhoneAlertSender.cs:33` (no config, no send); `Core/Discord/DiscordConfigStore.cs` | PASS (by the code's gate; not run on a clean profile here) |
| System tray: state-coloured icon; "double-click launches your main account" | `App/Tray/TrayService.cs:45`, `:85` | PASS |
| Plugins "run as separate processes", "hold no permissions until you grant each one by name" | `App/Plugins/PluginProcessSupervisor.cs:6`; `App/Plugins/ConsentStore.cs:16`; `RpcMethodCapabilityMap` | PASS |
| "Auto-update via Velopack" with remote config for "known-good Roblox version and mutex name" | `App/Updates/UpdateChecker.cs:12` (`WaitExitThenApplyUpdates`); `Core/RobloxCompatChecker.cs:13`; `ROROROblox.App.csproj` `Velopack 0.0.1298` | PASS |
| "Every control announces its name to assistive tech" | `Tests/AccessibleNamingFenceTests.cs:22`: a fence with one allowed unnamed control, so "every" is "every but one, by a ratchet" | PASS with that caveat, unchanged from earlier audits |
| "contrast is verified against rendered pixels in every theme" | `Tests/ContrastPairGateTests.cs:62` and `Tests/Rendering/` | PASS |
| Speaks six languages; "the picker only offers a language that's fully translated" | `App/Localization/UiCulture.cs:11` (the AVAILABLE-list guard) and the seven resx files | PASS |

**Long description, privacy paragraph.**

| Claim | Where in the tree | Result |
|---|---|---|
| "Your Roblox password is never seen by RoRoRo ... embedded in a Microsoft Edge WebView2 frame" | `App/CookieCapture/CookieCaptureWindow.xaml.cs:12` | PASS |
| "No telemetry. No analytics." | `ROROROblox.App.csproj` `PackageReference` list: no analytics or crash-reporting SDK; no analytics host in any added or existing source (a search for the common vendors finds only the word "amplitude" in a memory-watchdog comment and `PrivateServer` identifiers) | PASS |
| "Nothing about you leaves your machine except the Roblox-side calls during launch" | `Core/Diagnostics/PresenceService.cs` and the Roblox-calls row in `PRIVACY.md` | **FAIL, pre-existing; fixed 2026-11-27**: now "the Roblox-side calls RoRoRo makes for you". Open item 4 |
| "alerts to your own Discord webhook or ... Pushover or ntfy" | `App/Discord/DiscordWebhookSender.cs`, `App/Notify/PushoverSender.cs`, `NtfySender.cs` | PASS |
| "downloads its own signed files from its GitHub releases ... and sends nothing about you" | `Core/KnownIssues/KnownIssuesFeed.cs:44`, `Core/RobloxCompatSignature.cs` | PASS |
| "On a Store install, ... asks the Microsoft Store, through Windows, whether an update is waiting" | `App/Updates/StoreContextUpdateProbe.cs:8` (`GetAppAndOptionalStorePackageUpdatesAsync`) | PASS |
| **New sentence:** Roblox's public status page, "which a third party (Status.io) hosts for Roblox" | `Core/Feeds/RobloxStatusApi.cs:16` `StatusUrl = "https://api.status.io/1.0/status/..."`; registered with `UseCookies = false` at `App/RobloxHttpClients.cs:66` | PASS |
| **New sentence:** "sends nothing about you" | the DTOs read only `Id` and `status_code`; the request carries no cookie and `RORORO/<version>`. The address any web request carries is the one thing sent, named on the privacy page row | PASS, same wording the GitHub sentence uses |
| **New sentence:** "has a switch in Settings to turn it off" | spec section 10 and item 9 (`SettingsPage_RobloxFeeds*`, in flight) | **Not yet in the tree.** Item 9 must land before this sentence is true. Do not submit without it |

**Long description, trademark paragraph.**

| Claim | Where in the tree | Result |
|---|---|---|
| "launches the official Roblox client unmodified ... does not inject into, hook into, or alter the Roblox process" | no injection API in either project; the added lines under `src/` since `v1.40.0.0` contain none of the ten native-call names (section 7) | PASS |

**Product features (20/20).**

| Entry | Where in the tree | Result |
|---|---|---|
| One-click multi-instance launcher | `Core/MutexHolder.cs` | PASS |
| DPAPI vault with passphrase-protected export | `Core/AccountStore.cs:1076`; `Core/Transport/AccountTransportService.cs:68` | PASS |
| Live status ... idle time, frame-rate caps "raised, lowered or lifted per client" | `Core/Diagnostics/PresenceService.cs`, `ActivityMonitor.cs`, `Core/FpsCapSettler.cs`, `Core/FpsPresets.cs` | PASS |
| Accounts at a glance with "a one-click fix badge" | `Core/AccountFactsService.cs`; `App/ViewModels/AccountHealth.cs` | PASS (the click half carried, as above) |
| Memory watchdog plus Recycle | `Core/Diagnostics/MemoryWatchdog.cs`, `Core/Diagnostics/AccountRecycler.cs` | PASS |
| Squad Launch and Friend Follow | `App/SquadLaunch/SquadLaunchWindow.xaml.cs`; `MainViewModel.cs:2040` | PASS |
| One Library; grid or list, search and filters, paste any roblox.com link | `Games/LibraryViewModel.cs`, `LibraryPaste.cs` | PASS |
| One tools window ... keyboard shortcuts throughout | `App/Shell/ShellWindow.xaml.cs`; `App/Input/KeyboardVocabulary.cs` | PASS |
| Four built-in themes plus a theme editor | `Core/Theming/ThemeStore.cs`; `App/Theming/ThemeBuilderWindow.xaml.cs` | PASS |
| Optional Discord alerts to a webhook you create | `App/Discord/DiscordWebhookSender.cs` | PASS |
| System tray with state-coloured icon and double-click main launch | `App/Tray/TrayService.cs:45`, `:85` | PASS |
| Plugin system with consent and out-of-process isolation | `App/Plugins/ConsentStore.cs`, `PluginProcessSupervisor.cs` | PASS |
| Auto-update that keeps working when Roblox changes underneath | `App/Updates/UpdateChecker.cs`; `Core/RobloxCompatChecker.cs` | PASS |
| Start with Windows, "Windows' own Startup list stays in control" | `App/Startup/StartupRegistration.cs:26`; `PackagedStartupRegistration.cs` (a `StartupTask`, `Enabled="false"` by default in `Package.appxmanifest`) | PASS |
| Discord Join starts RoRoRo "even when it's closed, and always asks before launching anything" | `App/Discord/JoinUriScheme.cs:15`; `App/Modals/JoinRequestWindow.xaml.cs` | PASS |
| Alerts on your phone through Pushover or ntfy, and desktop and Discord in any mix per alert | `App/Notify/PushoverSender.cs`, `NtfySender.cs`; `Core/Discord/AlertRouter.cs:54` | PASS |
| Uptime marks every two hours | `Core/Discord/UptimeMarkTracker.cs:16` | PASS |
| Available in six languages ... "switching instantly, following Windows or your pick" | `App/Localization/UiCulture.cs`; the seven resx files | PASS |
| Known Roblox issues, "kept current without an app update" | `Core/KnownIssues/KnownIssuesFeed.cs`; `App/KnownIssues/KnownRobloxIssuesPage.xaml.cs:13` | PASS |
| Rejoin if it drops out, "never your main" | `Core/Diagnostics/AutoRejoinMonitor.cs:160` | PASS |

**What the audit found.** One claim that fails and was already failing (the "during launch" sentence,
open item 4), one new sentence that is not yet true because its switch has not landed (item 9), and
three claims whose "click" or "Stop wins" halves rest on earlier smokes and were not re-run here.
Short description and features need no edit. No screenshot or caption changes.

**Held, not added:** no listing line for the Roblox status line or the saved-game counts. The features
field is full (20/20), and no image can show them before 2027-02-20.

### The product name

Unchanged from v1.40: the English name `RoRoRo — Multi-Instance Launcher & Account Manager` is live and
the owner holds it. A submission cloned by the API carries the published name; the API never writes it.

## 3. What's new in this version

`docs/store/whats-new-1.41.0.0.md`, seven blocks, 1,136 to 1,350 characters (cap 1,500). Five bullets
and the quit-before-updating paragraph. The false-hold bullet says "could show" (open item 2).
`WHATS_NEW_VERSION` is `1.41.0.0` in `scripts/export-listing-translations.py`; the delta
`docs/store/listing-translations-1.41-delta.json` carries the long description and What's new (open
item 3). `listing-translations.json` was regenerated by the script at the same commit.

## 4. Notes for certification

`docs/store/reviewer-letter-1.41.0.0.md`, with the paste-ready body in
`reviewer-letter-1.41.0.0.paste.txt` (4,731 characters; v1.40's was 4,983). It names the new host
(`api.status.io`), what it is asked, that nothing about the person is sent, and that it is on by
default with a Settings switch. **Partner Center only**: the box holds the v1.40.0.0 letter, replace
it whole.

## 5. Age rating, privacy, screenshots

- **Age rating:** unchanged. The status line draws no content from the service and shows no user
  content.
- **Privacy policy URL:** unchanged; the page's text changes (below).
- **Screenshots:** none changed. Variant B holds them to 2027-02-20.

### Text for the hosted privacy page (#300)

The hosted page (`https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/`) is built from
`docs/PRIVACY.md`, so the simplest hand-off is the file itself at the release commit. The six edits
this release makes, verbatim, so a hand-merge into a differing hosted copy is unambiguous:

**1. TL;DR bullet, who RoRoRo talks to**

```
- **No telemetry. No analytics. No third-party tracking.** RORORO makes network calls to Roblox-owned endpoints while it runs, not just at launch, including a presence heartbeat about every 25 seconds per saved account; to GitHub Releases (for the daily update check, the compatibility feed, the known Roblox issues list (v1.31+), and, in direct-download builds only, the plugin catalog when you open the Plugins page); and, *(v1.41+)* to Status.io, a third-party service that hosts Roblox's public status page, to ask whether Roblox reports a problem. That last read sends no cookie, no account and no identifier, and one switch in Settings turns it off. The network table below lists every host, including the ones that only run when you set something up.
```

**2. TL;DR bullet, what leaves the PC**

```
- **No data leaves your PC** except the calls in the network table below: the Roblox-side calls, the same ones Roblox.com would make from your browser; the plain downloads and reads from GitHub and Status.io, which send nothing about you; and the alert services you set up yourself. The one other thing that leaves is, if you choose to use it, a passphrase-encrypted account-export file that you save yourself (never auto-uploaded).
```

**3. Network table intro**

```
RORORO initiates HTTPS connections **only** to the hosts in this table (the plugin hosts and the alert services you set up yourself included):
```

**4. Roblox endpoints row**

```
| `presence.roblox.com` and related Roblox endpoints (games, friends, universes, `apis.roblox.com` for pasted share links, `clientsettingscdn.roblox.com` for the current client version, checked while the main window is open (about every 30 minutes, never while RoRoRo is in the tray) and before a batch launch) | Continuously while RORORO is running, including a presence heartbeat about every 25 seconds per saved account | Keeps each saved account's online status, game details, and friends list current, and *(v1.36+)* the friends-online count shown under each account (`friends.roblox.com`). *(v1.41+)* The client-version check also shows a notice in the main window when Roblox has an update waiting, and while the Library or an account panel is open, `games.roblox.com` is asked how many people are in a saved game and when it last changed: one request for the games on screen, with game ids only, no cookie and no account (the "Show Roblox's status and game activity" switch turns this off). The same kind of ongoing call the Roblox website makes while you're signed in there. |
```

**5. New status host row**

```
| `api.status.io` *(v1.41+)* | While the main window is open: first read 30 seconds after the window is shown, then about every ten minutes, and once early after a launch fails because of the connection (at most once a minute). Never while RoRoRo is in the tray, never at startup. | **Status.io is a third-party service. It is not Roblox, GitHub or Microsoft.** It hosts Roblox's public status page (`status.roblox.com`), and RoRoRo asks it one question: does Roblox report a problem on the rows that matter for launching? **Sent:** the request itself, which is your IP address and `RORORO/<version>`, as with any web request. **Never sent:** no cookie, no account, no identifier, nothing about your PC. Nothing the service returns is shown in the window, written to a file or logged: it only chooses which of RoRoRo's own sentences appears. On by default; **Settings, "Show Roblox's status and game activity" turns it off** (and the game-activity reads with it), and with it off no request goes to this host. The "status page" link on the notice opens in your browser; RoRoRo does not fetch it. |
```

**6. Closing sentence**

```
RORORO makes **no other network connections**. There are no analytics endpoints, telemetry endpoints, or third-party SDKs. Status.io (above) is a service RoRoRo reads a public page from, not an SDK and not an analytics endpoint, and it receives no information about you beyond the address any web request carries. The "What changed" link on the Roblox update notice opens Roblox's release notes in your browser; RoRoRo does not fetch that page.
```


Untouched: the three measured data-folder rows from 2026-10-11 and the paragraph that explains them.
Done when the hosted page carries the status row, the client-version and games wording and the reworded sentences. #300 closes then.

## 7. Verification

- **Listing verifier:** not run to a result. See open item 3: ingest worked (12 pairs, no cap
  violations), the review judged 0 of 12 on three calls. Re-run on the delta; expect Rule 3 findings
  on the product nouns and the translated button names as at v1.36 to v1.40, which the owner has
  overruled each time.
- **Fences:** `ListingSheetParityFenceTests` and `ContrastPairGateTests`, full suite: see the commit
  message and the worker report for the counts.
- **Section 3 of the letter, re-measure on the release branch.** At `acfb0af` against `v1.40.0.0`:
  - `git diff --stat v1.40.0.0..HEAD -- src/ROROROblox.App/Package.appxmanifest` is empty.
  - `git diff --stat v1.40.0.0..HEAD -- '*.proto' '*NativeMethods.txt'` is empty.
  - Added lines under `src/ROROROblox.App` and `src/ROROROblox.Core` (tests excluded) matching
    `SendInput|keybd_event|mouse_event|SetWindowsHookEx|SendMessage|PostMessage|WriteProcessMemory|OpenProcess|DllImport|LibraryImport`: 0.
  - Added http(s) addresses in the same scope: `https://api.status.io/1.0/status/59db90dbcdeb2f04dadcf16d`,
    `https://create.roblox.com/docs/release-notes`, `https://games.roblox.com/v1/games`,
    `https://status.roblox.com`.
  - Added lines matching `Registry\.|RegistryKey|CreateSubKey|SetValue\(`: 0.
  - Shipped source in that scope: 30 files, +2,659 / -38 at `acfb0af`, before items 9 to 12.
  Run the same pipeline after those items land and change any number that moved. The `games.roblox.com`
  address was already in the tree before this cycle (`Core/RobloxApi.cs:23`); it counts as "added"
  only because the pulse client carries its own copy.
- **Not run:** any live check of the status read against Roblox's real page during an incident (none
  has occurred; the capture script records one when it does); any live Store-listing read.

## 8. Shipped

Not shipped.
