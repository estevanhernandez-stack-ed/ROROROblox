# Metric Smoke Harness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn 16 of the metric-alert smoke list's 21 rows into one command.

**Architecture:** A non-shipping console tool under `tools/` that sets up real app state, drives the running app over its real plugin pipe as a consented reporter, captures what the app POSTs to a local stand-in webhook, and asserts against the app's own log. No production change.

**Tech Stack:** .NET 10, C# 14, Grpc.Net.Client over a named pipe, `System.Net.HttpListener`, xUnit for the harness's own unit tests.

**Spec:** [docs/superpowers/specs/2026-09-11-metric-smoke-harness-design.md](../specs/2026-09-11-metric-smoke-harness-design.md). Read §0 before starting — it is seven verified facts, and four of them are why this is small.

**Smoke list:** [../smoke-metric-alerts.md](../smoke-metric-alerts.md). Task 7 marks which rows the harness covers. It never ticks one: the harness passing is not the same as a human having run it.

## Global Constraints

- **`tools/MetricSmoke` must never reach the Store build or be referenced by the app.** `tools/CompatSigner` is the precedent: `OutputType=Exe`, referenced by nothing, invoked by path.
- **It writes Este's real profile.** Every file it touches gets the strategy in spec §1.4, and it reads `discord.dat` back after writing to confirm the swap landed before it sends anything (§1.5). A crash marker (§1.6) makes an interrupted run recoverable.
- **Never commit** `dev-cert.pfx`/`.cer`, `accounts.dat`, `consent.dat`, `discord.dat`, `notify.dat`, `webview2-data/`, `/plugins/`, `spike/`, or any `.ROBLOSECURITY` value. The harness's backups contain real webhook URLs — they go somewhere gitignored and are never committed. A pre-commit hook also rejects any absolute user-profile path in a committed file, so derive paths, never hardcode them.
- **No end-to-end automation against real roblox.com.** This harness talks to a local pipe and localhost. It launches no Roblox client and needs no account signed in.
- **Always name the solution explicitly: `ROROROblox.slnx`.**
- Baseline: `dotnet test ROROROblox.slnx -c Release` gives **2139 unit + 27 harness pass, 1 harness skip by design**.

## The decision Task 1 has to make

The harness needs four things from the app's own code: `AppSettings` and `DiscordConfigStore` (both in Core, both taking a file path), `DiscordConfig` (Core), and `ConsentStore` — which is in **App**, a WinExe WPF project.

**Try referencing App first.** It is zero production change. The tool would need `TargetFramework` `net10.0-windows10.0.19041.0` to match, and WPF comes along as a transitive dependency.

**If that fights the tool's build, move `ConsentStore` to Core instead.** Verified: its only usings are `System.IO`, `System.Security.Cryptography`, `System.Text.Json` and `System.Text.Json.Serialization` — it has no App dependency at all, and its sibling-in-kind `DiscordConfigStore` already lives in Core. The move costs a namespace change across 13 files, most of them a single `using`. Do NOT do this speculatively; do it only if the reference genuinely does not work, and say which you did and why.

## File Structure

| File | Responsibility |
| --- | --- |
| `tools/MetricSmoke/MetricSmoke.csproj` | Non-shipping Exe. |
| `tools/MetricSmoke/ProfileGuard.cs` | Backup, restore, crash marker, read-back verification. |
| `tools/MetricSmoke/LogTail.cs` | Read the app log from a recorded offset; recognise the three lines. |
| `tools/MetricSmoke/MetricReporter.cs` | Pipe client: `x-plugin-id`, `ReportMetric`. |
| `tools/MetricSmoke/WebhookCatcher.cs` | Local `HttpListener` capturing POSTed payloads. |
| `tools/MetricSmoke/Scenarios.cs` | The scenario table, one entry per covered smoke row. |
| `tools/MetricSmoke/Program.cs` | Orchestration and the report. |
| `src/ROROROblox.Tests/SmokeHarness/` | Unit tests for the four testable pieces. |

---

### Task 1: The tool, and the guard that protects the profile

**Files:**
- Create: `tools/MetricSmoke/MetricSmoke.csproj`, `tools/MetricSmoke/ProfileGuard.cs`
- Modify: `ROROROblox.slnx` (add the project so the tests can reference it)
- Test: `src/ROROROblox.Tests/SmokeHarness/ProfileGuardTests.cs`

**Interfaces:**
- Produces: `ProfileGuard` with `Task<ProfileGuard> AcquireAsync(string dataRoot, string backupRoot)`, `Task RestoreAsync()`, `Task<bool> VerifyDiscordSwapAsync(string expectedMineUrl, string expectedClanUrl)`, and a static `Task<bool> RecoverOrphanedAsync(string backupRoot)`.

**This is the task that decides whether the harness gets run twice**, so it comes first and it is the one with the most tests. It touches a real profile and its failure mode is a config pointing at a dead localhost webhook.

**The per-file strategy is spec §1.4 and is not yours to change:**

| File | Strategy |
| --- | --- |
| `consent.dat` | Self-cleaning. Grant, then `RevokeAsync`. No backup — the harness only ever adds and removes its own plugin id. |
| `settings.json` | Single key. Read the original `MetricAlertsEnabled`, restore that value. Never rewrite the file wholesale. |
| `metric-rules.json` | Harness-owned. Back up only if one already exists; restore it, or delete the harness's if there was none. |
| `discord.dat` | Full backup and restore. The only file needing it. |

- [ ] **Step 1: Write the failing tests**

Create `src/ROROROblox.Tests/SmokeHarness/ProfileGuardTests.cs`. Every test works against a temp directory standing in for the data root — never the real one.

```csharp
    [Fact]
    public async Task Acquire_WritesAMarker_AndRestore_RemovesIt()
    {
        // The marker is what makes an interrupted run recoverable. If it is absent, a crash is
        // indistinguishable from a clean exit and the next run has nothing to restore from.
        var guard = await ProfileGuard.AcquireAsync(_dataRoot, _backupRoot);
        Assert.True(File.Exists(Path.Combine(_backupRoot, ProfileGuard.MarkerFileName)));

        await guard.RestoreAsync();
        Assert.False(File.Exists(Path.Combine(_backupRoot, ProfileGuard.MarkerFileName)));
    }

    [Fact]
    public async Task Restore_PutsDiscordConfigBackByteForByte()
    {
        var original = await File.ReadAllBytesAsync(_discordPath);

        var guard = await ProfileGuard.AcquireAsync(_dataRoot, _backupRoot);
        await File.WriteAllBytesAsync(_discordPath, [1, 2, 3]);
        await guard.RestoreAsync();

        Assert.Equal(original, await File.ReadAllBytesAsync(_discordPath));
    }

    [Fact]
    public async Task Restore_PutsBackOnlyTheOneSettingsKey()
    {
        // Rewriting settings.json wholesale would discard anything the app wrote while the harness
        // ran, and the app rewrites that file on exit. Read-modify-write one key, or lose settings.
        await File.WriteAllTextAsync(_settingsPath,
            """{ "version": 1, "metricAlertsEnabled": false, "streamerMode": true }""");

        var guard = await ProfileGuard.AcquireAsync(_dataRoot, _backupRoot);
        await guard.SetMetricAlertsEnabledAsync(true);
        await guard.RestoreAsync();

        var json = await File.ReadAllTextAsync(_settingsPath);
        Assert.Contains("\"streamerMode\": true", json);
        Assert.Contains("\"metricAlertsEnabled\": false", json);
    }

    [Fact]
    public async Task AnAbsentRulesFile_IsDeletedOnRestore_NotLeftBehind()
    {
        Assert.False(File.Exists(_rulesPath));

        var guard = await ProfileGuard.AcquireAsync(_dataRoot, _backupRoot);
        await guard.WriteRulesAsync("[]");
        await guard.RestoreAsync();

        Assert.False(File.Exists(_rulesPath));
    }

    [Fact]
    public async Task AnExistingRulesFile_IsRestored_NotClobbered()
    {
        await File.WriteAllTextAsync(_rulesPath, "ORIGINAL");

        var guard = await ProfileGuard.AcquireAsync(_dataRoot, _backupRoot);
        await guard.WriteRulesAsync("[]");
        await guard.RestoreAsync();

        Assert.Equal("ORIGINAL", await File.ReadAllTextAsync(_rulesPath));
    }

    [Fact]
    public async Task AnOrphanedMarker_IsDetectedAndRestoredBeforeAnythingElse()
    {
        // The Ctrl-C case. This is spec section 1.6 and it is worthless untested.
        var original = await File.ReadAllBytesAsync(_discordPath);
        var guard = await ProfileGuard.AcquireAsync(_dataRoot, _backupRoot);
        await File.WriteAllBytesAsync(_discordPath, [9, 9, 9]);
        // No RestoreAsync — simulating a kill.

        var recovered = await ProfileGuard.RecoverOrphanedAsync(_backupRoot);

        Assert.True(recovered);
        Assert.Equal(original, await File.ReadAllBytesAsync(_discordPath));
    }

    [Fact]
    public async Task VerifyDiscordSwap_ReturnsFalse_WhenTheWriteDidNotLand()
    {
        // Spec section 1.5. The only risk with an audience is a test alert reaching the real clan
        // channel because the swap silently failed, so the runner must be able to detect that.
        var guard = await ProfileGuard.AcquireAsync(_dataRoot, _backupRoot);

        Assert.False(await guard.VerifyDiscordSwapAsync("http://localhost:9/mine", "http://localhost:9/clan"));
    }
```

Write the fixture (`_dataRoot`, `_backupRoot`, `_discordPath`, `_settingsPath`, `_rulesPath`) yourself, seeding a real `DiscordConfig` through `DiscordConfigStore` so the encrypted file is genuine rather than a stub.

- [ ] **Step 2: Run and watch fail**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~ProfileGuard"`
Expected: FAIL to compile — the project does not exist.

- [ ] **Step 3: Create the project**

Model `tools/MetricSmoke/MetricSmoke.csproj` on `tools/CompatSigner/CompatSigner.csproj`. Resolve the `ConsentStore` reference per "The decision Task 1 has to make" above — try referencing App, fall back to moving `ConsentStore` to Core only if it genuinely does not work.

Add the project to `ROROROblox.slnx` so the test project can reference it. Confirm this does not pull it into the Store build: `scripts/finalize-store-build.ps1` builds the App project specifically, so a solution entry is not a Store artifact — verify that rather than assuming it.

- [ ] **Step 4: Implement `ProfileGuard`**

Nothing here may throw past `RestoreAsync` — a restore that gives up halfway is worse than one that never started. Log what it did, and on a partial failure say which files were and were not restored.

- [ ] **Step 5: Run the tests**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~ProfileGuard"`
Expected: PASS, 7 tests.

- [ ] **Step 6: Commit**

```bash
git add tools/MetricSmoke/ ROROROblox.slnx src/ROROROblox.Tests/SmokeHarness/
git commit -m "feat(smoke): a guard that gives the profile back"
```

---

### Task 2: Reading the app's own log as a verdict

**Files:**
- Create: `tools/MetricSmoke/LogTail.cs`
- Test: `src/ROROROblox.Tests/SmokeHarness/LogTailTests.cs`

**Interfaces:**
- Produces: `LogTail` with `static LogTail OpenAt(string logPath)` capturing the current length, `Task<IReadOnlyList<string>> NewLinesAsync()`, and recognisers `IReadOnlyList<DeliveredAlert> Delivered(IEnumerable<string> lines)`, `int RoutedNowhereCount(...)`, `IReadOnlyList<string> SkewDrops(...)`. `DeliveredAlert` carries `Destination`, `Title`, `AccountCount`.

**Why an offset rather than a log-directory override.** The app takes one (`AppLogging.Configure`) but production passes none, and using it would need a production change. Recording the file's length before a scenario and reading only what is appended needs nothing from the app and works against the real rolling file.

The three lines to recognise are in spec §0.3. Read them out of `AlertDispatcher.cs` and `MetricReportSinkAdapter.cs` rather than copying them from the spec — the spec can drift and the code cannot.

- [ ] **Step 1: Write the failing tests**

Cover, against a temp file you append to: only lines after the recorded offset are returned; a delivered line is parsed into destination, title and count; the routed-nowhere line is counted and is NOT mistaken for a delivered one; a skew drop is recognised and names its metric; a rolled file (length shrinks) does not throw or return garbage; and a line that merely *contains* the word alert is not matched. That last one matters — a loose matcher turns any log noise into a green.

- [ ] **Step 2: Run and watch fail**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~LogTail"`

- [ ] **Step 3: Implement, then Step 4: run the tests, then Step 5: commit**

```bash
git commit -m "feat(smoke): read the app's log as a verdict, from an offset"
```

---

### Task 3: The reporter

**Files:**
- Create: `tools/MetricSmoke/MetricReporter.cs`
- Test: `src/ROROROblox.Tests/SmokeHarness/MetricReporterTests.cs`

**Interfaces:**
- Produces: `MetricReporter(string pipeName, string pluginId)` with `Task<bool> IsHostReachableAsync()` and `Task ReportAsync(string subjectId, string metricId, double value, DateTimeOffset observedAt)`.

**Spec §0.1 is why this is short.** The interceptor checks only that the method is in the capability map, that an `x-plugin-id` header is present, and that the consent lookup for that id grants the capability. No install, no manifest, no consent sheet. So this is a gRPC client over `NamedPipeClientStream` with one header.

Copy the channel construction from `src/ROROROblox.PluginTestHarness/EndToEndContractTests.cs` — it already connects a `Grpc.Net.Client` channel to a named pipe and is maintained. Do not invent a second way.

`IsHostReachableAsync` uses the ungated `GetHostInfo` as a liveness probe. Spec §4.3 flags an open question: confirm it answers before the startup gate modals, and report what you find.

- [ ] **Step 1: Write the failing tests** — that the pipe name and plugin id land where expected, that an unreachable host returns false rather than throwing, and that `ReportAsync` converts a `DateTimeOffset` to the unix-milliseconds the proto expects without drifting a millisecond.
- [ ] **Step 2: Run and watch fail.**
- [ ] **Step 3: Implement. Step 4: run. Step 5: commit** — `feat(smoke): a reporter the interceptor accepts`

---

### Task 4: The webhook catcher

**Files:**
- Create: `tools/MetricSmoke/WebhookCatcher.cs`
- Test: `src/ROROROblox.Tests/SmokeHarness/WebhookCatcherTests.cs`

**Interfaces:**
- Produces: `WebhookCatcher` with `static WebhookCatcher Start()` binding a free localhost port, `string MineUrl`, `string ClanUrl`, `Task<IReadOnlyList<CapturedPost>> DrainAsync(TimeSpan within)`, and `IAsyncDisposable`. `CapturedPost` carries the path (so Mine and Clan are distinguishable), the body, and the arrival time.

**This is what makes the payload rows assertable.** The log carries the alert's Title but not its Body (spec §0.4), so the observed value's formatting and masked-versus-real naming can only be proven by capturing what the app actually POSTed.

Bind port 0 and read back the assigned port rather than picking one — a hardcoded port is a harness that fails when something else is listening. Respond `204` like Discord does, so the app's own 404-means-dead-webhook logic is never triggered.

- [ ] **Step 1: Write the failing tests** — a POST to the Mine path is captured with its body; Mine and Clan are distinguishable; `DrainAsync` returns nothing rather than hanging when no post arrives; two posts arrive in order; the listener releases its port on dispose so a second run in the same process works.
- [ ] **Step 2: Run and watch fail. Step 3: Implement. Step 4: run. Step 5: commit** — `feat(smoke): catch what the app actually posts`

---

### Task 5: The scenarios and the runner

**Files:**
- Create: `tools/MetricSmoke/Scenarios.cs`, `tools/MetricSmoke/Program.cs`
- Test: `src/ROROROblox.Tests/SmokeHarness/ScenarioTableTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–4.
- Produces: a console app runnable as `dotnet run --project tools/MetricSmoke`, printing one line per covered smoke row and a final pass/fail, exiting non-zero on any failure.

**The scenario table is the deliverable.** One entry per covered row, each naming the smoke-list row it satisfies so the two cannot drift. The 16 covered rows, and what each asserts:

| Smoke row | Setup | Assertion |
| --- | --- | --- |
| A breach reaches the desktop toast | Rate rule, two reports across the window under the floor | one `Alert → Local` |
| A consented plugin can report at all | granted consent | `ReportAsync` returns without an RPC error |
| Unconsented is denied — revoked | grant then revoke | `PermissionDenied`, and no delivered line |
| Unconsented is denied — never declared | a second plugin id never granted | `PermissionDenied`, and no delivered line |
| The opt-in setting gates it | gate off, breaching report | no delivered line and no routed-nowhere line |
| The observed value renders legibly | Level rule on a 0.0–1.0 ratio, report `0.79` | captured body contains `0.79`, not `0` |
| Unrecognised `subject_id` still reaches the user | report against a random Guid | one delivered line |
| A clock-skewed reporter is visible | report stamped hours ahead | a skew-drop line naming the metric |
| A resetting counter fires no false breach | rising value then a lower one | no delivered line |
| Repeated breaches do not repeat | several breaching reports inside the cooldown | exactly one delivered line |
| The rules file is picked up live | write rules while running, then report | delivered line without a restart |
| Its absence is inert | no rules file, breaching report | no delivered line, no error |
| A malformed rules file does not take the app down | truncated JSON, then report | host still reachable, and a later valid file still works |
| Streamer mode masks the metric alert | streamer mode on | the Mine body carries the masked name, the Clan body the real one |
| The plugin pipe still binds | — | `IsHostReachableAsync` true |
| An unconfigured destination falls back | destination Phone with no phone credentials | one `Alert → Local` |

Two of those need care. **The negative assertions** — "no delivered line" — are false greens if the window is too short; spec §4.4 says key the wait to the cooldown rather than picking a number, and say what you chose. **Streamer mode** is a settings key like the gate, so the guard restores it the same way; check whether `ProfileGuard` needs a second single-key restore and extend it if so.

The runner's order is fixed: recover an orphaned marker first, acquire the guard, start the catcher, write and verify the Discord swap, **abort if the verification fails**, then run scenarios, then restore in a `finally` that also runs on Ctrl-C.

- [ ] **Step 1: Write the failing test** — `ScenarioTableTests` asserts every scenario names a row that exists in `docs/superpowers/smoke-metric-alerts.md`, and that the table's count matches the number of rows the smoke list marks as harness-covered. That is what stops the table and the list drifting.
- [ ] **Step 2: Run and watch fail. Step 3: Implement the table. Step 4: Implement the runner. Step 5: Run the unit tests.**
- [ ] **Step 6: Commit** — `feat(smoke): sixteen rows, one command`

---

### Task 6: Prove every assertion can fail

**Files:**
- Create: `docs/superpowers/smoke-harness-verification.md`

**Why this is a task and not a step.** Spec §5.1: a harness that passes when the feature is broken is worse than no harness, and a row never seen red is not a check. This task is the only thing standing between a green run and a false one.

**This needs the app running**, so it is the first task that does. Start the dev build, then for each of the 16 scenarios break the thing it checks and confirm that row — and ideally only that row — goes red. Breakages to use: toggle the gate off, delete the `AlertsRaised` subscription line in `App.xaml.cs`, revoke the consent grant, point the destination at nothing, make the rate rule's floor unreachable, stamp a report in the past instead of the future.

**The subscription one matters most.** That single line in `App.xaml.cs` is the link the whole feature hangs from and nothing in the suite tests it — deleting it leaves every test green while the feature is silently dead. If this harness catches that, it has justified itself on its own.

Record the result per row in `docs/superpowers/smoke-harness-verification.md`: the row, what you broke, and what you saw. **If a row cannot be made to fail, say so and mark it as not yet a check** rather than quietly counting it.

Restore every deliberate breakage. `git status` must be clean at the end, and say so in your report.

- [ ] **Step 1: Start the dev build and run the harness clean. Step 2: Break and verify, row by row. Step 3: Restore everything and confirm a clean tree. Step 4: Commit the verification record.**

```bash
git commit -m "test(smoke): every row proven red before it was trusted green"
```

---

### Task 7: Docs, and the smoke list's new shape

**Files:**
- Modify: `docs/superpowers/smoke-metric-alerts.md`
- Modify: `docs/features.md`
- Create: `tools/MetricSmoke/README.md`

- [ ] **Step 1: Mark the covered rows.** Each of the 16 gains a marker saying the harness covers it and naming the command. **Do not tick any of them** — the harness passing is not a human having run it, and the list's own key says a tick means it was done on a real machine. The five manual rows gain a line saying why no harness will ever cover them.
- [ ] **Step 2: Rewrite the setup section.** It currently walks a human through granting consent at the sheet and hand-writing a rules file. Most of that is now `dotnet run --project tools/MetricSmoke`. Keep the manual path for the five rows that still need it.
- [ ] **Step 3: Write the tool's README** — what it does, the one command, what it touches in the profile and how it gives it back, and how to recover from an interrupted run.
- [ ] **Step 4: Add a features-ledger line** for the harness, honest about it being local-only with CI unproven.
- [ ] **Step 5: Run the suite** — `dotnet test ROROROblox.slnx -c Release`. Docs edits can fail it; fences read the tree from disk.
- [ ] **Step 6: Commit** — `docs(smoke): sixteen rows are a command now, five never will be`

---

## Self-Review

**Coverage.** Spec §1.1 the tool's home → Task 1. §1.2 the driver → Task 3. §1.3 the catcher → Task 4. §1.4 per-file state → Task 1. §1.5 read-back verification → Tasks 1 and 5. §1.6 the crash marker → Task 1. §1.7 local-only → Task 7's honest ledger line. §3's five manual rows → Task 7 records why. §5's whole test plan → Task 6, plus unit tests in Tasks 1–5.

**Three judgement calls an implementer should not silently reverse.** The `ConsentStore` reference is try-App-first and move-only-if-forced, not a speculative refactor. Negative assertions are keyed to the cooldown rather than a guessed wait. And the runner aborts rather than continues when the Discord swap does not verify, because that is the one failure with an audience.

**What this deliberately does not do.** No CI. No data-root seam, which is the better long-term answer and is recorded in spec §2 with its cost. No mocked phone.

**Type consistency.** `ProfileGuard.AcquireAsync(dataRoot, backupRoot)` and `RestoreAsync()` are the signatures in Tasks 1 and 5. `LogTail.OpenAt(logPath)` and its three recognisers are the shapes in Tasks 2 and 5. `MetricReporter(pipeName, pluginId)` with `IsHostReachableAsync`/`ReportAsync` is the shape in Tasks 3 and 5. `WebhookCatcher.Start()` with `MineUrl`/`ClanUrl`/`DrainAsync` is the shape in Tasks 4 and 5.
