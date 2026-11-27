<!-- Cart cycle #7, step 4. Inputs: scope.md and prd.md beside this file. Written here rather than
     docs/spec.md, which holds the retired v1.18/v1.21 snapshot CLAUDE.md treats as history.
     Autonomous run (profile autonomy_level: fully-autonomous, builder: self). Recon against HEAD 3a1e042
     and live probes on 2026-11-26. Every beat the record did not answer is marked
     "(default — confirm on next interactive run)" and collected in §21. -->

# RoRoRo: Roblox at a glance — Technical Spec

**Cycle:** opened 2026-11-26 on main at v1.40.0.0 · **Target:** v1.41.0.0 · **Inputs:** `scope.md`, `prd.md` beside this file · **Anchor:** *Know before you launch.* (still an assumption)

> **Banner, 2026-11-27: where the build departed from this spec.** The body below is left as written; these
> lines win over it.
>
> 1. **"Launch all" is "Launch multiple".** The many-account update sentence names the button by its real label,
>    `MainWindow_LaunchMultiple`, in every language, and says "the others" for "your other accounts":
>    "Roblox has an update. The next launch installs it, and Launch multiple holds the others until it is done."
> 2. **The switch label ends with a period:** "Show Roblox's status and game activity." (§21 item 32.)
> 3. **At 380 px the line's buttons and the "+N more" toggle drop to a row under the sentence** (Este,
>    2026-11-26 afternoon, after measuring: the real sentences wrapped to three to five lines beside the
>    buttons). This reverses "no compact wrap rule" from the C1 reading, and the toggle now lives inside the
>    notice template. Every status and update sentence fits two lines at 380 in all seven languages.
> 4. **The pulse, per surface** (C1 pick C3, then the afternoon ruling): the grid tile shows the count only; the
>    list row carries "N playing · updated N ago" as a second line spanning the columns; the account panel shows
>    the count only, with the date in its tooltip and accessible name. §21 item 28 is overturned.
> 5. **The startup metric** (§16, checklist item 1) is the start banner to `UpdateChecker`'s `Velopack reports
>    not-installed` line, the one synchronous line after `mainWindow.Show()`. Measured at C2: 1304 ms (v1.40)
>    against 1308 ms (v1.41).
> 6. **The tray-time request baseline cannot be taken** (`AppLogging.cs:46` drops `HttpClient`'s request lines);
>    each lane's own Debug line and the fake-clock tests carry it (checklist assumption 39).
> 7. **The Build a theme dialog rider rode** (task kw9psaaah): its row is two, measured to fit in seven languages
>    at 640 and 540.
> 8. **Known gap at merge:** the account panel's "Not played by this account · N playing" still trims in en, de,
>    es and pt-BR, and "0 playing" in de; the full text is in the tooltip.
>
> Measurements: `docs/store/smoke-2026-11-26-roblox-feeds.md`. Rulings: `mocks/README.md`.

Three readings, one line, one text line per game. Two of the readings already exist in the app and one of them lies. So the cycle is: fix the version comparison (Core, one function), add two small typed clients (status, pulse) that carry no cookie and keep no text, add one coordinator that decides when anything is asked, and add one pure resolver that decides which Roblox sentence is drawn. Everything on screen is a view over those four. No launch path gains a dependency on any of them, and that is enforced by construction: the launch code never awaits the coordinator, the status client or the pulse service.

## 0. Where the tree and the probes corrected the PRD

Read and measured 2026-11-26. Fifteen corrections. Numbers 1, 3, 6, 8 and 13 change the design.

1. **A status row is a pair, and the pair has ids.** Roblox's page reuses one row under several groups: Website (`5f31cdef5f2c7c04bbdc4fbf`) sits under User, Player and Creator; Games under Player and Creator; Avatar under all three. A real archived answer from another Status.io page shows the same row id at 400 under one group and 300 under another in the same read, so a status belongs to the (group id, row id) pair. The PRD's five watched rows stand as five pairs, matched by id. A rename on Roblox's side no longer silences the line, which is better than the PRD's rule ("renamed means silent"); a removed pair still does.
2. **The line reads row codes and nothing else.** An archived answer (2023-05-29) has two open incidents while every row reads 100. Scheduled maintenance does not change a row's code; maintenance under way sets it to 200. So the PRD's rules ("clears when every watched row is operational", "scheduled shows nothing", "under way is named") all fall out of the row code alone, and the incident and maintenance arrays are never parsed.
3. **No recorded Roblox incident exists to use as a fixture.** The Wayback Machine holds two captures of Roblox's status answer (2026-10-28, 2026-11-15), both Operational. Thirty public repos that name the page id hold no payload. The archive does hold real incident answers from other Status.io pages, so the fixtures are real and unmodified, from another page, and the evaluator takes its watch table as a parameter so no payload is edited to fit. §6.4.
4. **"What changed" goes to the release-notes index.** A per-version page exists (`…/release-notes/release-notes-748` answers 200, `-749` answers 404). Across builds 741 to 748, Roblox's notes were posted one to three days after the first client build, eight of eight. The relation between a build and the day the endpoint starts reporting it is not established. Under the PRD's own rule (no per-version link unless it can be relied on without fetching), the link is `https://create.roblox.com/docs/release-notes`, always.
5. **One pulse request carries 50 games.** 50 ids answered 200; 51 answered 400, code 9, "Too many universe IDs were requested." An id Roblox no longer returns is absent from the answer, not an error.
6. **There is no batch call to extend, and `IRobloxApi` is expensive to grow.** `GetGameMetadataByPlaceIdAsync` asks for one universe at a time, inside a three-call chain. `IRobloxApi` has 15 private fakes in 13 test files. The pulse gets its own typed client and its own interface, the same move the facts lane made in v1.36.
7. **A Library entry does not know its universe, and a private server may never have asked.** `LibraryEntry` has no universe id. A saved game has one (`FavoriteGame.UniverseId`); a private server has only a place id and a code; a Recent entry has only a place id. `game-icons.json` maps place to universe for every place the icon cache has resolved, but the cache is only asked when an entry has no icon (`LibraryViewModel.cs:401`), and a saved server carries its own thumbnail, so its place may never have been resolved. Answer to the PRD's open question: a private server knows its game's place, not its universe; the universe comes from a saved game of the same place when there is one, else from the icon cache, else from one request through the existing icon lane. §8.3.
8. **`IAppSettings` broadcasts nothing, and there is no settings export.** A setting takes effect because the Settings page pushes the new value into the object that uses it. The PRD's line "exports and imports with the rest of the settings" has nothing to attach to: the only export is account transport, which carries per-account fields. The switch persists in `settings.json` like every other switch and that is the whole of it.
9. **The notice area is eight grid rows, and three tests pin the banners this cycle moves.** Row 2 is the hold banner, row 3 is one strip with eight independently collapsing children (contested is the fourth, known issues the sixth), row 4 is the drift banner, row 5 is the Bloxstrap warning. `BannerRecipeTests`, `Rendering/BannerPairRenderTests` (expects two stacked banners with one fill) and `ExpiredRowRedundancyTests` pin the drift and Bloxstrap borders. Folding drift into the line would change all three, for a banner that cannot fire. So the drift border stays where it is until Este rules on it, and only a "repair" ruling moves it. §7.8.
10. **The banner rows have no compact handling and no wrap measurement.** In compact (380 px wide) every banner shows in full and wraps as it likes. `LabelWrapMeasurementTests` walks the roster, the Library and the tour only. The PRD's "as the contested banner does today, in at most two lines" describes a bound that does not exist. §7.6 sets one for the two new notices.
11. **There is no link rank, no ghost toggle, and no fence themes a tooltip.** Button ranks are Primary, Secondary, SecondaryStrong, DialogClose, Destructive, Cta, AccentAction, Warning, Ghost; toggle ranks are `SecondaryToggleButtonStyle`, `PanelExpanderToggleButtonStyle` and `PillToggleButtonStyle`. "See known issues" is a Secondary button. The two new links are Secondary buttons, and the "more" count is a panel-expander toggle that opens an inline list, not a tooltip.
12. **Twenty new string keys, not ten.** Six status sentences, four row names, a counted suffix, a link label, three update strings, the more-count and its spoken name, one pulse string, two for the switch, plus plural forms.
13. **A panel is open at startup.** The main's panel (or the first row's) is resolved open at load, and a persisted open state is restored (`MainViewModel.cs:5450-5452`, `AccountPanelViewModel.cs:307-318`). That load path is network-free on purpose today. So "a collapsed panel asks for nothing" does not keep the pulse out of the startup burst; the pulse service needs the same settle gate the coordinator has. §8.2.
14. **"Once after any client attaches" becomes a local re-read, and only while an update is pending.** The endpoint's answer is already in hand; what changes at an attach is the installed client. Re-reading two local file versions against the cached answer needs no request, so it can run while the window is hidden and clears the notice at once. An attach while nothing is pending re-reads nothing. §5.2.
15. **In compact the update notice has no batch clause** even with several accounts, and the "updated" date needs a year. The first is room (§7.6); the second is that a game can be years stale and the app's short date has no year (§8.6).

Two PRD questions stay open because this machine cannot answer them: the Neb log search (the share refused the read; §20), and what Roblox's "updated" date tracks (§8.6).

## 1. Stack

Fixed, and unchanged by this cycle. .NET 10 (SDK 10.0.203), WPF with WPF-UI 4.3.0, `Microsoft.Extensions.Http` typed clients, `System.Text.Json`, xUnit with hand-written fakes (no mocking library), `TimeProvider` with `FakeTimeProvider` already in the test project (`AccountFactsServiceTests`, `KnownIssuesStateTests`). No new package. No new project.

- Typed clients: https://learn.microsoft.com/dotnet/core/extensions/httpclient-factory#typed-clients
- `TimeProvider` and `ITimer`: https://learn.microsoft.com/dotnet/standard/datetime/timeprovider-overview
- `UIElement.IsVisibleChanged`: https://learn.microsoft.com/dotnet/api/system.windows.uielement.isvisiblechanged
- `System.Version` comparison: https://learn.microsoft.com/dotnet/api/system.version

## 2. Runtime and deployment

Windows 10 19041+ desktop, x64 and native arm64. Three channels, identities unchanged from `CLAUDE.md`.

### Deployment — Identity & Signing

| Field | Value |
|---|---|
| Target | Microsoft Store (MSIX) and GitHub release (Velopack), same tag |
| Store ID | `9NMJCS390KWB` |
| Identity name | `626LabsLLC.RoRoRoBlox` |
| Package family | `626LabsLLC.RoRoRoBlox_wz1chhb2h2v4a` |
| Publisher CN | `CN=177BCE59-0966-4975-9962-10E36652141F` |
| Publisher display name | `626Labs LLC` (frozen, no space) |
| Version policy | four parts, fourth always `0`: `1.41.0.0`; csproj `<Version>`, `Package.appxmanifest` and the tag agree; only `finalize-store-build.ps1` syncs the first two |
| Tag scheme | `v1.41.0.0`; `release.yml` drafts the Velopack release and signs `roblox-compat.json` |
| Velopack packId | `RORORO` (frozen) |
| Repo slug | `estevanhernandez-stack-ed/ROROROblox` |
| Signing | Store signs the Store MSIX; sideload uses `dev-cert.pfx`, never the same key; CI secrets unchanged |
| Runbook | `docs/store/release-playbook.md`; the agent drives through the Store MSIX build, the Partner Center click is Este's |

Nothing in this cycle touches the manifest, a capability, the compat feed or the plugin contract. `ci.yml` green on main before the tag, both architectures.

## 3. Architecture overview

```
                       ┌──────────────────────── Core (no WPF) ────────────────────────┐
 clientsettingscdn ───►│ RobloxUpdateProbe.GetUpdateStateAsync ── RobloxVersion parse  │
 (already called)      │        ▲ also called by the batch gate (MainViewModel)        │
                       │                                                                │
 api.status.io ───────►│ RobloxStatusApi ─► RobloxStatusSnapshot ─► RobloxStatusEval   │
 (new host, no cookie) │        ids and integers only, no text kept                    │
                       │                                                                │
 games.roblox.com ────►│ GamePulseApi (≤50 ids) ─► GamePulseService (in-memory, TTL)   │
 (already called)      └───────▲────────────────────────────────────▲──────────────────┘
                               │ asks only when told                │ interest sets
                       ┌───────┴──────── App ───────────────┐       │
 MainWindow visible? ─►│ RobloxFeeds (coordinator, 30 s     │   LibraryViewModel
 client attached ─────►│ timer, single flight, schedule)    │   AccountPanelViewModel
 launch failed (net) ─►│   Update state · Status reading    │   (on-screen entries only,
 settings switch ─────►│            │ Changed               │    window visible only)
                       └────────────┼───────────────────────┘
                                    ▼
        contested ─┐   RobloxNoticeResolver (pure)        hold banner (row 2): untouched
   known issues ───┼─►  order: contested > outage >       Bloxstrap warning (row 5): untouched
          drift ───┘    known issue > drift > update
                                    ▼
                    MainViewModel.RobloxLine ─► one element in the row 3 strip
                    (top notice, its actions, "+N more" inline list)
```

Three rules hold the shape together.

- **Nothing in a launch path awaits a feed.** The batch gate keeps its own probe call with its own 8 s timeout, as today. The coordinator is told what the gate saw; the gate never asks the coordinator.
- **The coordinator is the only caller of the status client and the only idle caller of the probe.** One place decides timing, so "nothing at startup, nothing in the tray, one request for the whole app" is one class to test.
- **Remote data picks a sentence and never supplies one.** The status DTOs have no text property. §6.1.

## 4. The update check tells the truth

Implements `prd.md > The update check tells the truth`. Core only. Lands first and can ship alone.

### 4.1 Version comparison

`RobloxUpdateProbe.DiffersFromLatest` (`Core/Diagnostics/RobloxUpdateProbe.cs:145-147`) compares text. It is replaced by a comparison of parsed versions.

```csharp
internal enum VersionMatch { Unknown, Same, LocalOlder, LocalNewer }

internal static VersionMatch Compare(string? local, string? latest)
// RobloxVersion.TryParseInstalled on BOTH sides: it strips spaces and turns commas into dots,
// so "0, 748, 0, 7481526" and "0.748.0.7481526" parse to the same System.Version.
// Either side blank or unparseable => Unknown. Unknown never votes.
```

`RobloxVersion.TryParseInstalled` (`Core/KnownIssues/RobloxVersion.cs`) already exists, is already tested, and is not changed. The probe gains a reference to it.

One behaviour moves on purpose: a local string that is present but unreadable used to count as a difference (text never matched) and now counts as unknown. That is the PRD's line "never holds a batch because it failed to read something".

### 4.2 Update state

`IRobloxUpdateProbe` gains one member. Two fakes implement the interface (`Discord/DiscordTestHarness.cs:523`, `MainViewModelTests.cs:2267`); both get the member in the same commit.

```csharp
public enum RobloxUpdateKind { Unknown, Current, Pending }
public enum RobloxUpdateDirection { None, Newer, Older }
public sealed record RobloxUpdateState(
    RobloxUpdateKind Kind,
    RobloxUpdateDirection Direction = RobloxUpdateDirection.None,
    Version? Latest = null)
{ public static RobloxUpdateState Unknown { get; } = new(RobloxUpdateKind.Unknown); }

Task<RobloxUpdateState> GetUpdateStateAsync(CancellationToken ct = default);
RobloxUpdateState CompareInstalledTo(Version latest);   // local reads only, no request, never throws
```

`CompareInstalledTo` applies rules 3 to 5 below against an endpoint answer the caller already holds. The coordinator uses it after a client attaches (§5.2).

Rules, in order:

1. Handler read and newest-install read both blank: `Unknown` (Roblox not installed; no notice, no hold).
2. Endpoint answer blank or unparseable: `Unknown`.
3. Compare each local side with the endpoint. Every readable side `Same`: `Current`.
4. Any readable side differs: `Pending`. Direction comes from the handler read when it is readable and differs (it is what a launch will run), otherwise from the newest install. `LocalOlder` gives `Newer`; `LocalNewer` gives `Older` (a rollback).
5. No side readable: `Unknown`.

`IsUpdatePendingAsync` stays and becomes `(await GetUpdateStateAsync(ct)).Kind == RobloxUpdateKind.Pending`. `IsInstallerRunning` and `IsUpdateChurnActive` are untouched.

The probe logs one Debug line per call with the three strings and the result (`client-version check: handler {H}, installed {I}, latest {L} -> {Kind} {Direction}`). Version numbers are not personal data, and today the probe never logs its own answer, which is why the false hold went unseen.

### 4.3 The batch gate

`MainViewModel.DecidePreWarmAsync` (`MainViewModel.cs:2753-2780`) keeps its order (strap, churn, version) and its catch-all. Its third step calls `GetUpdateStateAsync`, passes `Kind == Pending` to `PreWarmGate.Decide`, and hands the state to the coordinator (`RobloxFeeds?.ReportUpdateState(state)`), so the coordinator holds the endpoint's answer even when the gate saw the update first. The first client of the batch installs the update and attaches; the attach re-read (§5.2) turns the state to `Current` at about the moment the hold banner drops, so the notice does not appear behind it saying something that has just stopped being true. `PreWarmGate` itself is not changed.

Result for the six-alt user: on a current client `Decide` returns `LaunchAllNow`, no "Roblox is updating" banner, and no `Pre-warm: holding the batch` log line.

### 4.4 The drift banner's read is pinned, not repaired

`RobloxCompatChecker` (`Core/RobloxCompatChecker.cs`) is not edited. `CheckAsync()` takes no argument and reads the installed version through a static with no seam, and on CI there is no Roblox, so a test that called it would pass for the wrong reason. The pin is therefore two guards on the mechanism:

- A test asserts `Version.TryParse("0, 748, 0, 7481526", out _)` is false. That one line is why the banner cannot fire.
- A source fence over `RobloxCompatChecker.cs` asserts the installed string still reaches `Version.TryParse(installed, out` unmodified, and that the file contains neither `RobloxVersion.` nor a comma-to-dot `Replace`. Whoever repairs the banner deletes the fence on purpose, with Este's ruling in hand.

`DiagnosticsCollector`'s copy of the installed-version scan is left alone (a listed discrepancy in `CLAUDE.md`).

## 5. The feeds coordinator

Implements the timing lines of `prd.md > The update notice comes before the launch` and `prd.md > Roblox's status, only when it is not fine`, and `prd.md > Nothing moves for the six-alt user`.

### 5.1 `RobloxFeeds`

`src/ROROROblox.App/Feeds/RobloxFeeds.cs`, `internal sealed`, `IDisposable`, singleton. Modelled on `Updates/UpdateAvailability.cs`: a `TimeProvider` timer, a `SemaphoreSlim` single flight per lane, a `Changed` event that may fire on any thread, never throws, never on the startup path.

```csharp
public RobloxFeeds(
    Func<IRobloxUpdateProbe> probeFactory,      // a fresh typed client per attempt, as the known-issues refresh does
    Func<IRobloxStatusApi> statusFactory,
    IBloxstrapDetector straps,
    TimeProvider? time = null,
    ILogger<RobloxFeeds>? log = null,
    FeedOptions? options = null);

public event EventHandler? Changed;
public RobloxUpdateState Update { get; }        // Unknown until an answer; Unknown again after a failed read
public RobloxStatusReading Status { get; }      // None when off, never read, or older than 30 min

public void Start();                            // called once, from RunStartupChecksAsync; creates the timer, reads nothing
public void SetWindowVisible(bool visible);     // pushed from MainWindow through the view model; thread-safe
public DateTimeOffset? ReadsAllowedFrom { get; }   // first-visible + settle; the pulse service takes the same instant
public void SetStatusEnabled(bool enabled);     // the off switch; false clears Status at once
public void NoteClientAttached();               // from IRobloxProcessTracker.ProcessAttached
public void RequestEarlyStatusRead();           // from a connection-kind launch failure
public void ReportUpdateState(RobloxUpdateState state);   // what the batch gate saw
internal Task TickAsync();                      // the timer body; tests await it
```

It has its own 30-second timer and does not ride `MainViewModel.PeriodicTick`. `PeriodicTick` is internal and a test cannot fire it (`IdleSettingsTests.cs:107` says so); this class has to be tested against a fake clock. Recorded in §17.

### 5.2 The schedule is a pure function

`src/ROROROblox.App/Feeds/FeedSchedule.cs`. Everything the PRD says about "when" lives here and is tested without a timer.

```csharp
internal sealed record FeedOptions(
    TimeSpan Tick, TimeSpan SettleAfterShown,          // 30 s, 30 s
    TimeSpan UpdateInterval,                           // 30 min
    TimeSpan StatusInterval, TimeSpan StatusRefusedInterval,                // 10 min, 60 min
    TimeSpan StatusEarlyReadSpacing, TimeSpan StatusTrust);                 // 60 s, 30 min

// Everything Decide needs, as plain values, so a test builds one by hand.
internal sealed record FeedClock(
    DateTimeOffset? FirstVisibleAt, bool Visible, bool StrapHandlesLaunches, bool StatusEnabled,
    DateTimeOffset? UpdateLastAttempt,
    DateTimeOffset? StatusLastAttempt, bool StatusLastRefused, bool StatusEarlyReadRequested);

internal static FeedDue Decide(DateTimeOffset now, FeedClock clock, FeedOptions o);
// FeedDue is a [Flags] enum: None, Update, Status.
```

| Rule | Source |
|---|---|
| Nothing is due until `SettleAfterShown` has passed since the first time the window was visible. | PRD: never in the startup burst, first read no sooner than 30 s |
| Nothing is due while the window is not visible. | PRD: no read while hidden in the tray |
| Update is due when never attempted, or when `UpdateInterval` has passed since the last attempt. | PRD: every 30 minutes while visible |
| Update is never due while a strap handles launches, and a pass that sees a strap sets the state to `Unknown`, so a notice read before the strap arrived does not stay. | PRD: not shown under a strap |
| Status is due when enabled and never attempted, or when `StatusInterval` has passed since the last attempt. After a refusal (HTTP 403 or 429) the interval is `StatusRefusedInterval`. | PRD: every 10 minutes; §6.5 |
| An early read is honoured once, when `StatusEarlyReadSpacing` has passed since the last attempt. | PRD: at most once a minute |
| On the window becoming visible, `Decide` runs at once instead of waiting for the tick. | PRD: once when the window comes back, if the answer is old |

"Visible" means the main window is shown and not minimised. A minimised window is on the taskbar and nobody is reading it. **(default — confirm on next interactive run)**

**The attach re-read is not a schedule rule, because it is not a request.** While the state is `Pending`, `NoteClientAttached` and every timer pass call `probe.CompareInstalledTo(state.Latest)`: two local file-version reads against the endpoint answer already in hand. It runs whether or not the window is visible, costs no network, and turns the state to `Current` within one pass of the install finishing, which covers the PRD's 60 seconds even when RoRoRo was minimised for the launch. While the state is anything else, an attach does nothing: the startup re-attach scan raises `ProcessAttached` before the first read, the state is `Unknown` then, and nothing is read.

### 5.3 Wiring

The coordinator follows `UpdateAvailability`'s wiring, step for step, so there is one pattern in the composition root and not two.

- **Registered** in `App.xaml.cs` beside `UpdateAvailability`: `services.AddSingleton<RobloxFeeds>(…)` with `sp.GetRequiredService<IRobloxUpdateProbe>` and `sp.GetRequiredService<IRobloxStatusApi>` wrapped as factories.
- **Handed to the view model as a property**, where `UpdateAvailability` is (`App.xaml.cs:816`): `vm.RobloxFeeds = …; vm.GamePulse = …;`. The setter subscribes to `Changed` and marshals through `_ui.Invoke`; `StopPeriodicRefresh()` unsubscribes. No constructor parameter, so no test construction site changes.
- **Started** in `RunStartupChecksAsync` beside `UpdateAvailability.Start()` (`App.xaml.cs:956`), after reading `GetRobloxFeedsEnabledAsync` and calling `vm.SetRobloxFeedsEnabled`. `Start` creates the timer and reads nothing. The settle period is measured from the first visibility push, not from `Start`, so a modal that delays `RunStartupChecksAsync` only makes the first read later.
- **Disposed** at shutdown beside `UpdateAvailability` (`App.xaml.cs:3720`), together with the pulse service.
- **Visibility.** `MainWindow.xaml.cs` subscribes `IsVisibleChanged` and `StateChanged` and calls `vm.SetMainWindowVisible(IsVisible && WindowState != Minimized)`. The view model forwards it to the coordinator and keeps it for its panels (§8.5). `Window.IsVisible` is a dependency property and cannot be read from the timer thread, which is why it is pushed and not polled.
- **Attach.** Where the tracker is wired in `App.xaml.cs`: `tracker.ProcessAttached += (_, _) => feeds.NoteClientAttached();`. The event fires off the UI thread; the method does two local reads and never touches the UI.
- **Early status read.** `MainViewModel.LaunchAccountAsync`, the `case LaunchResult.Failed failed:` arm (`:2198-2201`): when `failed.Kind` is `NoConnection`, `TimedOut` or `RobloxUnavailable`, call `RobloxFeeds?.RequestEarlyStatusRead()`. Nothing is awaited.
- **The switch.** `MainViewModel.SetRobloxFeedsEnabled(bool)` calls `RobloxFeeds?.SetStatusEnabled` and `GamePulse?.SetEnabled` and recomputes the line. `SettingsPage` already holds the view model and pushes through it, as `OnAlwaysShowRecycleToggle` does; it gains no constructor parameter.

### 5.4 Failure behaviour

A failed update read sets `Update` to `Unknown`: the notice goes away and comes back on the next good read. A failed status read changes nothing until the last good answer is 30 minutes old, then `Status` reads `None`. Expiry is evaluated on every tick, including while hidden, so a stale line cannot be waiting when the window returns. Each lane logs its outcome at Debug, once per read, no payload (`roblox-status read: {Outcome}`), from the `FeedOutcome` the client returns (§6.1).

## 6. Roblox's status

Implements `prd.md > Roblox's status, only when it is not fine` and the status lines of `prd.md > Every read fails to silence`.

### 6.1 `RobloxStatusApi`

`src/ROROROblox.Core/Feeds/IRobloxStatusApi.cs`, `RobloxStatusApi.cs`. Template: `Core/KnownIssues/KnownIssuesFeed.cs` (a non-Roblox host, one constructor, per-request timeout).

```csharp
// Core/Feeds/FeedOutcome.cs, shared by the status and pulse clients, the fakes and the silence tests.
public enum FeedOutcome { Ok, Refused, HttpError, Timeout, Network, Shape }   // Refused = 403 or 429

public sealed record RobloxStatusRead(FeedOutcome Outcome, RobloxStatusSnapshot? Snapshot);   // Snapshot only when Ok

public interface IRobloxStatusApi
{ Task<RobloxStatusRead> ReadAsync(CancellationToken ct = default); }      // never throws

public sealed class RobloxStatusApi : IRobloxStatusApi
{ public RobloxStatusApi(HttpClient http, ILogger<RobloxStatusApi> log); }   // exactly one constructor
```

The result record is the shape `KnownIssuesFetch(Kind, Document, Problems)` already uses (`KnownIssuesFeed.cs:16-19`). The schedule needs `Refused`, and the log line needs the rest.

- `GET https://api.status.io/1.0/status/59db90dbcdeb2f04dadcf16d`. The page id is a constant. No query, no body, no header beyond `User-Agent: RORORO/<version>` and what the handler adds.
- Per-request timeout 15 s (linked token), the value `KnownIssuesFeed` and `AccountFactsApi` use. Response capped at 512 KB, twice the known-issues cap, because the largest archived answer from any page is 44 KB and today's is 5.6 KB: read through a length-limited stream, over the cap is `Shape`.
- Any exception, any non-success status, any parse failure maps to an outcome. The method never throws.

The DTOs are the guarantee that no third-party text reaches the window:

```csharp
private sealed record Envelope(Result? Result);
private sealed record Result(Component[]? Status);
private sealed record Component(string? Id, Container[]? Containers);
private sealed record Container(string? Id, [property: JsonPropertyName("status_code")] int? StatusCode);
```

No `name`, no `status`, no `details`, no incident or maintenance type. An id is kept only if it is 24 hex characters. A reflection test asserts that every DTO in the file has no `string` property other than `Id`, and that `RobloxStatusSnapshot` and `RobloxStatusReading` hold no `string` at all.

```csharp
public sealed record RobloxStatusSnapshot(IReadOnlyList<StatusCell> Cells);
public readonly record struct StatusCell(string ComponentId, string ContainerId, int Code);
```

Registration, in `RobloxHttpClients.cs` beside the two cookie-carrying clients so the no-cookie rule is asserted from the real graph:

```csharp
services.AddHttpClient<IRobloxStatusApi, RobloxStatusApi>(client => { /* UA: RORORO/<version>, as the others */ })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false });
```

`TypedHttpClientRegistrationTests` gains: both new interfaces in `TypedHttpClient_Resolves_WithExactlyOneApplicableCtor` (an `[InlineData]` and a registration line each); a new theory, `AnonymousClients_PrimaryHandler_HasUseCookiesFalseAndNoRedirects`, over the real `ConfigureServices` graph for the status and pulse clients (the existing `CookieCarryingClients_*` theories keep their name and their two clients); both in the User-Agent theory; and a one-constructor fact for each.

### 6.2 The watch table and the evaluator

`src/ROROROblox.Core/Feeds/RobloxStatusWatch.cs`, `RobloxStatusEvaluator.cs`. Pure.

| Row shown | Group (component id) | Row (container id) |
|---|---|---|
| Website | Player `5f31ce9f6f386704befc1f81` | `5f31cdef5f2c7c04bbdc4fbf` |
| Website | User `5f31ce906e9c9104c05543d1` | `5f31cdef5f2c7c04bbdc4fbf` |
| Games | Player `5f31ce9f6f386704befc1f81` | `5f31ce3530ca5f04bcd3ee2c` |
| Avatar | Player `5f31ce9f6f386704befc1f81` | `60047c5629a6230534c2300a` |
| GameJoin | Creator `5f31cebe1bc3b004bb012c2e` | `5f31ce5767272804b51e8046` |

Ids read from the live answer on 2026-11-26 (`probes/status-roblox-operational-2026-11-26.json`). The PRD's five rows, unchanged; Data Store and Asset Delivery stay out.

```csharp
public enum RobloxStatusRow { Website, Games, Avatar, GameJoin }
public enum RobloxStatusLevel { Operational, Maintenance, Degraded, PartialOutage, Outage, SecurityEvent, Unrecognised }
public readonly record struct WatchedCell(RobloxStatusRow Row, string ComponentId, string ContainerId);   // one line of the table above
public sealed record RobloxStatusReading(RobloxStatusRow? Worst, RobloxStatusLevel Level, int OtherRows)
{ public static RobloxStatusReading None { get; } = new(null, RobloxStatusLevel.Operational, 0); }

public static RobloxStatusReading Evaluate(RobloxStatusSnapshot snapshot, IReadOnlyList<WatchedCell> watch,
                                           Action<RobloxStatusRow>? onMissing = null);
```

- A shown row's level is the worst of its pairs. Two pairs of Website both degraded is one row, not "Website and 1 more".
- `Worst` is the highest-ranked row that is not operational; `OtherRows` counts the other shown rows that are not operational (0 to 3).
- A watched pair absent from the answer is unknown, contributes nothing, and calls `onMissing`. The coordinator logs it once per run at Warning with the row's own name (`roblox-status: watched row {Row} is not on the page`).

### 6.3 Levels

From Status.io's own table (`kb.status.io/developers/status-codes`), read 2026-11-26. Every code seen in thirteen archived real answers is on it.

| Code | Level | Rank | Sentence key |
|---|---|---|---|
| 100 | Operational | none | silence |
| 500 | Outage | 6 | `RobloxLine_Status_Outage` |
| 600 | SecurityEvent | 5 | `RobloxLine_Status_Security` |
| 400 | PartialOutage | 4 | `RobloxLine_Status_Partial` |
| 300 | Degraded | 3 | `RobloxLine_Status_Degraded` |
| any other | Unrecognised | 2 | `RobloxLine_Status_Problem` |
| 200 | Maintenance | 1 | `RobloxLine_Status_Maintenance` |

A missing code is unknown, not unrecognised: it shows nothing.

### 6.4 Fixtures

Recorded, unmodified, saved under `docs/cycles/2026-11-26-roblox-feeds/probes/` today; the build copies them to `src/ROROROblox.Tests/Fixtures/RobloxStatus/` with a `PROVENANCE.md`.

| File | What it is | What it proves |
|---|---|---|
| `status-roblox-operational-2026-11-26.json` | Roblox's page, live | all five watched pairs exist; every code 100; reading is `None` |
| `status-otherpage-incident-2022-03-28.json` | another Status.io page, Wayback `20220328131125` | one row id at 400 under one group and 300 under another; two incidents; eleven scheduled maintenances that change no code |
| `status-otherpage-disruption-maintenance-2022-11-29.json` | same page, Wayback `20221129143640` | a 500 and a maintenance under way (200) in one answer |
| `status-otherpage-degraded-2026-11-06.json` | a third page, Wayback `20261106064743` | eight degraded rows, and a field (`current_active`) that did not exist in 2022: the parser ignores what it does not know |

The evaluator tests pass each other-page payload with a watch table of that page's own ids. No payload is edited. The mapping from Roblox's ids to the four rows is proved by the first file. A synthetic answer is allowed only for what no real one can show (a missing `status_code`, an oversized body, a non-hex id) and each is named `synthetic-*`.

`scripts/capture-roblox-status.ps1` saves a dated copy of the live answer. If Roblox has an incident before the tag, its capture joins the fixtures as the first real Roblox incident answer.

### 6.5 Terms and load

Read 2026-11-26, from the service's own pages and not from memory.

- The public status API is documented as the thing to give "your API consumers" for "a simple GET request to query the current status" (`kb.status.io/developers/public-status-api`). No key, no stated limit on that page.
- Status.io's terms (`status.io/tos`, effective 2016-03-10) forbid interfering with or disrupting the service and reaching non-public parts of it. Nothing in them forbids reading a public status answer.
- No rate limit for the public status API was found in the knowledge base or by search. The answer is served through CloudFront (`X-Cache: Hit from cloudfront`, `Age` 19 s and 120 s on two reads a minute apart), so repeat reads are answered at the edge.
- Roblox's own creator docs link to the page by this id.

The PRD's stop condition is not met. Worst case, every one of about 6,500 installs open and visible at once is 11 requests a second against a CDN; real concurrency is a small fraction of that. The schedule already limits each install to one read per ten minutes, only while visible, with a 60-minute interval after a 403 or 429. No explicit permission was asked for, and Este ruled against a courtesy note (2026-11-26).

## 7. One Roblox line

Implements `prd.md > One Roblox line` and the wording lines of the update and status stories.

### 7.1 The resolver

`src/ROROROblox.App/Notices/RobloxNoticeResolver.cs`. Pure: inputs in, state out, no WPF.

```csharp
internal enum RobloxNoticeKind { Contested, Outage, KnownIssue, Drift, Update }
internal enum RobloxNoticeTone { Warning, Info }

internal sealed record RobloxLineInputs(
    string ContestedText,            // "" when not contested
    RobloxStatusReading Status,
    string KnownIssueText,           // "" when nothing to show or dismissed
    string DriftText,                // always "" unless Este rules "repair" (§7.8)
    RobloxUpdateState Update,
    bool HoldBannerShowing,          // MainViewModel.RobloxUpdating
    bool HasMultipleAccounts,
    bool Compact);

internal sealed record RobloxNotice(RobloxNoticeKind Kind, string Text, RobloxNoticeTone Tone, bool WithLink);
internal sealed record RobloxLineState(RobloxNotice? Top, IReadOnlyList<RobloxNotice> More);

internal static RobloxLineState Resolve(RobloxLineInputs inputs);
```

- Order is the enum's order. `Top` is the first that is true; `More` is the rest, in order.
- `Update` is present when `Kind == Pending` and the hold banner is not showing. `WithLink` is true for `Newer` only. Under a strap the coordinator never reads, so the state is `Unknown` and the notice is absent without the resolver knowing why.
- `Outage` is present when `Status.Worst` is not null.
- Tone: Contested, Outage and Drift are `Warning`; KnownIssue and Update are `Info`. Every notice carries the same glyph, so tone is never the only signal. **(default — the mock gate may change it)**
- The Bloxstrap warning and the hold banner are not inputs to the order. They stay where they are.

`KnownIssuesNoticeModel.SetSuppressed` hides the known-issues notice while the name is contested (`MainViewModel.cs:5744`, `:5773`; the only two call sites). Its comment gives the reason: the contested warning "must not compete for attention". Under a one-line rule it cannot compete, so the resolver owns precedence and `SetSuppressed` is deleted with both call sites: a known issue under a contested name is counted in "more" instead of vanishing. Three things move with it in the same commit: `KnownIssues/MainViewModelKnownIssuesTests.cs:46-68` (two tests that assert the text is empty under contested become two that assert it is in `More`), `KnownIssuesNoticeModelTests.cs:87-90` (deleted with the member), and the stale comments at `KnownIssuesNoticeModel.cs:13-15` and `MainWindow.xaml:2181-2184`. The model's `Dismiss` is unchanged, so a notice hidden behind a higher one is not used up; it is used up only when someone dismisses it, from the line or from the inline list.

### 7.2 View model

`MainViewModel` gains `RobloxLine` (a small `RobloxLineViewModel`, `src/ROROROblox.App/Notices/RobloxLineViewModel.cs`): `Top` and `More` as `RobloxNoticeViewModel` items, `HasNotice`, `MoreCount`, `MoreLabel`, `MoreSpokenName`, `IsExpanded`. A notice view model exposes `Text`, `Tone`, and four booleans the template binds visibility to: `ShowContestedActions`, `ShowKnownIssueActions`, `ShowStatusLink`, `ShowWhatChanged`. The commands are the existing ones (`CloseRobloxForMeCommand`, `RetryMutexCommand`, `OpenKnownIssuesCommand`, `DismissKnownIssueNoticeCommand`) plus two new ones that call `_shellOpener.Open` with a constant:

- `OpenRobloxStatusCommand`: `https://status.roblox.com`
- `OpenRobloxReleaseNotesCommand`: `https://create.roblox.com/docs/release-notes`

Both addresses are constants in `Core/Feeds/RobloxLinks.cs`. Nothing remote ever contributes to an address RoRoRo opens.

`Recompute()` runs on the UI thread whenever an input changes: `SetContested`, the known-issues model's change, `RobloxUpdating` flipping, `Accounts.CollectionChanged`, `IsCompact` changing, `RobloxFeeds.Changed`, the switch, and a culture change. `IsExpanded` resets to false when `MoreCount` reaches zero. `ContestedBannerText` and `KnownIssueNoticeText` stay as properties, because code and tests read them; the window stops binding them. `RobloxCompatBanner` and its row are untouched (§7.8).

### 7.3 XAML

`MainWindow.xaml`, row 3 strip. The contested child (`:2121-2140`) is replaced by the line and the known-issues child (`:2185-2213`) is removed. Rows 2, 4 and 5 are not touched. The strip's collapse trigger (`:2081-2093`) swaps its `ContestedBannerText` and `KnownIssueNoticeText` conditions for one on `RobloxLine.HasNotice`.

One `DataTemplate`, `RobloxNoticeTemplate`, draws a notice and is used twice: for `Top` (a `ContentControl`) and for each item of `More` (an `ItemsControl`, shown while `IsExpanded`).

```
▲  <text, wraps>                         [Close Roblox for me] [Retry]      contested
▲  <text>                                [Roblox status]                    outage
▲  <text>                                [See known issues] [Dismiss]       known issue
▲  <text>                                                                   drift
▲  <text>                                [What changed]                     update
                                                                 [+2 more]  toggle, right of the top notice
```

- Buttons are declared, not generated, each with a fixed rank and a `Visibility` bound to its `Show…` boolean: `CtaButtonStyle` for Close Roblox for me, `SecondaryButtonStyle` for Retry, See known issues, Roblox status and What changed, `GhostButtonStyle` for Dismiss. A rank cannot be switched by a trigger (`ButtonRankFenceTests`), which is why the template is fixed slots and not a list of actions.
- The more-count is a `ToggleButton` with `PanelExpanderToggleButtonStyle` (`ControlStyles.xaml:329`), the rank the account panels' expander already uses, `IsChecked` bound two-way to `IsExpanded`, content bound to `MoreLabel` ("+2 more"). There is no ghost toggle rank, and a `Button` style on a `ToggleButton` throws at load. **(the rank is a default — the mock gate may pick `SecondaryToggleButtonStyle`)**
- Brushes are `DynamicResource` only: `RowExpiredAccentBrush` for `Warning` text, `WhiteBrush` for `Info` text, on the strip's `RowBgBrush`. No new slot, no new derived brush, no hex literal. Text uses `BodyFontSize`.
- The glyph is the same `▲ ` run the drift and Bloxstrap banners use.
- Collapsed, the line is one notice tall whatever `MoreCount` is. The toggle sits in its own `Auto` column, so it takes width from the text; the "exactly as tall" render test runs at the default width and at 380 px with strings short enough not to wrap at either, so it measures the rule and not a wrap.

### 7.4 Strings

English in `Properties/Strings.resx` by hand; the six cultures through `docs/store/translations/ui-<culture>.json` and `scripts/gen-culture-resx.py`. "Roblox" and "Launch all" (the button's own label) stay as the app writes them.

| Key | English |
|---|---|
| `RobloxLine_Update_Many` | Roblox has an update. The next launch installs it, and Launch all holds your other accounts until it is done. |
| `RobloxLine_Update_One` | Roblox has an update. The next launch installs it. |
| `RobloxLine_WhatChanged` | What changed |
| `RobloxLine_Status_Outage` | Roblox reports an outage: {0} |
| `RobloxLine_Status_Partial` | Roblox reports a partial outage: {0} |
| `RobloxLine_Status_Degraded` | Roblox reports degraded performance: {0} |
| `RobloxLine_Status_Security` | Roblox reports a security issue: {0} |
| `RobloxLine_Status_Maintenance` | Roblox reports maintenance under way: {0} |
| `RobloxLine_Status_Problem` | Roblox reports a problem: {0} |
| `RobloxLine_Row_Website` | Website |
| `RobloxLine_Row_Games` | Games |
| `RobloxLine_Row_Avatar` | Avatars |
| `RobloxLine_Row_GameJoin` | Joining games |
| `RobloxLine_Status_AndMore` (plural) | {0} and {1} more |
| `RobloxLine_StatusLink` | Roblox status |
| `RobloxLine_More` | +{0} more |
| `RobloxLine_More_Name` (plural) | {0} more Roblox notices |
| `Library_Pulse_Updated` | updated {0} |
| `SettingsPage_RobloxFeeds` | Show Roblox's status and game activity |
| `SettingsPage_RobloxFeeds_Description` | Checks Roblox's status page and reads how many people are in your saved games. The status check goes to Status.io, a third party that hosts the page for Roblox. Nothing about you or your accounts is sent. |

The row name follows a colon so it never has to agree with a verb or take a case ending, which is what makes one sentence per level workable in Russian, Polish and German. `{0} playing` already exists (`Library_Playing`), as does the `{0} · {1}` joiner (`Library_Meta_Played`). A rollback uses the update sentence without the link. No sentence mentions bans, captchas, verification, sessions, an account or RoRoRo.

### 7.5 Accessibility

- The PRD asks for reading order, and the tab order gives it: the top notice's text, its buttons, then the toggle. No live region is added. WPF raises nothing when a text changes; an announcement would need a `LiveRegionChanged` raise from code-behind, and no main-window banner does that today. Left for a cycle that does it for every banner.
- The toggle's `AutomationProperties.Name` binds `MoreSpokenName` ("2 more Roblox notices").
- Every button's content is a `{loc:Loc}` key, which the naming fence counts as a name. The toggle's content is bound, so it needs the explicit name above. `AccessibleNamingFenceTests`' unnamed ceiling stays at 1; its scanned floor rises with the new controls in the same commit.
- Expanded notices are ordinary controls in the tab order. Nothing lives in a tooltip.

### 7.6 Compact

At 380 px the line uses the same template. With `Compact` true the resolver always picks `RobloxLine_Update_One`: the batch clause is the long half and compact has the least room. **(default — confirm on next interactive run)** The existing notices keep their text and wrap as they do in v1.40.

`LabelWrapMeasurementTests` gains a surface for the line at the default width and at 380 px, seven cultures, asserting at most two lines for every status sentence with its longest row name and counted suffix, and for both update sentences at the widths they are used. The contested, known-issue and drift texts are not held to two lines; they are v1.40's strings and this cycle does not shorten them.

### 7.7 Fences and pinned tests that move

Same commit as the change that moves them:

- `KnownIssues/MainViewModelKnownIssuesTests.cs` and `KnownIssuesNoticeModelTests.cs`, with the suppression (§7.1).
- `ExpiredRowRedundancyTests` counts `▲` runs; the line's template adds one. `BannerRecipeTests` and `Rendering/BannerPairRenderTests` pin rows 4 and 5, which do not move, and stay as they are.
- `AccessibleNamingFenceTests` scanned floor rises; `TypeLadderFenceTests` and `ThemedStatusColourTests` counts are re-recorded with direction.
- `DismissOrderFenceTests`: the known-issue Dismiss still follows its text inside the template.
- `ToggleWiringFenceTests`: the more-count binds `IsChecked` and has no `Click`.
- `NoRawXamlProseFenceTests`, `LocKeyParityFenceTests`, `CultureKeyParityFenceTests`: every new string is a key in all seven files.
- Two new named contrast pairs, `RowExpiredAccentBrush` on `RowBgBrush` and `WhiteBrush` on `RowBgBrush`, measured at AA in all four themes. The contested line has drawn the first pair since it shipped and it has never been measured: the comment at `MainWindow.xaml:2123-2130` records a ratio against `Bg`, a different slot from the strip's fill. If a theme fails, the tone for that state changes at the mock gate; the brush does not.

### 7.8 The drift banner waits for its ruling

Row 4 and `RobloxCompatBanner` are left exactly as they are. The banner cannot fire, so in practice there is still one Roblox line, and the three tests that pin it stay green untouched. `Drift` keeps its place in the resolver's order with an input that is always empty, so the order is defined and tested.

**Ruled 2026-11-26: leave.** Nothing in this cycle touches the banner, its row, its string or the checker. The table stays as the price of changing that later:

| Ruling | Change |
|---|---|
| leave | none |
| retire | delete row 4's border, `RobloxCompatBanner`, `LoadCompatBannerAsync`'s banner write, the `Drift` kind and the drift half of the three pinned tests |
| repair | parse the installed string in `RobloxCompatChecker` and delete the §4.4 fence; wire `RobloxCompatBanner` into `DriftText`; delete row 4's border; rewrite the three pinned tests around the Bloxstrap banner alone; and commit to moving the feed's ceiling with each Roblox release |

Until a repair, nothing can make the banner and the line draw at once. A repair that forgot to remove row 4 would draw drift twice; the repair row above names it.

## 8. Game pulse

Implements `prd.md > Saved games show their pulse` and the pulse lines of `prd.md > Every read fails to silence`.

### 8.1 `GamePulseApi`

`src/ROROROblox.Core/Library/IGamePulseApi.cs`, `GamePulseApi.cs`. Its own interface, so the cost of a fake lands in one new test file.

```csharp
public interface IGamePulseApi
{
    const int MaxIdsPerRequest = 50;     // measured 2026-11-26: 50 answers 200, 51 answers 400 code 9
    Task<GamePulseRead> ReadAsync(IReadOnlyCollection<long> universeIds, CancellationToken ct = default);
}
public sealed record GamePulseRead(FeedOutcome Outcome, GamePulseAnswer? Answer);          // Answer only when Ok
public sealed record GamePulseAnswer(IReadOnlyDictionary<long, GamePulseReading> Found);   // an asked id not in Found is gone
public readonly record struct GamePulseReading(int Playing, DateTimeOffset? UpdatedUtc);
```

- `GET https://games.roblox.com/v1/games?universeIds=<comma list>`, documented in Roblox's Games API reference (https://games.roblox.com/docs). The host is already on the privacy page.
- The caller never passes more than 50; the method throws `ArgumentException` on 51 so a batching bug fails in a test, not as a 400 in the field.
- One constructor `(HttpClient, ILogger<GamePulseApi>)`, 15 s per-request timeout (the facts client's value), an outcome on any failure, never throws on the network. A 200 without a `data` array is `Shape`, not "every game is gone".
- DTO reads `id`, `playing`, `updated` only. `playing` outside 0 to 100,000,000 or `updated` before 2006 or more than a day ahead is dropped for that game.
- Registered beside the status client with the same `UseCookies = false`, `AllowAutoRedirect = false` handler. The call carries no cookie and no account, so eight accounts and one game is one question.

### 8.2 `GamePulseService`

`src/ROROROblox.Core/Library/IGamePulseService.cs`, `GamePulseService.cs`. Singleton, in memory only. Nothing is written to disk, so there is no file to route through `AppDataRoot` and nothing to salvage.

```csharp
public interface IGamePulseService
{
    event EventHandler<GamePulseChangedEventArgs>? Changed;      // universe ids; may fire on any thread
    GamePulseView? TryGet(long universeId);                      // null = no line
    void SetInterest(object owner, IReadOnlyCollection<long> universeIds);   // what this owner has on screen now; empty removes the owner
    void SetEnabled(bool enabled);                               // false: clear readings, raise Changed, ask nothing; interest is kept
    void AllowReadsFrom(DateTimeOffset instant);                 // nothing is asked before this; unset means never
}
public readonly record struct GamePulseView(int? Playing, DateTimeOffset? UpdatedUtc);

public GamePulseService(Func<IGamePulseApi> apiFactory, TimeProvider? time = null, ILogger<GamePulseService>? log = null);
```

Registered as a singleton beside `LibraryCatalog` (`App.xaml.cs:1415`), with the typed client behind a factory so each read gets a fresh one, as the coordinator does. `IDisposable`; disposed with the coordinator.

**The startup gate.** The main's panel is open at load and a persisted open state is restored (§0, correction 13), and someone can open the Library in the first seconds. So interest alone would put a request a quarter second after the window shows. `AllowReadsFrom` is set once, by the view model, to the coordinator's `ReadsAllowedFrom` (first visible plus the 30 s settle). Interest declared earlier is recorded and acted on when the instant passes. Until `AllowReadsFrom` is called at all, the service asks nothing, so a test or a code path that forgets the gate fails closed.

| Rule | Value |
|---|---|
| Nothing is asked before `AllowReadsFrom`'s instant. | PRD: first new read of any kind at least 30 s after the window is shown |
| A change of interest is acted on after a 250 ms quiet period, so one screenful is one request and a scroll is not one request per tick. | debounce 250 ms |
| A game is not asked about again within 5 minutes of its last attempt, whoever asks, and whether that attempt succeeded or failed. | `MinReadAge` 5 min |
| While any owner has interest, a 60 s timer re-asks for the games on screen whose reading is 5 minutes old. No interest, no timer. | refresh while open |
| `TryGet` returns `Playing` as null once the reading is 15 minutes old. `UpdatedUtc` stays. `Changed` is raised for a game when its count expires. | `CountTrust` 15 min |
| A game absent from a good answer has no line; it is asked about again after 5 minutes. | gone |
| A failed read changes no reading and counts as the attempt for the games it asked about. `Refused` holds every read for 5 minutes. | failure |
| More than 50 ids are sent as consecutive requests of 50, never in parallel. | chunking |
| One reading per game, shared: the Library and every open panel read the same entry. | PRD: same numbers everywhere |

Interest is the union across owners. An owner is a `LibraryViewModel` or an `AccountPanelViewModel`; each replaces its own set, and an empty set removes the owner's key (a new `LibraryViewModel` is built each time the Library page is built, so a kept key would hold every past one). Readings are kept for the session (a few dozen entries); only interest gates the asking. With the switch off, interest is still recorded, so turning it back on asks for what is on screen at the next timer pass without anyone having to scroll.

### 8.3 Where a universe id comes from

`LibraryEntry` (`Core/Library/LibraryEntry.cs:30`) gains `long? UniverseId`. There are two places an entry is built, and both fill it: `LibraryCatalog` (the join) and `AccountPanelViewModel.Synthesize` (`:491-500`, a Recent entry made from a session row). The fill order is the same in both:

1. a saved game: `FavoriteGame.UniverseId`;
2. any other entry whose place matches a saved game: that game's universe id, a join that costs nothing;
3. otherwise `_icons.TryGet(placeId)?.UniverseId`, the map the icon cache already keeps;
4. otherwise null, and the entry has no pulse yet.

For case 4, `LibraryViewModel.NoteOnScreen` (`:398-403`) changes its guard: it asks the icon cache when the entry has no icon **or no universe id**. Today it returns early for any entry with an icon, which is every saved private server. On `IconResolved` the view model re-reads the id and adds the entry to its interest. The panel's `RequestVisibleIcons` gets the same guard.

That request is the existing icon lane: one paced lookup per place through the facts worker, once per run, kept in `game-icons.json` afterwards. It is the one new traffic the pulse causes beyond its own batched read, it goes to hosts already on the privacy page, and it only happens for a private server or Recent entry on screen whose place no saved game shares. The universe-only request (an entry that has an icon and no universe id) is pulse traffic, so it is made only while the switch is on and after the pulse's startup gate has passed; the panel's load path stays `requestIcons: false`. An entry with no icon is asked about exactly as in v1.40.

### 8.4 Library

`Games/LibraryItem.cs`, `LibraryViewModel.cs`, `GamesPage.xaml(.cs)`.

- `LibraryViewModel` is built by hand in `BuildLibraryPage` (`App.xaml.cs:1868`) and takes `IGamePulseService` there.
- `LibraryItem` gains `PulseCountText` and `PulseUpdatedText` (each empty when absent) and `PulseText` (the two joined). It re-raises them on the service's `Changed`, on a culture change, and wherever `MetaText` re-raises today.
- **Accessible name.** `LibraryItem` has `TooltipText`, `PlayName`, `PlayAsName` and `MoreName`, and no name for the entry itself. It gains `EntryName` (name, players, updated; the pulse halves dropped when absent), bound to `AutomationProperties.Name` on the item container through the two lists' `ItemContainerStyle`, and `TooltipText` gains the pulse. The button names are unchanged.
- **On screen.** Today the tile and row roots raise `Loaded` and the page calls `_vm.NoteOnScreen(item)`. The list recycles containers, and a recycled container does not raise `Loaded` again; the grid and the list are two `ListBox`es over the same items (`GamesPage.xaml:497`, `:536`), and switching view collapses one without unloading it. The page therefore tracks four events on the template root: `Loaded` (add), `Unloaded` (remove), `DataContextChanged` (remove the old item, add the new), `IsVisibleChanged` (add or remove). `LibraryViewModel` keeps the resulting set and hands its universe ids to `SetInterest`. This also fixes icon requests for recycled rows, which use the same call.
- **Window visible.** The root's `IsVisibleChanged` already follows a hidden window. The page adds its window's `StateChanged`, so minimised means an empty interest set and restored re-declares the tracked set. `Detach()` clears it.
- **Layout.** The grid tile is 144 px wide with 8 px of padding (`GamesPage.xaml:169`), so 128 px of text at `MetaFontSize`. "81,827 playing · updated 2 days ago" does not fit on one line there in English, and German and Russian run longer. In the tile the pulse is two lines: the count, then "updated…". `Height="246"` and `ItemSize="144,246"` (`:503`) grow together by two meta lines. The list row and the panel entry have the width for the joined line. **(defaults — the mock gate rules; §12)**
- No spinner and no placeholder. The line is absent until an answer arrives.
- A "From Roblox" search result keeps its own count from the search answer. It is not a saved game and is not asked about.

### 8.5 Account panel

`ViewModels/AccountPanelViewModel.cs`. `PanelEntry` is an immutable positional record with a hand-written copy constructor (`:29-37`), drawn by a plain `ItemsControl`, at most three entries per list. A panel is not collapsed by default: the main's (or the first row's) is open at load and a persisted open state is restored.

- `PanelEntry` gains one member, `Pulse` (a `GamePulseView?`), which the copy constructor copies. The universe id is already on its `Entry`. `PulseText` is computed from `Pulse` by the shared formatter and re-raised by the existing `RaiseTimesChanged`.
- The panel view model sets `Pulse` when it builds an entry (`TryGet`) and, on the service's `Changed`, replaces the affected entries in place (`list[i] = entry with { Pulse = … }`), marshalled through its existing `Marshal`.
- The panel declares interest for its entries while it is expanded and the main window is visible (the view model's `SetMainWindowVisible`, §5.3), and an empty set otherwise. A collapsed panel asks for nothing. An open panel at startup declares interest at once and the service's gate holds the request.
- `PanelEntry.MetaText` joins parts with a hard-coded `" · "`; the pulse is one more part, joined the same way.

### 8.6 Formatting

One formatter, `Games/PulseText.cs`, used by both surfaces:

```csharp
internal static string Count(GamePulseView? pulse, bool isPrivateServer);    // "" when absent or a private server
internal static string Updated(GamePulseView? pulse);                        // "" when absent
internal static string Joined(GamePulseView? pulse, bool isPrivateServer);   // both, either, or ""
```

No clock parameter: `AccountSummary.RelativeAgo` reads its own clock and its own test override (`AccountSummary.cs:1263-1267`), and the formatter uses the same one for the year rule below.

- Count: `Loc.Format("Library_Playing", n.ToString("N0", TranslationSource.Instance.CurrentCulture))`, the call the "From Roblox" group already makes. Zero reads "0 playing".
- Updated: `Loc.Format("Library_Pulse_Updated", when)`, where `when` is `AccountSummary.RelativeAgo(updated)` for anything inside eleven months. `RelativeAgo` is the ladder "last played" uses and ends in `DisplayDate.ShortMonthDay` past seven days, which has no year. "Last played" is always recent; a game's last update can be years old and would read "updated Mar 3". Past eleven months the formatter uses a new `DisplayDate.MonthYear(DateTimeOffset, CultureInfo?)` ("Mar 2023"), the fourth member of `DisplayDate` and built the way the other three are.
- Both present: joined with `Library_Meta_Played` (`{0} · {1}`). Count expired: the updated half alone. Neither: empty.
- A private server: the updated half only, never the count.

Measured on eleven games on 2026-11-26: "updated" ranged from 5.7 hours (Doors) to 17 days (Tower of Hell), nine of eleven inside four days. The date tells games apart. Whether it moves on a publish, a settings change or both is not established and cannot be without a game to publish; the label says "updated", which is true either way.

## 9. Every read fails to silence

Implements `prd.md > Every read fails to silence`. One table, one test class per lane that walks every row.

| Condition | Update | Status | Pulse |
|---|---|---|---|
| no network, refused, DNS | `Unknown`: no notice | nothing changes; line clears at 30 min | nothing changes; count clears at 15 min |
| timeout (8 s / 15 s / 15 s) | same | same | same |
| non-success status | same | same; 403 or 429 backs off to 60 min | same; 429 holds 5 min |
| empty body, not JSON, wrong shape | same | same | same |
| over the size cap | not applicable | shape failure | not applicable |
| answer good, a watched pair or a game missing | not applicable | that row is unknown; Warning once per run | that game has no line |

In every row: no dialog, no banner, no red text, no toast; no account's health, presence or facts change; no launch waits. Logging is one Debug line per read with the outcome and no payload. Nothing from any feed is written to a file.

A structural test backs the "no launch waits" line: `RobloxLauncher` and the launch methods of `MainViewModel` contain no `await` on `RobloxFeeds`, `IRobloxStatusApi` or `IGamePulseService` (a source fence over the named methods), and `Core/RobloxLauncher.cs` references none of the three types.

## 10. The off switch

Implements `prd.md > The off switch`.

- `SettingsBlob` (`Core/AppSettings.cs:745-775`): `bool RobloxFeedsEnabled = true`. A settings file written by v1.40 has no such key and reads as true, which is "on for upgrades".
- `IAppSettings`: `GetRobloxFeedsEnabledAsync`, `SetRobloxFeedsEnabledAsync`. The four private fakes (`MainViewModelTests`, `RobloxLauncherTests`, `StreamerIdentityProviderTests`, `Discord/DiscordTestHarness`) get the pair in the same commit.
- `Preferences/SettingsPage.xaml`: `CheckBox x:Name="RobloxFeedsEnabledToggle"`, the row shape of `AlwaysShowRecycleToggle` (`:212-225`), with the description under it. `Checked` and `Unchecked` handlers, never `Click`. The name satisfies `SettingsReachabilityTests`' control edge.
- The handler follows `OnAlwaysShowRecycleToggle` (`SettingsPage.xaml.cs:870-890`): save, then push through the view model it already holds, `_mainViewModel.SetRobloxFeedsEnabled(v)` (§5.3). Off clears the status reading and every pulse at once and raises both change events, so the line and the pulse text disappear without a restart. From that call on, the schedule never returns `Status` and the pulse service asks nothing, though it keeps recording what is on screen.
- On again: the status clock starts as if the window had just been shown (first read after the 30 s settle), and the pulse asks for what is on screen at its next timer pass. One request each, no burst, and nobody has to scroll.
- The update notice is not behind the switch.
- Proof for "zero requests when off": a test starts the coordinator and the pulse service disabled, advances a fake clock two hours with the window visible and interest declared, and asserts the stub handlers saw no request.

There is no settings export, so nothing else to do (§0, correction 8). Account export is untouched.

## 11. Privacy, listing and letter text

Implements `prd.md > The privacy page and the listing stay true`. Same PR as the code.

**`docs/PRIVACY.md`**

- `:83`, the Roblox endpoints row: "`clientsettingscdn.roblox.com` for the current client version before a batch launch" becomes "…for the current client version, checked while the main window is open and before a batch launch". The same row's games wording gains "and, while the Library or an account panel is open, how many people are in a saved game and when it last changed".
- A new row in the table at `:78-92`: host `api.status.io`; what it is ("Status.io, the third-party service that hosts Roblox's status page at status.roblox.com. Not Roblox, GitHub or Microsoft."); what is sent ("the request itself: your IP address and `RORORO/<version>`"); what is never sent ("no cookie, no account, no identifier"); when ("about every ten minutes while the main window is open, never while RoRoRo is in the tray"); how to stop it ("Settings, Show Roblox's status and game activity").
- `:21`, `:22`, `:76` and `:96` are the four sentences that say who RoRoRo talks to. Each is re-read and made true: "Roblox-owned endpoints", "only to", and "no other network connections" all need the status host named. `:96`'s "third-party SDKs" stays true (a GET is not an SDK); "no analytics, no telemetry" stays true.

**Listing** (`docs/store/listing-copy.md:131-132` and the six `listing-copy-<lang>.md` sheets, plus a `listing-translations-1.41-delta.json`): one sentence added to the privacy paragraph. English: "RoRoRo also checks Roblox's public status page, which a third party (Status.io) hosts for Roblox, sends nothing about you to do it, and has a switch in Settings to turn it off." Dated provenance note on each sheet. `WHATS_NEW_VERSION` bumped in the listing export. The three-surface audit (short description, long description, features) runs against the release notes and is recorded in the submission packet. No image changes.

**Reviewer letter** (`docs/store/reviewer-letter-1.41.0.0.md` and `.paste.txt`, from `reviewer-letter.md`): under "What a code review would find": one new host, `api.status.io`, a public read of Roblox's status page, no cookie, no account data, on by default with a switch; the client-version check now also runs while the window is open; saved games read a player count and a date from the Games endpoint the app already calls.

**Elsewhere.** `README.md:116` ("RoRoRo talks to Roblox, to GitHub…, and to the alert services you connect yourself") gains the status page. The About page has no network sentence; nothing to change. #300: the hosted privacy page is handed this text on release day.

Whether editing the long description disturbs the variant B experiment is Este's answer before submission (§20).

## 12. The mock gate

Implements the two mock lines in the PRD and the match-the-mock rule. Before any XAML: one HTML page of rendered options under `docs/cycles/2026-11-26-roblox-feeds/mocks/`, captured to PNG in brand and flatline, the format the last cycle used.

- The line in each of its five states, at default width and at 380 px.
- The line with "+2 more", collapsed and expanded.
- Tone options: all five in the warning colour, or the split in §7.1.
- The grid tile, the list row and the panel entry with the pulse line; and the private-server variant (date only).
- Three tile options: two more meta lines (count, then updated; the spec's default), one more line that wraps, or the count alone in the tile with the date kept for the tooltip and the list.
- The more-count as `PanelExpanderToggleButtonStyle` and as `SecondaryToggleButtonStyle`.

Este rules on the renders. The built line and tiles are drawn off screen by rendering tests (the `UpdateButtonRenderTests` pattern) and compared with the picks before the items close.

## 13. Data model

Nothing new is persisted except one boolean.

| Thing | Where it lives | Lifetime |
|---|---|---|
| `RobloxFeedsEnabled` | `settings.json`, `SettingsBlob` | persisted, default true |
| `RobloxUpdateState` | `RobloxFeeds`, memory | until the next read |
| `RobloxStatusReading` | `RobloxFeeds`, memory | 30 min after the last good read |
| `GamePulseReading` per universe | `GamePulseService`, memory | session; count trusted 15 min |
| interest sets | `GamePulseService`, memory | while a surface is open and visible |
| `LibraryEntry.UniverseId` | computed at the join | never stored |
| known-issue dismissals | `known-issues-dismissed.json` | unchanged |

No store gains a field, so `StoreWrongShapeTests` and `StoreSalvageTests` gain no rows. No `AppDataRoot` site is added, so `AppDataRootPathPinTests.SiteTable` is unchanged.

## 14. File structure

New files marked `+`, changed files `~`. Paths under `src/` unless noted.

```
ROROROblox.Core/
├── Diagnostics/
│   ├── ~ IRobloxUpdateProbe.cs          GetUpdateStateAsync
│   ├── ~ RobloxUpdateProbe.cs           parsed comparison, state rules, Debug outcome line
│   └── + RobloxUpdateState.cs           kind, direction, record
├── Feeds/
│   ├── + IRobloxStatusApi.cs
│   ├── + RobloxStatusApi.cs             one ctor; DTOs with no text
│   ├── + RobloxStatusSnapshot.cs        cells: two ids and a code
│   ├── + RobloxStatusWatch.cs           the five watched pairs, the four rows
│   ├── + RobloxStatusEvaluator.cs       pure: snapshot + watch table -> reading
│   ├── + FeedOutcome.cs                 Ok, Refused, HttpError, Timeout, Network, Shape
│   └── + RobloxLinks.cs                 the two addresses RoRoRo opens
├── Library/
│   ├── + IGamePulseApi.cs
│   ├── + GamePulseApi.cs                <= 50 ids per request
│   ├── + IGamePulseService.cs
│   ├── + GamePulseService.cs            interest, debounce, TTLs, in memory
│   ├── ~ LibraryEntry.cs                UniverseId
│   └── ~ LibraryCatalog.cs              fills UniverseId at the join
├── ~ IAppSettings.cs                    RobloxFeedsEnabled pair
└── ~ AppSettings.cs                     SettingsBlob parameter and accessors

ROROROblox.App/
├── Feeds/
│   ├── + RobloxFeeds.cs                 the coordinator
│   └── + FeedSchedule.cs                pure: what is due now
├── Notices/
│   ├── + RobloxNoticeResolver.cs        pure: inputs -> top and more
│   └── + RobloxLineViewModel.cs         line and notice view models
├── Games/
│   ├── + PulseText.cs                   the one formatter
│   ├── ~ LibraryItem.cs                 PulseText, accessible name
│   ├── ~ LibraryViewModel.cs            on-screen set, interest, window visibility
│   └── ~ GamesPage.xaml(.cs)            pulse line; Loaded, Unloaded, DataContextChanged
├── ViewModels/
│   ├── ~ MainViewModel.cs               RobloxLine, two commands, gate reports state, early-read hook
│   └── ~ AccountPanelViewModel.cs       PanelEntry.Pulse, Synthesize fills UniverseId, interest while expanded
├── KnownIssues/
│   └── ~ KnownIssuesNoticeModel.cs      SetSuppressed deleted
├── Localization/
│   └── ~ DisplayDate.cs                 MonthYear
├── Preferences/
│   └── ~ SettingsPage.xaml(.cs)         the switch
├── Properties/
│   └── ~ Strings.resx (+ six generated culture files)
├── ~ MainWindow.xaml(.cs)               the line in the row 3 strip; visibility pushed to the view model
├── ~ RobloxHttpClients.cs               status and pulse clients, no-cookie handlers
└── ~ App.xaml.cs                        registrations, Start after Show, attach hook

ROROROblox.Tests/
├── ~ RobloxUpdateProbeTests.cs          field strings, both spellings, unknown never votes
├── + RobloxCompatDriftPinTests.cs       the comma form does not parse; source fence on the checker
├── + Feeds/RobloxStatusApiTests.cs      shape, cap, no-text reflection test
├── + Feeds/RobloxStatusEvaluatorTests.cs
├── + Feeds/FeedScheduleTests.cs
├── + Feeds/RobloxFeedsTests.cs          fake clock; zero requests when off or hidden
├── + Feeds/FeedSilenceTests.cs          the §9 table, every row, three lanes
├── + Library/GamePulseApiTests.cs
├── + Library/GamePulseServiceTests.cs
├── + Notices/RobloxNoticeResolverTests.cs
├── + Rendering/RobloxLineRenderTests.cs
├── + Rendering/LibraryPulseRenderTests.cs
├── + NoFeedInLaunchPathFenceTests.cs
├── + Fixtures/RobloxStatus/             four recorded answers, PROVENANCE.md
├── ~ TypedHttpClientRegistrationTests.cs
├── ~ Rendering/LabelWrapMeasurementTests.cs
├── ~ ExpiredRowRedundancyTests.cs, AccessibleNamingFenceTests.cs
├── ~ KnownIssues/MainViewModelKnownIssuesTests.cs, KnownIssuesNoticeModelTests.cs
└── ~ the four IAppSettings fakes, the two IRobloxUpdateProbe fakes

(repo root)
├── scripts/+ capture-roblox-status.ps1
├── docs/~ PRIVACY.md, features.md, feature-ledger.md, decisions.md, architecture.md
├── docs/store/~ listing-copy*.md, + listing-translations-1.41-delta.json, + reviewer-letter-1.41.0.0.*
├── docs/store/translations/~ ui-<culture>.json (six)
└── docs/cycles/2026-11-26-roblox-feeds/  scope.md, prd.md, spec.md, probes/, mocks/
```

## 15. Data flow

**An update lands while RoRoRo is open.** The 30 s timer fires; the window is visible and thirty minutes have passed; the coordinator resolves a probe, which reads the handler's file version (`0, 748, 0, 7481526`) and the endpoint (`0.749.0.…`), parses both, and returns `Pending, Newer`. `Changed` fires on a pool thread; `MainViewModel` marshals, the resolver puts `Update` at the top (nothing higher is true), and the line reads "Roblox has an update…" with What changed. The person launches one account. Roblox installs; the client attaches; `NoteClientAttached` re-reads the two local versions against the 749 it already holds, both parse to 749, the state is `Current`, the line clears. No request is made, so it clears the same way with RoRoRo minimised. If instead they press Launch all first, the gate makes its own read, holds the batch, and the hold banner takes the notice's place until it ends.

**Roblox degrades game joins.** A launch fails with `TimedOut`. The row says so in the v1.38 words. The view model calls `RequestEarlyStatusRead`. On the next pass, if a minute has passed since the last status read, the client fetches the answer, keeps 21 cells of two ids and a code, the evaluator finds the Creator and Game Join pair at 300, and the line reads "Roblox reports degraded performance: Joining games" with a Roblox status button. Ten minutes later the pair reads 100 and the line clears. If the network dies instead, the line stays for at most thirty minutes and then clears.

**The Library opens on forty saved games.** Eighteen tiles are realised and raise `Loaded`; the view model's set holds eighteen entries; after 250 ms the service sends one request with their universe ids. The answer fills eighteen readings and raises `Changed`; each item re-raises `PulseText`. Scrolling realises six more: one request for six. Opening an account panel whose three games are among them asks for nothing. Five minutes later, with the page still open, one request refreshes what is on screen. The window is closed to the tray: interest empties, the timer stops, nothing is asked.

## 16. Tests and fences

Unit and rendering tests are listed in §14. The acceptance tests the PRD names explicitly:

| PRD line | Test |
|---|---|
| the two field strings answer "no update", and the test fails on today's code | `RobloxUpdateProbeTests`: handler `0, 748, 0, 7481526`, endpoint `0.748.0.7481526`, expects `Current`; written and seen failing before the fix |
| one version behind, both spellings | four cases: comma or dotted local against dotted or comma endpoint |
| unreadable is unknown | a garbage local string with a readable other side decides by the other side; both garbage is `Unknown` |
| drift banner unchanged | `RobloxCompatDriftPinTests` |
| nothing at startup, nothing in the tray, one request for the app | `RobloxFeedsTests` on a fake clock; `GamePulseServiceTests` with interest declared at time zero and no request until the gate instant, and none ever when `AllowReadsFrom` was not called |
| the notice clears within 60 s of the attach | `RobloxFeedsTests`: pending, window not visible, attach, local versions now current, state is `Current` with zero requests |
| no third-party text | reflection test over the status DTOs and reading |
| status against a recorded real answer | `RobloxStatusEvaluatorTests` over the four fixtures |
| fixed order, nothing used up, same height | `RobloxNoticeResolverTests`, `RobloxLineRenderTests` |
| one screenful is one request; scroll asks once | `GamePulseServiceTests` with a counting fake |
| seven languages without clipping | `LabelWrapMeasurementTests`, `LibraryPulseRenderTests` |
| pixel-identical with no notice | `RobloxLineRenderTests` against a v1.40 capture taken before any XAML changes |
| zero requests with the switch off | §10 |

Live acceptances (manual, per the no-automation-against-roblox.com rule). Six, and two of them need real accounts, which a test data folder does not have:

| Acceptance | Where | Whose hands |
|---|---|---|
| absent when current: no update notice for a full check cycle | dev build, `RORORO_DATA_DIR` set | agent |
| status silent on Roblox's live page | same | agent |
| pulse matches Roblox's own page for a saved game | same, one game saved into the test folder | agent |
| **a batch of two or more on a current client shows no "Roblox is updating" banner and writes no hold line** | the release candidate on real accounts | Este |
| present when pending: the notice shows before any launch, one launch installs, the notice clears | a real waiting update; either build | whoever is at the machine the day Roblox ships |
| **a batch with a real update waiting still launches the first account, holds the rest and releases them** | the release candidate on real accounts, same day | Este |

The last two need the build ready before Roblox's next release; builds have landed on a Monday or Tuesday for eight weeks running.

`CLAUDE.md`'s test counts are re-recorded at the release.

## 17. Key technical decisions

1. **Status rows are matched by id pair, and the DTOs carry no text.** Decided because row names repeat across groups and a status belongs to the pair, and because an unsigned third-party feed must not be able to put a word on screen. Tradeoff: the ids are constants; if Roblox rebuilds its page the line goes silent (and says so in the log once) until a release updates them. The remote compat feed is a possible later home for the table.
2. **The coordinator owns its own timer.** `PeriodicTick` exists so nothing else stands up a second timer, but it cannot be fired from a test, and every hard rule in this cycle is a timing rule. `UpdateAvailability` and the known-issues feed already made the same choice. Tradeoff: one more 30-second wake-up in the process.
3. **The pulse and the status get their own typed clients.** `IRobloxApi` has 15 fakes in 13 files. Tradeoff: two more registrations and one more theory in `TypedHttpClientRegistrationTests`.
4. **The release-notes link is the index.** Measured: notes trail the build by one to three days, eight of eight. Tradeoff: one more click for the reader. Revisit when two releases in a row show the per-version page live before the endpoint reports the version; the release-day acceptance records both times.
5. **Fixtures are real answers from another page, not a written Roblox incident.** Tradeoff: the first real Roblox incident answer is still unseen; the capture script and the warn-once log line are the hedge.
6. **The drift banner stays in its row, and its read is fenced.** Este ruled "leave" on 2026-11-26. Moving it into the line now would rewrite three pinned tests for a banner that cannot fire, and two of the three possible rulings would then throw that work away. `Drift` keeps its place in the resolver's order with an empty input. Tradeoff: until the ruling, "one Roblox line" is true because the banner is dead, not because the layout forbids two. §7.8 prices each ruling.
7. **Interest sets, not `Loaded` alone.** A recycled container never raises `Loaded` again, and a hidden window never raises `Unloaded`. Tradeoff: three events and a visibility push per page instead of one handler.
8. **A failed update read hides the notice.** The strict reading of "fails to silence". Tradeoff: on a flaky connection the notice can blink out between reads. The alternative (keep the last endpoint answer and re-compare locally) is cheap to add if that shows in the field.

## 18. Dependencies and external services

| Service | Call | Auth | Limit | Docs |
|---|---|---|---|---|
| Roblox client settings | `GET clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer` | none | not stated | in the app since v1.7 |
| Status.io public status API | `GET api.status.io/1.0/status/59db90dbcdeb2f04dadcf16d` | none | not stated; CDN-cached | https://kb.status.io/developers/public-status-api/ · https://kb.status.io/developers/status-codes/ · https://status.io/tos |
| Roblox Games API | `GET games.roblox.com/v1/games?universeIds=` | none | 50 ids per request (measured) | https://games.roblox.com/docs |
| Roblox release notes | opened in the browser, never fetched | n/a | n/a | https://create.roblox.com/docs/release-notes |
| Roblox status page | opened in the browser, never fetched | n/a | n/a | https://status.roblox.com |

No key, no secret, no new package.

## 19. Build order

For `/checklist`. Item 2 gates items 6 and 13. The first halt is before any XAML.

1. **Baselines.** v1.40 main-window captures (no notice, default and compact) for the pixel bar; startup time; the request log of a session left in the tray.
2. **The truthful check** (§4). Core and two fakes. Rides with v1.41 (ruled 2026-11-26), and is still its own commit.
3. **Status client, evaluator, fixtures** (§6).
4. **Pulse client and service** (§8.1, §8.2).
5. **Coordinator and schedule** (§5).
6. **Resolver and line view model** (§7.1, §7.2).
7. **Halt C1: the mock gate** (§12), and the Neb search result if it has come in (§20).
8. **The line in XAML**, with the known-issues suppression tests and the fences in §7.7.
9. **Pulse in the Library and the panel** (§8.3 to §8.6).
10. **The switch** (§10).
11. **Strings through the catalog**, six cultures, then the translation verifier on the delta.
12. **Privacy page, listing, letter, README** (§11).
13. **Halt C2: live acceptances** on the dev build with test data; then riders (task `8pm4lzbdv` rewritten, `0h7s65ljx` and `bxranna8z` closed on evidence, the theme dialog if it is ready) and the release lane.

## 20. Open issues

Owners named. None blocks `/checklist`.

| Issue | Owner | Needed by |
|---|---|---|
| **Is the false hold visible in the field?** Not answerable from this machine: Neb's share refused the read. On Neb: `Select-String -Path "$env:LOCALAPPDATA\ROROROblox\logs\rororoblox-*.log" -Pattern "Pre-warm: holding the batch" \| Measure-Object`. | Este, one command | halt C1 |
| **Does the long description edit touch what variant B varies?** | Este | before submission |
| **What "updated" tracks** (publish, settings, both). Unknown; the label is true either way. | open | none |
| **The first real Roblox incident answer** is unseen. `capture-roblox-status.ps1` records one when it happens. | agent, opportunistic | before the tag if one occurs |
| **Recycled containers and `Loaded`.** Stated from WPF's documented behaviour, not measured in this tree. Item 9 opens with a rendering test that scrolls a 100-entry list and counts the interest set. | agent | item 9 |
| **Data Store and Asset Delivery rows.** Left out. | Este | next interactive run |

## 21. Assumptions added by this spec

The PRD's sixteen stand. These continue the numbering; each is one sentence to overturn.

17. "Visible" means shown and not minimised, for the main window and for the Library's window.
18. The "more" count is a panel-expander toggle that opens an inline list, not a tooltip. *(Ruled B1 at C1, 2026-11-26.)*
19. The two new links are Secondary buttons; no link rank is added.
20. Contested, outage and drift use the warning colour; known issue and update use white; all five carry the glyph. *(Ruled A1 at C1, 2026-11-26.)*
21. In compact the update notice drops the batch clause. *(Held: `RobloxNoticeResolver.cs:70`; ruled F "as drawn" at C1.)*
22. The two-line bound applies to the status and update sentences only, at both widths.
23. "What changed" always opens the release-notes index.
24. After a 403 or 429 the status read waits 60 minutes; no jitter is added.
25. A failed update read hides the notice until the next good read.
26. Levels rank outage, security event, partial outage, degraded, unrecognised, maintenance.
27. Two watched pairs of one row count as one row.
28. In the grid tile the pulse is two lines (count, then updated) and the tile grows by two; the list row and the panel show it joined on one. *(Overturned 2026-11-26: C3 at C1, the tile shows the count only; the afternoon ruling made the panel count-only too. Banner item 4.)*
29. The pulse refreshes every five minutes while its surface stays open.
30. A "From Roblox" search result keeps its own count and gets no pulse.
31. The known-issues notice is no longer suppressed under a contested name; it is counted in "more". *(Held: `SetSuppressed` deleted in item 11.)*
32. The switch is labelled "Show Roblox's status and game activity" and sits with the other general switches. *(Held, with a closing period. Banner item 2.)*
33. After a client attaches, the notice clears from a local re-read against the endpoint answer already held; no request is made, and an attach while nothing is pending reads nothing.
34. (Ruled, no longer an assumption: the drift banner is left as it is.)
35. No live region is added to the line; reading order comes from the tab order.
36. A game last updated more than eleven months ago shows a month and a year.
37. A private server or Recent entry with an icon and no known universe costs one request through the existing icon lane, only while the switch is on.

## 22. Rulings

Put to Este as five calls when the spec was handed over; three ruled the same day, 2026-11-26 ("1 leave, 2 ride with it, 5 skip").

1. **The drift banner is left as it is.** Row 4, its string and `RobloxCompatChecker` are untouched; the fence in §4.4 stays; `Drift` keeps its place in the resolver's order with an input that is always empty. This closes the PRD's first open question.
2. **The false-hold fix rides with v1.41.** No release ahead of the cycle. Build item 2 is still its own commit.
5. **No courtesy note to Status.io.**

Not yet answered: the Neb log search (call 3) and whether the long-description sentence touches what variant B varies (call 4). Both stay in §20.
