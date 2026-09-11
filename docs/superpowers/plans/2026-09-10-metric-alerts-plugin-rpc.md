# External Metric Alerts, Plan 2: the plugin RPC Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A consented plugin can report a number to the host over gRPC, and that number becomes a routed alert.

**Architecture:** One additive RPC, `ReportMetric`, gated by a new capability. An App-side sink adapter owns the `MetricAlertCoordinator` plan 1 built: it enforces the opt-in setting, rejects future-dated observations, resolves the reporting subject to a RoRoRo account's masked and real names, and raises the resulting triggers on an event that `App` wires to `AlertDispatcher` exactly as `MainViewModel.AlertsRaised` already is. Rules reach the coordinator through an `IMetricRuleSource` seam whose plan-2 implementation reads a local JSON file and whose plan-3 implementation reads the signed manifest, so plan 3 swaps one registration and changes nothing else.

**Tech Stack:** .NET 10, C# 14, WPF, Grpc.AspNetCore over a named pipe, Google.Protobuf, xUnit, `Microsoft.Extensions.TimeProvider.Testing`.

**Spec:** [docs/superpowers/specs/2026-09-09-external-metric-alerts-design.md](../specs/2026-09-09-external-metric-alerts-design.md) — read §1.2, §1.6, §3 and the whole of §6 before starting. §6 is not background; it is three requirements this plan exists to satisfy.

**Predecessor:** Plan 1 merged to `main` as PR #207 at `9f39630`. It built `MetricObservation`, `MetricHistory`, `MetricRule`/`MetricEvaluator`, `MetricAlertCoordinator` and `AlertKind.MetricBreach`. Read those five files before Task 4.

**Smoke list:** [../smoke-metric-alerts.md](../smoke-metric-alerts.md). Task 8 updates it. Nothing in this plan is verified by the suite alone.

## Global Constraints

- **The shipped binary names no vendor, no endpoint and no field path.** The test to apply to any change: does the binary or the 626-hosted manifest name Big Games? If yes, the separation is a fig leaf (spec §1.6).
- **The macro wall.** The Store binary never synthesizes input or injects into a client. This feature receives numbers a plugin already gathered; it acts on nothing.
- **Core returns keys and data, never prose.** `CoreStringBoundaryFenceTests` bans a `string Message` member in Core. `WebhookPayload` is the one sanctioned English-payload exception, because a single payload feeds the desktop toast, both webhooks and the phone.
- **Absence is denial.** A gRPC method missing from `RpcMethodCapabilityMap` is denied, and at runtime the whole plugin host silently fails to bind for the session (logged at Debug only). The tests are the gate that goes red, not startup.
- **Off the UI thread read `MainViewModel.AccountsSnapshot`, never `Accounts`.** Every gRPC handler runs off the UI thread.
- **Never commit** `dev-cert.pfx`/`.cer`, `accounts.dat`, `consent.dat`, `discord.dat`, `notify.dat`, `webview2-data/`, `/plugins/`, `spike/`, or any `.ROBLOSECURITY` value. A pre-commit hook and the CI `guards` job reject cookie prefixes, key files, and any absolute user-profile path in a committed file. Use `%LOCALAPPDATA%` and friends, or a relative path.
- **Always name the solution explicitly: `ROROROblox.slnx`.** A gitignored legacy `.sln` sits beside it and a bare `dotnet build` errors MSB1011.
- **A running dev-build RoRoRo locks `bin\Debug`.** Build Release.
- Baseline at the start of this plan: `dotnet test ROROROblox.slnx -c Release` gives **2092 unit + 24 harness pass, 1 harness skip by design**.

## Two facts that contradict the CLAUDE.md checklist — verified 2026-09-10, trust these

1. CLAUDE.md says a new provider interface goes on `PluginHostService` as an optional ctor parameter "so the two test construction sites keep compiling." It is **30 construction sites** across the two test projects, which is why the pattern exists at all. The price of the optional parameter is a dedicated wiring test proving production actually supplies it — see `ThemeFeedWiringTests` and `SavedAccountsWiringTests`. Task 6 pays that price.
2. CLAUDE.md's older comments say a missing capability-map entry "crashes" startup. It does not and never has in production: `App.StartPluginHostListener` runs the startup service fire-and-forget and logs the faulted task at Debug, so a missing entry means **plugins are silently off for the session**. `RpcMethodCapabilityMap`'s own class summary already carries this correction.

## File Structure

| File | Responsibility |
| --- | --- |
| `src/ROROROblox.PluginContract/Protos/plugin_contract.proto` | The wire contract. One new rpc, one new message. Additive: contract_version stays `"1.0"`. |
| `src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj` | NuGet `<Version>` 0.9.0 → 0.10.0. |
| `src/ROROROblox.App/Plugins/PluginCapability.cs` | The capability constant and its resx key. |
| `src/ROROROblox.App/Plugins/RpcMethodCapabilityMap.cs` | The gate entry. Absence is denial. |
| `src/ROROROblox.Core/Metrics/MetricHistory.cs` | Gains a series-count bound (spec §6.3). |
| `src/ROROROblox.Core/Metrics/IMetricRuleSource.cs` | The seam plan 3 replaces. |
| `src/ROROROblox.App/Metrics/LocalFileMetricRuleSource.cs` | Plan 2's interim rule source. One file, no UI. |
| `src/ROROROblox.App/Plugins/IMetricReportSink.cs` | The host-side sink the RPC hands to. Mirrors `IAccountActivityMarker`'s adapter role. |
| `src/ROROROblox.App/Plugins/Adapters/MetricReportSinkAdapter.cs` | The gate, the clock check, name resolution, the coordinator, the event. |
| `src/ROROROblox.App/Plugins/PluginHostService.cs` | The `ReportMetric` override plus one optional ctor parameter. |
| `src/ROROROblox.App/App.xaml.cs` | DI registration and the event wire to `AlertDispatcher`. |
| `docs/plugins/AUTHOR_GUIDE.md` | The recipe a plugin author actually reads. |

---

### Task 1: The wire contract and the capability

**Files:**
- Modify: `src/ROROROblox.PluginContract/Protos/plugin_contract.proto`
- Modify: `src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj` (the `<Version>` line)
- Modify: `src/ROROROblox.App/Plugins/PluginCapability.cs`
- Modify: `src/ROROROblox.App/Plugins/RpcMethodCapabilityMap.cs`
- Modify: `src/ROROROblox.App/Properties/Strings.resx`
- Modify: `docs/store/translations/ui-fr.json`, `ui-de.json`, `ui-ru.json`, `ui-pt-BR.json`, `ui-pl.json`, `ui-es.json`
- Test: `src/ROROROblox.Tests/Metrics/MetricCapabilityTests.cs`

**Interfaces:**
- Produces: `PluginCapability.HostMetricsReport` (the string `"host.metrics.report"`); the proto messages `MetricReport` and the rpc `ReportMetric(MetricReport) returns (Empty)`; the generated C# types `ROROROblox.PluginContract.MetricReport`.

**Why a capability at all.** Every other `host.*` capability fences something that can cause harm. This one fences a *write into the alert system*: a plugin holding it can make the user's phone buzz. That is exactly the kind of thing consent exists for, and it is why this is gated while `GetTheme` is not.

- [ ] **Step 1: Write the failing test**

Create `src/ROROROblox.Tests/Metrics/MetricCapabilityTests.cs`:

```csharp
using ROROROblox.App.Plugins;

namespace ROROROblox.Tests.Metrics;

public class MetricCapabilityTests
{
    [Fact]
    public void TheCapability_IsKnownAndHostEnforced()
    {
        // host.* means the interceptor enforces it on every call. A system.* capability is
        // disclosed for consent but unenforceable, because the plugin is its own process.
        // Reporting a metric is a write into the alert system, so it must be the enforced kind.
        Assert.True(PluginCapability.IsKnown(PluginCapability.HostMetricsReport));
        Assert.True(PluginCapability.IsHostEnforced(PluginCapability.HostMetricsReport));
    }

    [Fact]
    public void TheRpc_IsInTheCapabilityMap_AndIsGated()
    {
        Assert.True(RpcMethodCapabilityMap.TryGetRequired("ReportMetric", out var required));
        Assert.Equal(PluginCapability.HostMetricsReport, required);
    }

    [Fact]
    public void TheCapability_HasATranslatedDisplayString()
    {
        // Display() resolves a resx KEY at call time. A missing key falls through to the
        // "unknown capability" format, which would put a raw dotted string in the consent
        // sheet -- the one screen where a user decides whether to trust a plugin.
        var shown = PluginCapability.Display(PluginCapability.HostMetricsReport);
        Assert.DoesNotContain("host.metrics.report", shown, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryHostMethodStillHasAnEntry()
    {
        // Duplicates RpcMethodCapabilityMapTests on purpose: this is the test that fails the
        // moment the proto gains a method and the map does not, and a reader of THIS file
        // should see that guard rather than have to know the other file exists.
        RpcMethodCapabilityMap.AssertExhaustive();
    }
}
```

- [ ] **Step 2: Run and watch fail**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~MetricCapabilityTests"`
Expected: FAIL to compile — `PluginCapability.HostMetricsReport` does not exist.

- [ ] **Step 3: Add the rpc and its message to the proto**

In `src/ROROROblox.PluginContract/Protos/plugin_contract.proto`, add to the `service RoRoRoHost` block, after the `StopAccounts` rpc:

```protobuf
  // Metric reporting (additive, NuGet 0.10.0). A consented plugin reports ONE number it
  // gathered itself; the host keeps the history, applies the rules, and decides whether that
  // is worth an alert.
  //
  // The split is deliberate and is the whole design. Thresholding stays in the host so a
  // metric alert inherits per-account mute, the per-(account, kind) cooldown, and coalescing
  // from the same router every other alert kind rides. A plugin that decided for itself when
  // to page would bypass all three, and "a bad night becomes forty notifications" is the one
  // outcome this feature must not produce.
  //
  // Fire-and-forget: the host returns Empty whether or not the report produced an alert.
  // A plugin cannot learn whether it made the user's phone buzz, and does not need to.
  rpc ReportMetric(MetricReport) returns (Empty);
```

Then add the message, beside `MarkAccountActiveRequest`:

```protobuf
// One number, for one subject, at one instant.
message MetricReport {
  // A RoRoRo account id (the stringified Guid from GetAccounts), or empty for a metric that
  // is not about any one account. Mapping an EXTERNAL identity -- a game's user id, a clan
  // member -- onto a RoRoRo account is the plugin's job, because the plugin is the only side
  // that knows the external system. GetAccounts carries roblox_user_id for exactly this.
  //
  // An id the host does not recognise is treated as the empty case rather than rejected: the
  // alert still reaches the user, keyed globally, instead of vanishing because a plugin
  // guessed an id wrong.
  string subject_id = 1;

  // Opaque to the host. The host stores history per (subject, metric_id) and matches rules by
  // exact ordinal comparison. It carries no meaning here -- naming it "battle.points" or
  // "widgets" is entirely the plugin's and the user's business, and the host ships no list of
  // known ids because shipping one would name a game.
  string metric_id = 2;

  // Whatever the source reported, raw, and frequently CUMULATIVE (points so far) rather than
  // a rate. Deriving a rate from two of these is the host's job, not the plugin's.
  double value = 3;

  // When the plugin observed it, UTC, unix milliseconds. NOT when it sent it.
  //
  // The host compares this against its own clock and drops a report stamped in the future,
  // with a log line. That asymmetry is worth knowing: a rate is computed over a time window
  // and so a skewed clock silently disables rate rules ONLY, while level and change rules
  // keep working -- the feature half-works and nobody can tell. Send real UTC.
  int64 observed_at_unix_ms = 4;
}
```

- [ ] **Step 4: Bump the contract package version**

In `src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj`, change `<Version>0.9.0</Version>` to `<Version>0.10.0</Version>`. Additive only — the wire `contract_version` stays `"1.0"`, so a plugin that never calls this still handshakes.

- [ ] **Step 5: Add the capability constant and its resx key**

In `src/ROROROblox.App/Plugins/PluginCapability.cs`, add to the `host.*` constants, after `HostQueriesAccounts`:

```csharp
    public const string HostMetricsReport = "host.metrics.report";
```

and to the `ResxKeys` dictionary, in the matching position:

```csharp
        [HostMetricsReport] = "Plugin_Cap_MetricsReport",
```

- [ ] **Step 6: Add the capability-map entry**

In `src/ROROROblox.App/Plugins/RpcMethodCapabilityMap.cs`, add after the `StopAccounts` entry:

```csharp
        // Metric reporting (0.10.0). Gated, unlike the ungated reads above, because this one
        // WRITES: a plugin holding it can cause the user's phone to ring. That is the whole
        // reason the consent sheet exists.
        ["ReportMetric"] = PluginCapability.HostMetricsReport,
```

- [ ] **Step 7: Add the English string, then the six translations**

Add to `src/ROROROblox.App/Properties/Strings.resx` a `Plugin_Cap_MetricsReport` entry with the value:

```
Report numbers it gathers, so RoRoRo can alert you about them
```

Voice check before you write it: this line appears in the consent sheet, where a user decides whether to trust a plugin. It says what the plugin gains, in the second person, plainly. It does not say "metrics" or "telemetry" — those are our words, not the reader's.

Add the same key to all six `docs/store/translations/ui-*.json`:

| lang | value |
| --- | --- |
| fr | `Transmettre les chiffres qu'il collecte, pour que RoRoRo vous alerte à leur sujet` |
| de | `Gesammelte Zahlen melden, damit RoRoRo dich darüber benachrichtigen kann` |
| ru | `Передавать собранные числа, чтобы RoRoRo мог вас о них уведомлять` |
| pt-BR | `Enviar os números que coleta, para que o RoRoRo possa avisar você sobre eles` |
| pl | `Przesyłać zebrane liczby, aby RoRoRo mógł Cię o nich powiadamiać` |
| es | `Enviar los números que recopila, para que RoRoRo pueda avisarte sobre ellos` |

- [ ] **Step 8: Regenerate and lint the catalogs**

```bash
for c in fr de ru pt-BR pl es; do python scripts/gen-culture-resx.py $c; done
python scripts/lint-translations.py
```
Expected: all six clean, key count risen by exactly one, zero parity candidates. `gen-culture-resx.py` refuses an incomplete catalog, so a missing language fails here rather than shipping English into a Polish consent sheet.

- [ ] **Step 9: Run the tests**

```bash
dotnet build ROROROblox.slnx -c Release && dotnet test ROROROblox.slnx -c Release --no-build
```
Expected: PASS. The four new tests, plus `RpcMethodCapabilityMapTests` and the harness's `CapabilityMap_CoversEveryHostMethod`, all green. Baseline 2092 + 4 = 2096 unit, 24 harness.

- [ ] **Step 10: Commit**

```bash
git add src/ROROROblox.PluginContract/ src/ROROROblox.App/Plugins/PluginCapability.cs \
        src/ROROROblox.App/Plugins/RpcMethodCapabilityMap.cs \
        src/ROROROblox.App/Properties/ docs/store/translations/ \
        src/ROROROblox.Tests/Metrics/MetricCapabilityTests.cs
git commit -m "feat(plugins): ReportMetric on the wire, gated by host.metrics.report"
```

---

### Task 2: Bound the series dictionary by key count

**Files:**
- Modify: `src/ROROROblox.Core/Metrics/MetricHistory.cs`
- Test: `src/ROROROblox.Tests/Metrics/MetricHistoryTests.cs` (add to the existing class)

**Interfaces:**
- Consumes: `MetricHistory` as plan 1 left it.
- Produces: `MetricHistory(int capacity = 64, int maxSeries = 256)`. The first parameter keeps its current meaning and position, so every existing construction site is unaffected.

**Why now, before the RPC exists.** Plan 1's history caps samples per series but never caps the number of series. Until now the only caller was a test. From Task 5 onward, `metric_id` arrives from a plugin, and a reporter whose id varies — a server id, a session suffix, a battle name — grows the dictionary for the process lifetime of a tray app that runs for days. Landing the bound first means the RPC is never briefly unbounded.

**The rule to implement, stated before the code so it is not improvised:** when a new series would exceed `maxSeries`, the observation is **dropped** and the dictionary is left alone. Not evicted-oldest. Eviction would silently discard a series a rule is actively watching in order to make room for junk ids, which is strictly worse than refusing the junk. A caller has no way to distinguish a dropped add from a kept one, and does not need to: the alternative is unbounded growth.

- [ ] **Step 1: Write the failing tests**

Add to `src/ROROROblox.Tests/Metrics/MetricHistoryTests.cs`:

```csharp
    [Fact]
    public void SeriesCount_IsBounded_AndTheBoundDropsRatherThanEvicts()
    {
        // A reporter whose metric id varies -- a server id, a session suffix -- would otherwise
        // grow this dictionary for the lifetime of a tray app that runs for days.
        var h = new MetricHistory(capacity: 8, maxSeries: 3);
        var acct = Guid.NewGuid();

        for (var i = 0; i < 3; i++)
        {
            h.Add(new MetricObservation(acct, $"m{i}", 1, DateTimeOffset.UnixEpoch));
        }
        h.Add(new MetricObservation(acct, "overflow", 1, DateTimeOffset.UnixEpoch));

        // The three that got in are still there -- the bound refuses the newcomer rather than
        // evicting a series a rule may be actively watching.
        Assert.Equal(1, h.Count(acct, "m0"));
        Assert.Equal(1, h.Count(acct, "m2"));
        Assert.Equal(0, h.Count(acct, "overflow"));
    }

    [Fact]
    public void AnExistingSeries_KeepsAcceptingAfterTheBoundIsReached()
    {
        // The bound is on the number of series, not on writes. Refusing further samples to a
        // series a rule is watching would turn a junk-id flood into a silent outage of the
        // metric the user actually configured.
        var h = new MetricHistory(capacity: 8, maxSeries: 2);
        var acct = Guid.NewGuid();

        h.Add(new MetricObservation(acct, "real", 1, DateTimeOffset.UnixEpoch));
        h.Add(new MetricObservation(acct, "other", 1, DateTimeOffset.UnixEpoch));
        h.Add(new MetricObservation(acct, "junk", 1, DateTimeOffset.UnixEpoch));
        h.Add(new MetricObservation(acct, "real", 2, DateTimeOffset.UnixEpoch.AddMinutes(1)));

        Assert.Equal(2, h.Count(acct, "real"));
    }

    [Fact]
    public void TheSeriesBound_IsPerAccountPlusMetric_NotPerMetric()
    {
        // The key is the PAIR. Two accounts reporting the same metric id are two series, which
        // is what makes a per-account bound meaningful at all.
        var h = new MetricHistory(capacity: 8, maxSeries: 2);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        h.Add(new MetricObservation(a, "same", 1, DateTimeOffset.UnixEpoch));
        h.Add(new MetricObservation(b, "same", 1, DateTimeOffset.UnixEpoch));
        h.Add(new MetricObservation(Guid.NewGuid(), "same", 1, DateTimeOffset.UnixEpoch));

        Assert.Equal(1, h.Count(a, "same"));
        Assert.Equal(1, h.Count(b, "same"));
    }
```

- [ ] **Step 2: Run and watch fail**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~MetricHistoryTests"`
Expected: FAIL to compile — `MetricHistory` has no `maxSeries` parameter.

- [ ] **Step 3: Implement the bound**

Read `src/ROROROblox.Core/Metrics/MetricHistory.cs` first and match its existing shape. Four facts about that file, verified, so you do not have to rediscover them:

- It uses a **primary constructor** taking `capacity`, so `maxSeries` goes beside it as a second primary-constructor parameter.
- `_capacity` **throws** `ArgumentOutOfRangeException` on a value below two. It does not clamp. Decide deliberately which `maxSeries` should do — the tests above pass a small explicit value, so either works — and match whichever you choose to the reasoning in your doc comment.
- `_series` is a **`ConcurrentDictionary`**, not a plain dictionary. That shapes the bound: check `TryGetValue` first, and only consult `_series.Count` on the miss path before adding.
- Every reader takes `lock (series.Samples)`.

The count check races benignly: two threads adding two different new series at the bound can both pass it, so the dictionary may briefly hold one or two more than `maxSeries`. That is fine and worth a comment saying so — taking a lock across the whole dictionary to make a junk-id bound exact would put a global lock on the hot path to save two entries.

Add the parameter, then, in `Add`, refuse a NEW series when the dictionary is already at the bound:

```csharp
    /// <summary>
    /// The most distinct (account, metric) pairs this history will hold. Metric ids arrive from
    /// a plugin, so a reporter whose id varies would otherwise grow this dictionary without limit
    /// for the lifetime of a process that runs for days.
    /// <para>
    /// At the bound a NEW series is refused and the existing ones are untouched — never
    /// evict-oldest. Eviction would discard a series a rule is actively watching in order to make
    /// room for junk, which is a silent outage of the metric the user configured. Refusing the
    /// newcomer costs only the junk.
    /// </para>
    /// </summary>
    private readonly int _maxSeries = maxSeries;
```

The refusal itself goes in `Add`, guarding only the create path — an existing series keeps accepting samples, because refusing further samples to a series a rule is watching would turn a junk-id flood into a silent outage of the metric the user actually configured.

- [ ] **Step 4: Run the tests**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~MetricHistoryTests"`
Expected: PASS, the existing cases plus the three new ones.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/Metrics/MetricHistory.cs src/ROROROblox.Tests/Metrics/MetricHistoryTests.cs
git commit -m "fix(metrics): bound the series dictionary, refusing newcomers over evicting watched series"
```

---

### Task 3: The rule source seam and its interim implementation

**Files:**
- Create: `src/ROROROblox.Core/Metrics/IMetricRuleSource.cs`
- Create: `src/ROROROblox.App/Metrics/LocalFileMetricRuleSource.cs`
- Test: `src/ROROROblox.Tests/Metrics/LocalFileMetricRuleSourceTests.cs`

**Interfaces:**
- Consumes: `MetricRule(string MetricId, MetricRuleKind Kind, double Threshold, TimeSpan Window, bool AlertWhenBelow = true)` and `MetricRuleKind { Rate, Level, Event }` from plan 1.
- Produces: `interface IMetricRuleSource { IReadOnlyList<MetricRule> CurrentRules(); }`; `sealed class LocalFileMetricRuleSource(string filePath, ILogger<LocalFileMetricRuleSource> log) : IMetricRuleSource`.

**Why a seam and not just a file reader.** Plan 3 replaces the source of rules with a signed manifest. If Task 4's adapter reads a file directly, plan 3 is a rewrite of the adapter plus its tests. Behind this interface, plan 3 registers a different implementation and changes nothing else. The interface is deliberately one method — no change event — because Task 4 re-reads the rules on every report, which is cheap and removes a whole class of stale-rules bug.

**Why the interim source is an unsigned local file, when the spec insists the manifest is signed.** Spec §1.5 requires a signature because the manifest names **URLs the plugin will call**, which makes an unsigned one an attacker choosing this app's request targets. A rule carries no URL: it is a metric id, a kind, a threshold and a window. There is no exfiltration primitive in a threshold, so signing it would be ceremony. Plan 3's manifest, which does carry URLs, is signed for exactly the reason this file is not.

- [ ] **Step 1: Write the failing tests**

Create `src/ROROROblox.Tests/Metrics/LocalFileMetricRuleSourceTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Metrics;
using ROROROblox.Core.Metrics;

namespace ROROROblox.Tests.Metrics;

public class LocalFileMetricRuleSourceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rororo-rules-{Guid.NewGuid():N}.json");

    private LocalFileMetricRuleSource Sut() =>
        new(_path, NullLogger<LocalFileMetricRuleSource>.Instance);

    private void Write(string json) => File.WriteAllText(_path, json);

    [Fact]
    public void NoFile_MeansNoRules_NotAnError()
    {
        // The shipped default. Absent must be inert: this feature is opt-in, and a user who
        // never configured a rule must never see an exception, a warning, or an alert.
        Assert.Empty(Sut().CurrentRules());
    }

    [Fact]
    public void AWellFormedFile_ParsesEveryKind()
    {
        Write("""
        [
          { "metricId": "a", "kind": "Rate",  "threshold": 100, "windowMinutes": 10 },
          { "metricId": "b", "kind": "Level", "threshold": 500, "alertWhenBelow": false },
          { "metricId": "c", "kind": "Event" }
        ]
        """);

        var rules = Sut().CurrentRules();

        Assert.Equal(3, rules.Count);
        Assert.Equal(MetricRuleKind.Rate, rules[0].Kind);
        Assert.Equal(TimeSpan.FromMinutes(10), rules[0].Window);
        Assert.Equal(100, rules[0].Threshold);
        Assert.False(rules[1].AlertWhenBelow);
        Assert.Equal(MetricRuleKind.Event, rules[2].Kind);
    }

    [Fact]
    public void MalformedJson_YieldsNoRules_AndDoesNotThrow()
    {
        // A truncated file must not take the app down. This is read on the gRPC path, and
        // nothing on that path may throw into the plugin host.
        Write("[ { \"metricId\": \"a\", ");
        Assert.Empty(Sut().CurrentRules());
    }

    [Fact]
    public void AnUnknownKind_DropsThatRuleAndKeepsTheRest()
    {
        // One bad row must not cost the user the rules that were fine. Silently dropping the
        // whole file on one typo is how a person concludes the feature is broken.
        Write("""
        [
          { "metricId": "good", "kind": "Rate", "threshold": 1, "windowMinutes": 5 },
          { "metricId": "bad",  "kind": "Telepathy", "threshold": 1 }
        ]
        """);

        var rule = Assert.Single(Sut().CurrentRules());
        Assert.Equal("good", rule.MetricId);
    }

    [Fact]
    public void ARuleWithNoMetricId_IsDropped()
    {
        // A rule with no metric id matches nothing, so it is not a rule. Keeping it would put a
        // row in the file that looks configured and can never fire.
        Write("""[ { "kind": "Rate", "threshold": 1, "windowMinutes": 5 } ]""");
        Assert.Empty(Sut().CurrentRules());
    }

    [Fact]
    public void AnEditedFile_IsPickedUpWithoutRestart()
    {
        // CurrentRules() re-reads every call on purpose. It is a small file read on a path that
        // already crosses a named pipe, and it removes the entire stale-rules failure mode.
        var sut = Sut();
        Write("""[ { "metricId": "a", "kind": "Event" } ]""");
        Assert.Single(sut.CurrentRules());

        Write("""[ { "metricId": "a", "kind": "Event" }, { "metricId": "b", "kind": "Event" } ]""");
        Assert.Equal(2, sut.CurrentRules().Count);
    }

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch (Exception) { /* temp file */ }
    }
}
```

- [ ] **Step 2: Run and watch fail**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~LocalFileMetricRuleSourceTests"`
Expected: FAIL to compile — `LocalFileMetricRuleSource` does not exist.

- [ ] **Step 3: Write the interface**

Create `src/ROROROblox.Core/Metrics/IMetricRuleSource.cs`:

```csharp
namespace ROROROblox.Core.Metrics;

/// <summary>
/// Where the rules come from. One method, deliberately: callers re-read on every observation
/// rather than subscribing to a change event, which is cheap for a handful of rules and removes
/// the stale-rules failure mode entirely.
///
/// <para>
/// This interface exists so the SOURCE can change without the consumer changing. Plan 2 reads a
/// local JSON file the user writes; plan 3 reads a signed manifest. Swapping them is one
/// registration.
/// </para>
/// </summary>
public interface IMetricRuleSource
{
    /// <summary>The rules as they stand right now. Empty is the shipped default and is never
    /// an error — this feature is opt-in and a user with no rules must see nothing happen.</summary>
    IReadOnlyList<MetricRule> CurrentRules();
}
```

- [ ] **Step 4: Write the implementation**

Create `src/ROROROblox.App/Metrics/LocalFileMetricRuleSource.cs`:

```csharp
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ROROROblox.Core.Metrics;

namespace ROROROblox.App.Metrics;

/// <summary>
/// Reads metric rules from a JSON array the user writes by hand. The INTERIM source: plan 3
/// replaces it with a signed manifest, and this class exists so plan 2 has something end-to-end
/// testable without the manifest's signing rig.
///
/// <para>
/// <b>Unsigned on purpose, and that is not an oversight.</b> The manifest plan 3 ships is signed
/// because it names URLs the plugin will call, which makes an unsigned one an attacker choosing
/// this app's request targets. A rule names no URL — a metric id, a kind, a threshold, a window —
/// so there is no exfiltration primitive here to protect. Signing it would be ceremony.
/// </para>
/// <para>
/// Nothing here may throw. It is read on the gRPC report path, and a malformed file must cost the
/// user their rules, never their plugin host.
/// </para>
/// </summary>
public sealed class LocalFileMetricRuleSource(string filePath, ILogger<LocalFileMetricRuleSource> log)
    : IMetricRuleSource
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public IReadOnlyList<MetricRule> CurrentRules()
    {
        List<RuleRow>? rows;
        try
        {
            if (!File.Exists(filePath)) return [];
            rows = JsonSerializer.Deserialize<List<RuleRow>>(File.ReadAllText(filePath), Options);
        }
        catch (Exception ex)
        {
            // Named at Information, not Warning: a user who has never written this file is the
            // common case and is not in trouble. A user who wrote a broken one needs to be able
            // to find out why, and "no alerts ever" with a silent log is indistinguishable from
            // "the feature does not work".
            log.LogInformation(ex, "Could not read metric rules from {Path}; no rules are active.", filePath);
            return [];
        }

        if (rows is null) return [];

        var rules = new List<MetricRule>(rows.Count);
        foreach (var row in rows)
        {
            if (row is null) continue;

            // A rule with no metric id matches nothing, so it is not a rule. Keeping it would put
            // a row in the file that looks configured and can never fire.
            if (string.IsNullOrWhiteSpace(row.MetricId)) continue;

            if (!Enum.TryParse<MetricRuleKind>(row.Kind, ignoreCase: true, out var kind))
            {
                // One typo must not cost the rules that were fine.
                log.LogInformation("Metric rule for {MetricId} names an unknown kind {Kind}; skipped.",
                    row.MetricId, row.Kind);
                continue;
            }

            rules.Add(new MetricRule(
                row.MetricId,
                kind,
                row.Threshold,
                TimeSpan.FromMinutes(row.WindowMinutes),
                row.AlertWhenBelow));
        }

        return rules;
    }

    /// <summary>The on-disk shape. Separate from <see cref="MetricRule"/> so the file format and
    /// the domain type can drift apart without one dragging the other.</summary>
    private sealed class RuleRow
    {
        [JsonPropertyName("metricId")] public string? MetricId { get; set; }
        [JsonPropertyName("kind")] public string? Kind { get; set; }
        [JsonPropertyName("threshold")] public double Threshold { get; set; }
        [JsonPropertyName("windowMinutes")] public double WindowMinutes { get; set; }
        [JsonPropertyName("alertWhenBelow")] public bool AlertWhenBelow { get; set; } = true;
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~LocalFileMetricRuleSourceTests"`
Expected: PASS, 6 tests.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.Core/Metrics/IMetricRuleSource.cs \
        src/ROROROblox.App/Metrics/LocalFileMetricRuleSource.cs \
        src/ROROROblox.Tests/Metrics/LocalFileMetricRuleSourceTests.cs
git commit -m "feat(metrics): a rule-source seam, with a local file behind it until plan 3"
```

---

### Task 4: The sink adapter — the gate, the clock, the names

**Files:**
- Create: `src/ROROROblox.App/Plugins/IMetricReportSink.cs`
- Create: `src/ROROROblox.App/Plugins/Adapters/MetricReportSinkAdapter.cs`
- Test: `src/ROROROblox.Tests/Metrics/MetricReportSinkAdapterTests.cs`

**Interfaces:**
- Consumes: `IMetricRuleSource` (Task 3); `MetricAlertCoordinator(TimeProvider time, int historyCapacity = 64)` with `void SetRules(IReadOnlyList<MetricRule>)` and `IReadOnlyList<AlertTrigger> Observe(MetricObservation o, string displayName, string realName)` (plan 1).
- Produces: `interface IMetricReportSink { void Report(string subjectId, string metricId, double value, long observedAtUnixMs); }`; `sealed class MetricReportSinkAdapter : IMetricReportSink` with `event EventHandler<IReadOnlyList<AlertTrigger>>? AlertsRaised`.

**This is the task where spec §6's first two requirements land.** Read §6 before writing a line. The opt-in setting having no reader was plan 1's most important open defect; if this adapter does not consult it, the toggle stays decorative and this plan has failed its main job.

**Three decisions not to improvise:**

- **The gate is read per report, not cached at construction.** A user who turns the feature off must have it off on the next report, not on the next restart. `IAppSettings` is async and this path is not, so the adapter holds a `Func<bool>` the caller wires to a cached-and-refreshed read rather than blocking a gRPC thread on a file read.
- **A future-dated observation is DROPPED, with a log line, not clamped to now.** Clamping invents data: it would place a foreign clock's reading at the host's now and compute a rate across an interval that never happened. Dropping loses the sample and says so.
- **An unrecognised subject id becomes `Guid.Empty`, not a rejection.** That is `MetricObservation`'s documented global-carrier convention, the same one `AlertKind.UptimeMark` uses. A plugin that guesses an id wrong should still reach the user, keyed globally, rather than have the alert vanish.

- [ ] **Step 1: Write the failing tests**

Create `src/ROROROblox.Tests/Metrics/MetricReportSinkAdapterTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ROROROblox.App.Plugins.Adapters;
using ROROROblox.Core.Discord;
using ROROROblox.Core.Metrics;

namespace ROROROblox.Tests.Metrics;

public class MetricReportSinkAdapterTests
{
    private static readonly Guid Acct = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string M = "battle.points";

    private sealed class FixedRules(params MetricRule[] rules) : IMetricRuleSource
    {
        public IReadOnlyList<MetricRule> CurrentRules() => rules;
    }

    private static (MetricReportSinkAdapter Sut, FakeTimeProvider Clock, List<AlertTrigger> Raised) New(
        bool enabled = true, IMetricRuleSource? rules = null)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var sut = new MetricReportSinkAdapter(
            rules ?? new FixedRules(new MetricRule(M, MetricRuleKind.Rate, 100, TimeSpan.FromMinutes(10))),
            () => enabled,
            _ => ("Masked", "Real"),
            clock,
            NullLogger<MetricReportSinkAdapter>.Instance);

        var raised = new List<AlertTrigger>();
        sut.AlertsRaised += (_, t) => raised.AddRange(t);
        return (sut, clock, raised);
    }

    private static long Ms(DateTimeOffset at) => at.ToUnixTimeMilliseconds();

    [Fact]
    public void ABreach_RaisesATrigger_CarryingBothNames()
    {
        var (sut, clock, raised) = New();

        sut.Report(Acct.ToString(), M, 0, Ms(clock.GetUtcNow()));
        clock.Advance(TimeSpan.FromMinutes(10));
        sut.Report(Acct.ToString(), M, 500, Ms(clock.GetUtcNow()));   // 50/min, floor is 100

        var t = Assert.Single(raised);
        Assert.Equal(AlertKind.MetricBreach, t.Kind);
        Assert.Equal(Acct, t.AccountId);
        Assert.Equal("Masked", t.DisplayName);
        Assert.Equal("Real", t.RealName);
    }

    [Fact]
    public void WhenTheSettingIsOff_NothingIsEvenRecorded()
    {
        // Spec section 6, requirement 1. Through plan 1 this feature was off only because the
        // destination list happened to be empty. If the gate is not HERE, the toggle is decorative.
        var (sut, clock, raised) = New(enabled: false);

        sut.Report(Acct.ToString(), M, 0, Ms(clock.GetUtcNow()));
        clock.Advance(TimeSpan.FromMinutes(10));
        sut.Report(Acct.ToString(), M, 500, Ms(clock.GetUtcNow()));

        Assert.Empty(raised);
    }

    [Fact]
    public void TurningTheGateOn_TakesEffectOnTheNextReport()
    {
        // Read per report, not cached at construction: a user who flips the switch must not have
        // to restart the app to be believed.
        var enabled = false;
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var sut = new MetricReportSinkAdapter(
            new FixedRules(new MetricRule(M, MetricRuleKind.Level, 500, TimeSpan.Zero)),
            () => enabled,
            _ => ("Masked", "Real"),
            clock,
            NullLogger<MetricReportSinkAdapter>.Instance);

        var raised = new List<AlertTrigger>();
        sut.AlertsRaised += (_, t) => raised.AddRange(t);

        sut.Report(Acct.ToString(), M, 400, Ms(clock.GetUtcNow()));
        Assert.Empty(raised);

        enabled = true;
        sut.Report(Acct.ToString(), M, 400, Ms(clock.GetUtcNow()));
        Assert.Single(raised);
    }

    [Fact]
    public void AFutureDatedReport_IsDropped_NotClamped()
    {
        // Spec section 6, requirement 2. Clamping would invent data: it places a foreign clock's
        // reading at our now and computes a rate over an interval that never happened.
        var (sut, clock, raised) = New();

        sut.Report(Acct.ToString(), M, 0, Ms(clock.GetUtcNow()));
        clock.Advance(TimeSpan.FromMinutes(10));
        sut.Report(Acct.ToString(), M, 500, Ms(clock.GetUtcNow().AddHours(2)));

        // Had it been clamped to now, this would have been a 50/min breach.
        Assert.Empty(raised);
    }

    [Fact]
    public void ASlightlyFutureReport_IsAccepted()
    {
        // A hard "any future instant is a lie" test would reject every report from a machine
        // whose clock is a second fast, which is most machines. The tolerance is what keeps this
        // a skew detector rather than a clock-sync requirement.
        var (sut, clock, raised) = New();

        sut.Report(Acct.ToString(), M, 0, Ms(clock.GetUtcNow()));
        clock.Advance(TimeSpan.FromMinutes(10));
        sut.Report(Acct.ToString(), M, 500, Ms(clock.GetUtcNow().AddSeconds(2)));

        Assert.Single(raised);
    }

    [Fact]
    public void AnUnparseableSubject_BecomesTheGlobalCarrier()
    {
        // MetricObservation's documented convention, shared with AlertKind.UptimeMark: an
        // observation belonging to no account uses Guid.Empty. A plugin that guesses an id wrong
        // should still reach the user rather than have the alert vanish.
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var sut = new MetricReportSinkAdapter(
            new FixedRules(new MetricRule(M, MetricRuleKind.Level, 500, TimeSpan.Zero)),
            () => true,
            _ => ("Masked", "Real"),
            clock,
            NullLogger<MetricReportSinkAdapter>.Instance);

        var raised = new List<AlertTrigger>();
        sut.AlertsRaised += (_, t) => raised.AddRange(t);

        sut.Report("not-a-guid", M, 400, Ms(clock.GetUtcNow()));

        Assert.Equal(Guid.Empty, Assert.Single(raised).AccountId);
    }

    [Fact]
    public void NoRules_MeansNoWork_AndNoAlert()
    {
        // One SUT. Subscribing to one adapter and reporting to another would make Assert.Empty
        // pass no matter what the code did.
        var (sut, clock, raised) = New(rules: new FixedRules());

        sut.Report(Acct.ToString(), M, 0, Ms(clock.GetUtcNow()));
        clock.Advance(TimeSpan.FromMinutes(10));
        sut.Report(Acct.ToString(), M, 0, Ms(clock.GetUtcNow()));   // 0/min would breach any floor

        Assert.Empty(raised);
    }

    [Fact]
    public void ASubscriberThatThrows_DoesNotThrowIntoTheReporter()
    {
        // This runs on a gRPC handler thread. Nothing here may take down the plugin host, and an
        // alert is a passenger -- the same contract AlertDispatcher and MainViewModel.RaiseAlerts
        // already hold.
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var sut = new MetricReportSinkAdapter(
            new FixedRules(new MetricRule(M, MetricRuleKind.Level, 500, TimeSpan.Zero)),
            () => true,
            _ => ("Masked", "Real"),
            clock,
            NullLogger<MetricReportSinkAdapter>.Instance);

        sut.AlertsRaised += (_, _) => throw new InvalidOperationException("subscriber blew up");

        var ex = Record.Exception(() => sut.Report(Acct.ToString(), M, 400, Ms(clock.GetUtcNow())));
        Assert.Null(ex);
    }
}
```

- [ ] **Step 2: Run and watch fail**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~MetricReportSinkAdapterTests"`
Expected: FAIL to compile — `MetricReportSinkAdapter` does not exist.

- [ ] **Step 3: Write the sink interface**

Create `src/ROROROblox.App/Plugins/IMetricReportSink.cs`:

```csharp
namespace ROROROblox.App.Plugins;

/// <summary>
/// Host-side sink for a plugin's <c>ReportMetric</c> RPC. Mirrors
/// <see cref="IAccountActivityMarker"/>'s adapter role: it takes the plugin-facing, stringified
/// shape and does the host-side work, so the RPC handler stays a pass-through with no reasoning
/// of its own.
/// <para>
/// Deliberately returns nothing. The RPC is fire-and-forget: a plugin cannot learn whether its
/// report made the user's phone ring, and does not need to.
/// </para>
/// </summary>
public interface IMetricReportSink
{
    /// <summary>
    /// Record one reported number. Never throws — this is called from a gRPC handler, and a bad
    /// report must cost the report, never the plugin host.
    /// </summary>
    /// <param name="subjectId">A RoRoRo account id as a string, or anything else, which is
    /// treated as the global carrier rather than rejected.</param>
    /// <param name="observedAtUnixMs">When the PLUGIN observed it, UTC. A value meaningfully in
    /// the host's future is dropped and logged.</param>
    void Report(string subjectId, string metricId, double value, long observedAtUnixMs);
}
```

- [ ] **Step 4: Write the adapter**

Create `src/ROROROblox.App/Plugins/Adapters/MetricReportSinkAdapter.cs`:

```csharp
using Microsoft.Extensions.Logging;
using ROROROblox.Core.Discord;
using ROROROblox.Core.Metrics;

namespace ROROROblox.App.Plugins.Adapters;

/// <summary>
/// The seam between "a plugin reported a number" and "the alert system knows". Owns the
/// <see cref="MetricAlertCoordinator"/>, enforces the opt-in setting, refuses observations from a
/// clock that disagrees with ours, resolves the subject to the names an alert carries, and RAISES
/// the resulting triggers rather than sending them.
///
/// <para>
/// Raising is the whole design, and it is why this class does not know what a webhook is.
/// <c>App</c> wires <see cref="AlertsRaised"/> to <c>AlertDispatcher.DispatchAsync</c>, exactly as
/// it already wires <c>MainViewModel.AlertsRaised</c>. Everything downstream — per-account mute,
/// the per-(account, kind) cooldown, coalescing, fallback-to-Local — therefore applies to a metric
/// breach for free, and cannot be bypassed from here.
/// </para>
/// </summary>
/// <param name="rules">Plan 2 reads a local file; plan 3 reads a signed manifest. Re-read on every
/// report, so an edited rule takes effect without a restart.</param>
/// <param name="isEnabled">The opt-in gate, read PER REPORT. A user who turns the feature off must
/// have it off on the next report, not on the next launch. A func rather than
/// <c>IAppSettings</c> because this path is synchronous and must not block a gRPC thread on a
/// file read.</param>
/// <param name="resolveNames">Account id to (masked display name, real name). Masked comes first
/// because it is the one nearly every destination gets; only the clan channel is allowed the real
/// one. Off the UI thread this must read <c>MainViewModel.AccountsSnapshot</c>, never
/// <c>Accounts</c>.</param>
public sealed class MetricReportSinkAdapter(
    IMetricRuleSource rules,
    Func<bool> isEnabled,
    Func<Guid, (string Display, string Real)> resolveNames,
    TimeProvider time,
    ILogger<MetricReportSinkAdapter> log) : IMetricReportSink
{
    /// <summary>
    /// How far into the host's future a reported instant may sit before it is treated as a skewed
    /// clock rather than ordinary jitter. A hard "no future instants" rule would reject reports
    /// from any machine running a second fast, which is most of them; this keeps the check a skew
    /// detector rather than a clock-synchronisation requirement.
    /// </summary>
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromSeconds(30);

    private readonly MetricAlertCoordinator _coordinator = new(time);

    /// <summary>Fired when a report produced one or more breaches. <c>App</c> subscribes this to
    /// the alert dispatcher.</summary>
    public event EventHandler<IReadOnlyList<AlertTrigger>>? AlertsRaised;

    public void Report(string subjectId, string metricId, double value, long observedAtUnixMs)
    {
        try
        {
            if (!isEnabled()) return;
            if (string.IsNullOrWhiteSpace(metricId)) return;

            var observedAt = DateTimeOffset.FromUnixTimeMilliseconds(observedAtUnixMs);
            var now = time.GetUtcNow();

            if (observedAt - now > FutureTolerance)
            {
                // Named out loud because the failure is otherwise invisible and ASYMMETRIC: a
                // skewed clock puts every sample outside the rate window, so Rate rules go
                // permanently quiet while Level and Event keep working. "Half the feature stopped"
                // is indistinguishable from "the feature never worked" without this line.
                log.LogInformation(
                    "Dropped a metric report for {MetricId} stamped {Skew} in the future — check the reporter's clock; it is sending local time, not UTC.",
                    metricId, observedAt - now);
                return;
            }

            var active = rules.CurrentRules();
            if (active.Count == 0) return;

            // Guid.Empty is the documented global carrier, shared with AlertKind.UptimeMark: an
            // observation that belongs to no account must still reach the user rather than vanish
            // because a plugin guessed an id wrong.
            var accountId = Guid.TryParse(subjectId, out var parsed) ? parsed : Guid.Empty;
            var (display, real) = resolveNames(accountId);

            _coordinator.SetRules(active);
            var triggers = _coordinator.Observe(
                new MetricObservation(accountId, metricId, value, observedAt), display, real);

            if (triggers.Count == 0) return;
            AlertsRaised?.Invoke(this, triggers);
        }
        catch (Exception ex)
        {
            // Degrade-safe, like every other alert surface: an alert is a passenger. This runs on
            // a gRPC handler thread and must never take the plugin host down.
            log.LogWarning(ex, "A metric report was dropped.");
        }
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~MetricReportSinkAdapterTests"`
Expected: PASS, 8 tests.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.App/Plugins/IMetricReportSink.cs \
        src/ROROROblox.App/Plugins/Adapters/MetricReportSinkAdapter.cs \
        src/ROROROblox.Tests/Metrics/MetricReportSinkAdapterTests.cs
git commit -m "feat(metrics): the report sink — the opt-in gate finally has a reader"
```

---

### Task 5: The RPC handler

**Files:**
- Modify: `src/ROROROblox.App/Plugins/PluginHostService.cs` (one optional ctor parameter, one override)
- Test: `src/ROROROblox.Tests/Metrics/ReportMetricHandlerTests.cs`

**Interfaces:**
- Consumes: `IMetricReportSink` (Task 4); the generated `MetricReport` message (Task 1).
- Produces: `PluginHostService.ReportMetric(MetricReport, ServerCallContext) -> Task<Empty>`; the ctor gains a trailing `IMetricReportSink? metricSink = null`.

**The parameter is optional and trailing, and that is not laziness.** `PluginHostService` is constructed at **30 sites** across the two test projects. Twenty-nine do not care about metrics. Making it required edits all thirty for no behavioural gain. The price is Task 6's wiring test, which proves production supplies it — the same bargain `IThemePaletteSource` and `ISavedAccountsProvider` already struck, and both of their ctor doc comments explain it.

**Null means inert, not broken.** Unlike `GetTheme`, which fails `FailedPrecondition` on a null source because "no theme" and "unwired" are different claims, a null sink here simply returns `Empty`. A plugin cannot tell whether its report caused an alert in either case, so failing the call would tell it something untrue about its own correctness.

- [ ] **Step 1: Write the failing tests**

Create `src/ROROROblox.Tests/Metrics/ReportMetricHandlerTests.cs`. Build the service the way the other `PluginHostService` tests in this suite do — copy a neighbouring test's construction helper rather than inventing one, since the ctor has eleven required parameters:

```csharp
using Grpc.Core;
using ROROROblox.App.Plugins;
using ROROROblox.PluginContract;

namespace ROROROblox.Tests.Metrics;

public class ReportMetricHandlerTests
{
    private sealed class RecordingSink : IMetricReportSink
    {
        public List<(string Subject, string Metric, double Value, long At)> Reports { get; } = [];

        public void Report(string subjectId, string metricId, double value, long observedAtUnixMs)
            => Reports.Add((subjectId, metricId, value, observedAtUnixMs));
    }

    [Fact]
    public async Task TheHandler_IsAPassThrough_ToTheSink()
    {
        // Everything that could be a judgement call -- the gate, the clock check, the names --
        // lives in the sink. The handler reasons about nothing, so there is nothing here to get
        // wrong later.
        var sink = new RecordingSink();
        var sut = BuildService(sink);

        await sut.ReportMetric(new MetricReport
        {
            SubjectId = "11111111-1111-1111-1111-111111111111",
            MetricId = "battle.points",
            Value = 1234.5,
            ObservedAtUnixMs = 1_700_000_000_000,
        }, TestContext());

        var r = Assert.Single(sink.Reports);
        Assert.Equal("11111111-1111-1111-1111-111111111111", r.Subject);
        Assert.Equal("battle.points", r.Metric);
        Assert.Equal(1234.5, r.Value);
        Assert.Equal(1_700_000_000_000, r.At);
    }

    [Fact]
    public async Task WithNoSinkWired_ItReturnsEmpty_RatherThanFailing()
    {
        // Unlike GetTheme, which fails FailedPrecondition on a null source because "no theme" and
        // "unwired" are different claims, a plugin can never tell whether a report caused an
        // alert. Failing the call would tell it something untrue about its own correctness.
        var sut = BuildService(sink: null);

        var response = await sut.ReportMetric(new MetricReport { MetricId = "m" }, TestContext());

        Assert.NotNull(response);
    }
}
```

Add `BuildService(IMetricReportSink? sink)` and `TestContext()` helpers modelled on the existing `PluginHostService` tests in `src/ROROROblox.Tests/Plugins/`. Find one that already constructs the service and reuse its stubs rather than writing new ones.

- [ ] **Step 2: Run and watch fail**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~ReportMetricHandlerTests"`
Expected: FAIL to compile — `ReportMetric` is not a member of `PluginHostService`.

- [ ] **Step 3: Add the field, the ctor parameter, and the assignment**

In `src/ROROROblox.App/Plugins/PluginHostService.cs`, add the field beside `_savedAccounts`, carrying its own reasoning the way the two optional fields above it do:

```csharp
    /// <summary>
    /// Where a plugin's reported numbers go (contract 0.10.0). Optional for the same reason as
    /// <see cref="_themePalettes"/> and <see cref="_savedAccounts"/> — 30 construction sites, 29 of
    /// which do not care — and guarded the same way: <c>MetricReportWiringTests</c> asserts the
    /// production registration supplies it.
    /// <para>
    /// Unlike those two, null here is INERT rather than an error: ReportMetric returns Empty and
    /// records nothing. A plugin cannot tell whether a report produced an alert in any case, so
    /// failing the call would tell it something untrue about its own correctness.
    /// </para>
    /// </summary>
    private readonly IMetricReportSink? _metricSink;
```

Add `IMetricReportSink? metricSink = null` as the **last** parameter of the constructor, after `savedAccounts`, and assign it. Trailing keeps all 30 existing construction sites compiling untouched.

- [ ] **Step 4: Add the override**

```csharp
    // =====================================================================
    // ReportMetric (external metric alerts, plan 2 — contract 0.10.0).
    //
    // A pass-through, deliberately and completely. The opt-in gate, the
    // clock-skew check, subject-to-account resolution and the rules all
    // live in IMetricReportSink, so this handler reasons about nothing and
    // there is nothing here to get wrong later. Capability gate
    // (host.metrics.report) is enforced upstream by CapabilityInterceptor
    // via RpcMethodCapabilityMap — absence in that map is denial.
    //
    // Fire-and-forget: Empty comes back whether or not an alert resulted.
    // =====================================================================

    public override Task<Empty> ReportMetric(MetricReport request, ServerCallContext context)
    {
        _metricSink?.Report(request.SubjectId, request.MetricId, request.Value, request.ObservedAtUnixMs);
        return Task.FromResult(new Empty());
    }
```

- [ ] **Step 5: Run the tests**

```bash
dotnet build ROROROblox.slnx -c Release && dotnet test ROROROblox.slnx -c Release --no-build
```
Expected: PASS. The harness's `CapabilityMap_CoversEveryHostMethod` is the one that would have caught a missing map entry; it should already be green from Task 1.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.App/Plugins/PluginHostService.cs \
        src/ROROROblox.Tests/Metrics/ReportMetricHandlerTests.cs
git commit -m "feat(plugins): the ReportMetric handler, a pass-through to the sink"
```

---

### Task 6: Wire it up in production

**Files:**
- Modify: `src/ROROROblox.App/App.xaml.cs` (`ConfigureServices`, and the alert wiring beside `vm.AlertsRaised`)
- Test: `src/ROROROblox.Tests/MetricReportWiringTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 3, 4 and 5.
- Produces: DI registrations for `IMetricRuleSource` and `IMetricReportSink`; the `AlertsRaised` subscription that makes the feature reach a user.

**This is the task that makes the feature exist.** Everything before it is reachable only from tests. Read `App.xaml.cs:1093` for the `PluginHostService` factory and `App.xaml.cs:1682` for the existing `vm.AlertsRaised` wire before editing; both are the patterns to match.

**The rules file lives beside the app's other data**, at `%LOCALAPPDATA%\ROROROblox\metric-rules.json`. Find how the app already builds that folder path — `AppSettings.DefaultPath()` resolves it — and reuse it. Do not construct the path with a literal, and never write an absolute user path into a committed file; the pre-commit guard rejects it.

**Wiring the gate without blocking a gRPC thread.** `IAppSettings.GetMetricAlertsEnabledAsync` is async and the sink's gate is a synchronous `Func<bool>`. Read the setting once at startup into a `volatile bool`, and refresh it wherever the app already reacts to a settings change. If no such hook exists, refresh it on the periodic tick the view model already runs. Do **not** call `.Result` or `.GetAwaiter().GetResult()` on the settings read from the gate: a gRPC handler thread blocking on a file read under a semaphore is a deadlock waiting for a busy disk.

- [ ] **Step 1: Write the failing tests**

Create `src/ROROROblox.Tests/MetricReportWiringTests.cs`, modelled on `SavedAccountsWiringTests`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Plugins;
using ROROROblox.Core.Metrics;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// The price of an optional constructor parameter, paid a third time — same bargain as
/// <c>ThemeFeedWiringTests</c> and <c>SavedAccountsWiringTests</c>, whose docs carry the full
/// argument (30 construction sites; null is right for 29 and a silent defect in the one that
/// matters).
/// <para>
/// It matters more here than for either of those. A null theme source makes GetTheme fail loudly.
/// A null metric sink makes ReportMetric succeed and do nothing, so an unwired production host
/// would look exactly like a working one from every side — the plugin gets its Empty, the suite
/// stays green, and no alert ever fires. This test is the only thing standing between that and a
/// release.
/// </para>
/// </summary>
public class MetricReportWiringTests
{
    private static ServiceCollection RealRegistrations()
    {
        var services = new ServiceCollection();
        global::ROROROblox.App.App.ConfigureServices(services, NullLoggerFactory.Instance);
        return services;
    }

    [Fact]
    public void ProductionDi_RegistersAMetricReportSink()
    {
        var services = RealRegistrations();
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IMetricReportSink));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void ProductionDi_RegistersARuleSource()
    {
        var services = RealRegistrations();
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IMetricRuleSource));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void PluginHostService_TakesAMetricSink_KeptOptionalOnPurpose()
    {
        var ctor = typeof(PluginHostService).GetConstructors().Single();
        var sink = ctor.GetParameters().SingleOrDefault(p => p.ParameterType == typeof(IMetricReportSink));

        Assert.NotNull(sink);
        Assert.True(sink!.IsOptional, "kept optional on purpose — see the ctor's doc comment.");
    }

    [Fact]
    public void TheHostServiceFactory_ActuallyPassesTheSink()
    {
        // The registration existing and the factory USING it are different facts, and this is the
        // gap the other two tests cannot see: a sink registered in DI but never handed to the
        // factory leaves production silently inert.
        var services = RealRegistrations();
        var factory = Assert.Single(services, d => d.ServiceType == typeof(PluginHostService));
        Assert.NotNull(factory.ImplementationFactory);
    }
}
```

- [ ] **Step 2: Run and watch fail**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~MetricReportWiringTests"`
Expected: FAIL — no `IMetricReportSink` registration exists.

- [ ] **Step 3: Register the rule source and the sink**

In `ConfigureServices`, beside the other plugin registrations near line 1090:

```csharp
        // Metric alerts (plan 2). The rule source is a seam: plan 3 swaps this one registration
        // for the signed-manifest reader and nothing downstream changes. The file is absent on a
        // fresh install, which means no rules, which means the feature is inert — the correct
        // shipped default for an opt-in alert.
        services.AddSingleton<ROROROblox.Core.Metrics.IMetricRuleSource>(sp =>
            new ROROROblox.App.Metrics.LocalFileMetricRuleSource(
                System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(ROROROblox.Core.AppSettings.DefaultPath())!,
                    "metric-rules.json"),
                sp.GetRequiredService<ILogger<ROROROblox.App.Metrics.LocalFileMetricRuleSource>>()));
```

`AppSettings.DefaultPath()` is already `public static` (verified — `src/ROROROblox.Core/AppSettings.cs:37`), so this needs no new accessor. Take its directory rather than hardcoding the folder: the app's data location has moved once already, and a second literal would not move with it.

Then register the sink. Its three funcs are the interesting part:

```csharp
        services.AddSingleton<ROROROblox.App.Plugins.IMetricReportSink>(sp =>
            new ROROROblox.App.Plugins.Adapters.MetricReportSinkAdapter(
                sp.GetRequiredService<ROROROblox.Core.Metrics.IMetricRuleSource>(),
                () => MetricAlertsEnabled,
                accountId => ResolveAlertNames(sp, accountId),
                TimeProvider.System,
                sp.GetRequiredService<ILogger<ROROROblox.App.Plugins.Adapters.MetricReportSinkAdapter>>()));
```

`MetricAlertsEnabled` is a `volatile bool` field on `App`, seeded from `IAppSettings.GetMetricAlertsEnabledAsync()` during startup and refreshed when settings change. `ResolveAlertNames` reads `MainViewModel.AccountsSnapshot` — never `Accounts` — and returns `(summary.RenderName, summary.DisplayName)`, which is the masked-then-real order every other alert site uses; an account it cannot find returns a pair of empty strings rather than throwing.

- [ ] **Step 4: Pass the sink to the host service, and wire the event**

Add `sp.GetRequiredService<ROROROblox.App.Plugins.IMetricReportSink>()` as the final argument of the `PluginHostService` factory at line 1093.

Then, beside the existing `vm.AlertsRaised` subscription near line 1682:

```csharp
            // Metric breaches reach the dispatcher by exactly the path every other alert kind
            // takes, which is what makes mute, the per-(account, kind) cooldown and coalescing
            // apply to them without a line of new routing code.
            if (provider.GetService<ROROROblox.App.Plugins.IMetricReportSink>()
                is ROROROblox.App.Plugins.Adapters.MetricReportSinkAdapter metricSink)
            {
                metricSink.AlertsRaised += (_, triggers) => _ = dispatcher.DispatchAsync(triggers);
            }
```

Match whatever the surrounding code names its service provider and dispatcher variables; the names above are illustrative, the structure is not.

- [ ] **Step 5: Run everything**

```bash
dotnet build ROROROblox.slnx -c Release && dotnet test ROROROblox.slnx -c Release --no-build
```
Expected: PASS, including the four new wiring tests.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.App/App.xaml.cs src/ROROROblox.Tests/MetricReportWiringTests.cs
git commit -m "feat(metrics): wire the report path from the pipe to the dispatcher"
```

---

### Task 7: The harness end-to-end — a real plugin, a real pipe, a real denial

**Files:**
- Modify: `src/ROROROblox.PluginTestHarness/EndToEndContractTests.cs`
- Create: `src/ROROROblox.PluginTestHarness/StubMetricReportSink.cs`

**Interfaces:**
- Consumes: `IMetricReportSink`, the generated client, and whatever consent scaffolding the existing harness tests already use.

**Why this task is not redundant with Task 5.** Task 5 proves the handler calls the sink. This proves the *capability gate* actually stops an unconsented plugin over a real named pipe with the real interceptor in the loop, which is the only place that can be proven. The unit tests construct the service directly and never see `CapabilityInterceptor` at all.

Read `EndToEndContractTests.cs` in full first. Copy its consent-granting and consent-revoking helpers exactly; they are the part most easily got subtly wrong, and `StubSavedAccountsProvider` is the model for the new stub.

- [ ] **Step 1: Write the stub**

Create `src/ROROROblox.PluginTestHarness/StubMetricReportSink.cs`, modelled on `StubSavedAccountsProvider`:

```csharp
using ROROROblox.App.Plugins;

namespace ROROROblox.PluginTestHarness;

/// <summary>Records what reached the host, so a test can assert the difference between "the call
/// was denied" and "the call arrived and did nothing".</summary>
internal sealed class StubMetricReportSink : IMetricReportSink
{
    public List<(string Subject, string Metric, double Value, long At)> Reports { get; } = [];

    public void Report(string subjectId, string metricId, double value, long observedAtUnixMs)
        => Reports.Add((subjectId, metricId, value, observedAtUnixMs));
}
```

- [ ] **Step 2: Write the failing tests**

Add to `EndToEndContractTests.cs`, following that file's existing arrangement conventions:

```csharp
    [Fact]
    public async Task ReportMetric_WithConsent_ReachesTheHost()
    {
        // Over the real named pipe, through the real CapabilityInterceptor. The unit tests
        // construct PluginHostService directly and never see the interceptor at all, so this is
        // the only place the gate is actually exercised.
        var sink = new StubMetricReportSink();
        await using var host = await StartHostAsync(metricSink: sink,
            capabilities: [PluginCapability.HostMetricsReport]);

        await host.Client.ReportMetricAsync(new MetricReport
        {
            SubjectId = "11111111-1111-1111-1111-111111111111",
            MetricId = "battle.points",
            Value = 42,
            ObservedAtUnixMs = 1_700_000_000_000,
        }, host.PluginHeaders);

        var r = Assert.Single(sink.Reports);
        Assert.Equal("battle.points", r.Metric);
        Assert.Equal(42, r.Value);
    }

    [Fact]
    public async Task ReportMetric_WithoutConsent_IsDenied_AndNothingReachesTheHost()
    {
        // Both halves matter. A denial that still let the report through would be a gate in name
        // only, and asserting the status code alone would not catch it.
        var sink = new StubMetricReportSink();
        await using var host = await StartHostAsync(metricSink: sink, capabilities: []);

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            host.Client.ReportMetricAsync(new MetricReport { MetricId = "battle.points" },
                host.PluginHeaders).ResponseAsync);

        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
        Assert.Empty(sink.Reports);
    }
```

`StartHostAsync` is illustrative — use whatever the file's existing tests actually call, and extend it with a metric-sink parameter the same way it was extended for the saved-accounts stub.

- [ ] **Step 3: Run and watch fail**

Run: `dotnet test src/ROROROblox.PluginTestHarness/ -c Release`
Expected: FAIL to compile until the stub and the host-builder parameter exist.

- [ ] **Step 4: Make them pass**

Extend the harness's host builder to accept and pass the sink. No production change should be needed: if a test fails for a reason other than missing scaffolding, stop and report it — a real gate defect found here is more valuable than a green harness.

- [ ] **Step 5: Run the whole solution**

```bash
dotnet build ROROROblox.slnx -c Release && dotnet test ROROROblox.slnx -c Release --no-build
```
Expected: PASS, harness count up by 2.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.PluginTestHarness/
git commit -m "test(plugins): prove the metric gate over a real pipe, both directions"
```

---

### Task 8: The author guide, the feature ledger, and the smoke list

**Files:**
- Modify: `docs/plugins/AUTHOR_GUIDE.md`
- Modify: `docs/features.md`
- Modify: `docs/superpowers/smoke-metric-alerts.md`

**Why documentation is a task and not a footnote.** This RPC is useless without a plugin, and the only person who can write that plugin reads the author guide. An undocumented RPC is a feature nobody can reach.

- [ ] **Step 1: Add the author-guide recipe**

Add a section to `docs/plugins/AUTHOR_GUIDE.md` matching the file's existing recipe format. It must cover, in the guide's own voice:

- Declaring `host.metrics.report` in the manifest, and that a user can decline it.
- That `subject_id` is a **RoRoRo account id**, that `GetAccounts` carries `roblox_user_id` for mapping an external identity onto one, and that an unrecognised id becomes a global alert rather than an error.
- That `value` should be sent **raw and cumulative** — the host derives rates — with a worked example of a counter that resets, since that is the case authors get wrong.
- That `observed_at_unix_ms` is **when the plugin observed it, in UTC**, and that sending local time silently disables rate rules while leaving level and change rules working.
- That the host decides whether a report is worth an alert, so a plugin should report at its natural polling cadence and never try to rate-limit on the user's behalf.
- A politeness note: if the plugin polls a third-party API, respect that service's cache and terms. Name no service.

- [ ] **Step 2: Add the feature-ledger row**

Add a row to `docs/features.md` for metric alerts, following that file's existing columns. Status is honest: the reporting path works, the rule source is the interim local file, and the signed manifest is plan 3.

- [ ] **Step 3: Update the smoke list**

In `docs/superpowers/smoke-metric-alerts.md`, flip the three plan-1 rows from `[-]` to `[ ]` — they are now reachable — and add any row this plan's implementation revealed that the list does not already have. Do not tick anything: nothing in this task verifies anything on a real machine.

- [ ] **Step 4: Run the suite**

```bash
dotnet test ROROROblox.slnx -c Release
```
Expected: PASS. Docs edits can fail this suite — `ContrastPairGateTests` parses a research document and several fences read the tree from disk — so it is run even for a documentation-only change.

- [ ] **Step 5: Commit**

```bash
git add docs/
git commit -m "docs(plugins): the metric-report recipe, and what a wrong clock costs"
```

---

## Self-Review

**Spec coverage.** §1.1 `MetricBreach` on the existing rails → plan 1, re-proven end to end here by Task 7. §1.2 the plugin fetches and core decides → Tasks 1, 4, 5: the plugin reports a raw number and the host owns every decision. §1.6 the user brings the source, and no vendor ships → Tasks 1 and 3: `metric_id` is opaque, the rules file is the user's, and the binary names nothing. §3 nothing changes for existing kinds → the four alert kinds are untouched; the only shared file edited is `PluginHostService`, additively. §5.2 the RPC capability fences → Tasks 1 and 7. §6.1 the setting gets a reader → Task 4, with three tests. §6.2 future-dated observations → Task 4. §6.3 the series bound → Task 2.

**Deliberately deferred, with the plan that owns each.** §1.3 and §1.5, the signed manifest and its fallback chain → plan 3. §1.6's fence asserting no vendor hostname appears in the shipped binary → plan 3, because there is nothing to assert against until a manifest exists. §5.4 live smoke → the smoke list, Este-gated. §6.4, `WebhookPayload` formatting in the current culture, is pre-existing and outside this feature.

**Three judgement calls an implementer should not silently reverse.** The gate is read **per report**, not cached at construction, so turning the feature off is believed immediately. A future-dated observation is **dropped, not clamped**, because clamping invents an interval that never happened. The series bound **refuses newcomers rather than evicting**, because evicting would discard the series a rule is actively watching to make room for junk.

**One thing left deliberately crude.** The rules file has no UI, no validation beyond parsing, and no error surface in the app — a user with a typo sees a log line and no alerts. That is acceptable for an interim format plan 3 replaces, and would not be acceptable for a shipped one. If plan 3 slips, this is the first thing to revisit.

**Type consistency check.** `IMetricReportSink.Report(string, string, double, long)` is the signature in Tasks 4, 5 and 7. `IMetricRuleSource.CurrentRules()` is the signature in Tasks 3, 4 and 6. `MetricHistory(int capacity = 64, int maxSeries = 256)` keeps `capacity` first and named, so plan 1's `new MetricHistory(historyCapacity)` inside `MetricAlertCoordinator` is unaffected. `PluginCapability.HostMetricsReport` and the map key `"ReportMetric"` match the proto's rpc name exactly, which is what `AssertExhaustive` compares.
