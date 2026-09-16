# Metric Alert Wording and Grouping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A metric alert says what fired in words a player reads ("Diamonds went above 0", "Points stopped climbing"), and one plugin read that breaches for several accounts becomes one alert per destination instead of one per account.

**Architecture:** The breach carries the `MetricRule` that fired (new optional `Label` on the rule, new optional `Rule` on `AlertTrigger`), and `WebhookPayload` words the title and lines from it. A new Core `MetricBreachBatcher`, owned by `MetricReportSinkAdapter`, holds breaches for a fixed 5-second window from the first, one group per (metric id, rule), then raises each group as one `AlertsRaised`. `AlertRouter`, `AlertDispatcher` and the App wiring are unchanged, so mute, the per-(account, kind) cooldown, destinations and desktop fallback apply exactly as before, and the four other alert kinds never pass through the batcher.

**Tech Stack:** .NET 10, C# 14, xUnit 2.9, `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`), WPF App host, `tools/MetricSmoke` live harness.

**Spec:** [docs/superpowers/specs/2026-09-15-metric-alert-wording-design.md](../specs/2026-09-15-metric-alert-wording-design.md) (approved 2026-09-15, binding). Builds on [2026-09-09-external-metric-alerts-design.md](../specs/2026-09-09-external-metric-alerts-design.md).

**Evidence that motivates it (2026-09-15, RoRoRo 1.28 Store):** rule `{"metricId":"ps99.diamonds","kind":"Level","threshold":0,"alertWhenBelow":false}`, 8 accounts in one Ur Score read, 24 notifications (Local, Mine, Phone x 8), each Discord post `CElCPapa — ps99.diamonds` / `• CElCPapa — ps99.diamonds at 2974993`, one `AlertDispatcher` line per account per destination inside ~100 ms.

**Branch:** `feat/metric-alert-wording` (spec commit `4af260d`). Every task commits on this branch.

## Global Constraints

- **Always name the solution: `ROROROblox.slnx`.** Build **Release**; a running dev build locks `bin\Debug`.
- **`WebhookPayload` is Core and deliberately English.** One payload feeds the toast, both webhooks and the phone. No new resx keys, no UI copy, so no localization work in this plan.
- **No Core type may declare a `string Message` member** (`CoreStringBoundaryFenceTests`). Nothing in this plan is named `Message`.
- **The plugin contract is untouched:** no proto change, no new capability, no `RpcMethodCapabilityMap` entry, no `PluginHostService` change.
- **Rules file stays local and unsigned** (`%LOCALAPPDATA%\ROROROblox\metric-rules.json`). `MetricAlertsEnabled` and the four Metric alerts destination checkboxes are unchanged. No new setting, so the four private `IAppSettings` fakes are untouched.
- **Spec wording table, verbatim:**

  | Kind | Title | Line |
  | --- | --- | --- |
  | Rate | `{account} — {label} stopped climbing` | `• {account} — {rate} a minute over {window} min (alert under {threshold})` |
  | Level, below | `{account} — {label} fell below {threshold}` | `• {account} — now {value}` |
  | Level, above | `{account} — {label} went above {threshold}` | `• {account} — now {value}` |
  | Event | `{account} — {label} changed` | `• {account} — now {value}` |

  Grouped: `{n} accounts — {label} stopped climbing` (or the kind's wording), one line per account.
- **Formatting (spec):** numbers of 1,000 and up use thousands separators; small values keep `0.##`; a threshold prints the way the rule wrote it; a rule without a label falls back to the metric id.
- **Streamer masking unchanged:** every destination gets `DisplayName`; only `AlertDestination.Clan` gets `RealName`.
- **Grouping (spec):** same metric id + same rule within a short window = one alert, through the same `AlertRouter`, destinations, per-(account, kind) cooldown (an account in cooldown drops out of the group), mute and desktop fallback. Different stats or rules stay separate. Only metric breaches are grouped.
- **Nothing on the gRPC report path may throw, and nothing on a timer callback may throw** (an exception escaping a thread-pool timer callback terminates the process).
- **Tests read the source tree.** `ScenarioTableTests` counts the rows of `docs/superpowers/smoke-metric-alerts.md` as equality; a docs edit there can fail the suite.
- **A comment that goes stale is corrected in the same commit and names the date** (`corrected 2026-09-15`).
- **Never commit** `dev-cert.pfx`/`.cer`, `accounts.dat`, `consent.dat`, `discord.dat`, `notify.dat`, `webview2-data/`, `/plugins/`, `spike/`, `firebase-debug.log`, or any `.ROBLOSECURITY` value. `git add` names files explicitly; never `git add -A`.
- **Merging, tagging and the Store submission each wait for the owner's OK.** Version fourth component is always `0`; only `finalize-store-build.ps1` syncs the csproj `<Version>` and `Package.appxmanifest`.
- Commit trailer on every commit: `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

## Before Task 1: record the baseline

- [ ] Run and write the counts into the PR description draft:

```powershell
dotnet build ROROROblox.slnx -c Release
dotnet test  ROROROblox.slnx -c Release --no-build
```

Expected: 0 errors; all unit and harness tests pass, 1 harness skip by design. Note the exact unit/harness counts; the final verification in Task 5 compares against them.

## File Structure

| File | Change | Responsibility |
| --- | --- | --- |
| `src/ROROROblox.Core/Metrics/MetricRule.cs` | Modify | `MetricRule` gains trailing `string? Label = null`. |
| `src/ROROROblox.Core/Discord/AlertTrigger.cs` | Modify | `AlertTrigger` gains trailing `MetricRule? Rule = null`. |
| `src/ROROROblox.Core/Metrics/MetricAlertCoordinator.cs` | Modify | Attaches the breaching rule to the trigger. |
| `src/ROROROblox.App/Metrics/LocalFileMetricRuleSource.cs` | Modify | Reads `label`, normalises it (`NormaliseLabel`, `LabelLimit`). |
| `src/ROROROblox.Core/Discord/WebhookPayload.cs` | Modify | Words metric titles and lines from the rule; invariant number formatting. |
| `src/ROROROblox.App/Notify/PushoverSender.cs` | Modify | Caps the title at Pushover's 250-character limit. |
| `src/ROROROblox.Core/Metrics/MetricBreachBatcher.cs` | Create | Fixed-window grouping per (metric id, rule), clock-injected, thread-safe, drop-on-dispose. |
| `src/ROROROblox.App/Plugins/Adapters/MetricReportSinkAdapter.cs` | Modify | Owns the batcher; `AlertsRaised` forwards to it; becomes `IDisposable`. |
| `src/ROROROblox.App/App.xaml.cs`, `src/ROROROblox.App/Discord/AlertDispatcher.cs`, `src/ROROROblox.App/Tray/TrayService.cs` | Modify (comments only) | "raised on a gRPC handler thread" is now "a thread-pool timer thread". |
| `src/ROROROblox.Tests/Metrics/LocalFileMetricRuleSourceTests.cs` | Modify | Label parsing and normalisation. |
| `src/ROROROblox.Tests/Metrics/MetricAlertCoordinatorTests.cs` | Modify | Trigger carries the rule that fired. |
| `src/ROROROblox.Tests/Discord/WebhookPayloadTests.cs` | Modify | The wording table, formatting, fallback, masking, grouping, the live case. |
| `src/ROROROblox.Tests/Notify/PhoneAlertTests.cs` | Modify | Pushover title cap. |
| `src/ROROROblox.Tests/Metrics/MetricBreachBatcherTests.cs` | Create | Window, grouping, dedupe, concurrency, throwing subscriber, dispose. |
| `src/ROROROblox.Tests/Metrics/MetricReportSinkAdapterTests.cs` | Modify | Advance past the window; eight-accounts-one-alert; dispose. |
| `src/ROROROblox.Tests/Metrics/MetricRoutingTests.cs` | Modify | Pin: one grouped batch = one alert per destination, cooldown account drops out. |
| `src/ROROROblox.Tests/Discord/AlertDispatcherTests.cs` | Modify | Pin: grouped batch = one toast + one post naming the count. |
| `tools/MetricSmoke/Scenarios.cs`, `tools/MetricSmoke/README.md` | Modify | Value row reads `now <v>`; new grouping scenario; counts; timing note. |
| `src/ROROROblox.Tests/SmokeHarness/ScenarioTableTests.cs` | Modify | Row count 22, `SmokeMetrics.Group`, grouping-window fence. |
| `docs/superpowers/smoke-metric-alerts.md` | Modify | New `[harness]` row; `label` in the rules schema; counts. |
| `docs/features.md` | Modify | Metric alerts row and Metric-alert smoke harness row. |
| `docs/decisions.md` | Modify | 2026-09-15 decision entry. |

## Interface contract

```csharp
// Core/Metrics/MetricRule.cs
public sealed record MetricRule(
    string MetricId, MetricRuleKind Kind, double Threshold, TimeSpan Window,
    bool AlertWhenBelow = true, string? Label = null);

// Core/Discord/AlertTrigger.cs
public sealed record AlertTrigger(
    AlertKind Kind, Guid AccountId, string DisplayName, string RealName, string? GameName,
    long? PrivateBytes, DateTimeOffset OccurredAtUtc, double? MetricValue = null, MetricRule? Rule = null);
// GameName still carries the metric id for MetricBreach (the smoke harness attributes alerts by it).

// App/Metrics/LocalFileMetricRuleSource.cs
internal const int LabelLimit = 40;
internal static string? NormaliseLabel(string? raw);

// App/Notify/PushoverSender.cs
internal static string TruncateTitleForPushover(string title);   // <= 250 chars

// Core/Metrics/MetricBreachBatcher.cs
public sealed class MetricBreachBatcher(TimeProvider time, ILogger log) : IDisposable
{
    public static readonly TimeSpan Window;                              // 5 seconds
    public event EventHandler<IReadOnlyList<AlertTrigger>>? Flushed;     // one call per (metric id, rule) group
    public void Add(IReadOnlyList<AlertTrigger> triggers);
    public void Dispose();                                               // drops pending groups
}

// App/Plugins/Adapters/MetricReportSinkAdapter.cs
public sealed class MetricReportSinkAdapter(...) : IMetricReportSink, IDisposable
{
    public event EventHandler<IReadOnlyList<AlertTrigger>>? AlertsRaised;  // add/remove forward to the batcher's Flushed
    public void Dispose();
}

// tools/MetricSmoke/Scenarios.cs
SmokeMetrics.Group = "smoke.group";   // scenario "several-accounts-one-alert"
```

---

### Task 1: The breach carries the rule that fired, and rules can carry a label

**Files:**
- Modify: `src/ROROROblox.Core/Metrics/MetricRule.cs:22-29`
- Modify: `src/ROROROblox.Core/Discord/AlertTrigger.cs:1,51-69`
- Modify: `src/ROROROblox.Core/Metrics/MetricAlertCoordinator.cs:74-88`
- Modify: `src/ROROROblox.App/Metrics/LocalFileMetricRuleSource.cs:160-165,187-196`
- Test: `src/ROROROblox.Tests/Metrics/LocalFileMetricRuleSourceTests.cs`
- Test: `src/ROROROblox.Tests/Metrics/MetricAlertCoordinatorTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `MetricRule.Label` (`string?`, trailing, default null), `AlertTrigger.Rule` (`MetricRule?`, trailing, default null), `LocalFileMetricRuleSource.NormaliseLabel(string?) : string?`, `LocalFileMetricRuleSource.LabelLimit = 40`. Every `MetricBreach` trigger from `MetricAlertCoordinator.Observe` has `Rule` set to the breaching rule instance.

- [ ] **Step 1: Write the failing tests**

Append to `LocalFileMetricRuleSourceTests`:

```csharp
    [Fact]
    public void ALabel_IsReadWhenPresent_AndAbsentMeansTheMetricIdSpeaks()
    {
        // Ur Score 0.3.2 writes "label"; a hand-written 1.28 file has none and must parse exactly as
        // before. A null label is what makes the alert fall back to the metric id.
        Write("""
        [
          { "metricId": "battle.points", "kind": "Rate",  "threshold": 100, "windowMinutes": 10, "label": "Points" },
          { "metricId": "ps99.diamonds", "kind": "Level", "threshold": 0, "alertWhenBelow": false },
          { "metricId": "c",             "kind": "Event", "label": "   " }
        ]
        """);

        var rules = Sut().CurrentRules();

        Assert.Equal(3, rules.Count);
        Assert.Equal("Points", rules[0].Label);
        Assert.Null(rules[1].Label);
        Assert.Null(rules[2].Label);
    }

    [Theory]
    [InlineData("  Points  ", "Points")]
    [InlineData("Line\nbreak", "Line break")]
    [InlineData("Tab\t\there", "Tab here")]
    public void NormaliseLabel_TrimsAndFlattensControlCharacters(string raw, string expected)
    {
        // The label lands in a phone title and a bold Discord line. A newline inside it would split
        // the title from its own sentence.
        Assert.Equal(expected, LocalFileMetricRuleSource.NormaliseLabel(raw));
    }

    [Fact]
    public void NormaliseLabel_CapsALongLabelWithAnEllipsis()
    {
        var cut = LocalFileMetricRuleSource.NormaliseLabel(new string('a', 100));

        Assert.Equal(LocalFileMetricRuleSource.LabelLimit, cut!.Length);
        Assert.EndsWith("…", cut, StringComparison.Ordinal);
    }
```

Append to `MetricAlertCoordinatorTests`:

```csharp
    [Fact]
    public void ATrigger_CarriesTheRuleThatFired()
    {
        // The wording needs the label, kind, threshold, window and direction of the rule that
        // breached, not just its metric id. Same tie-break as above: the first breaching rule wins,
        // and it is that rule the trigger carries.
        var rate = new MetricRule(M, MetricRuleKind.Rate, 100, TimeSpan.FromMinutes(10), Label: "Points");
        var level = new MetricRule(M, MetricRuleKind.Level, 1000, TimeSpan.FromMinutes(10), Label: "Points");

        var (sut, clock) = New(rate, level);
        sut.Observe(Obs(0, clock.GetUtcNow()), "Masked", "Real");
        clock.Advance(TimeSpan.FromMinutes(10));

        var t = Assert.Single(sut.Observe(Obs(500, clock.GetUtcNow()), "Masked", "Real"));
        Assert.Equal(rate, t.Rule);
        Assert.Equal(M, t.GameName);          // the metric id stays where the harness looks for it
        Assert.Equal(50d, t.MetricValue);     // for Rate, the measured rate per minute
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj -c Release --filter "FullyQualifiedName~LocalFileMetricRuleSourceTests|FullyQualifiedName~MetricAlertCoordinatorTests"`
Expected: build FAILS — `MetricRule` has no parameter `Label`, `AlertTrigger` has no member `Rule`, `LocalFileMetricRuleSource` has no `NormaliseLabel`/`LabelLimit`.

- [ ] **Step 3: Implement**

`MetricRule.cs`, replace the record declaration (lines 22-29):

```csharp
/// <summary>One rule against one metric. Supplied by the caller; core never invents one.</summary>
/// <param name="AlertWhenBelow">Level only. Ignored by Rate (always "below") and Event.</param>
/// <param name="Label">The friendly name the alert uses in place of <paramref name="MetricId"/>
/// ("Points" for <c>battle.points</c>). Optional, trailing, and data rather than prose: it is the
/// user's (or their plugin's) own word, already normalised by the rule source, and
/// <c>WebhookPayload</c> falls back to the metric id when it is null.</param>
public sealed record MetricRule(
    string MetricId,
    MetricRuleKind Kind,
    double Threshold,
    TimeSpan Window,
    bool AlertWhenBelow = true,
    string? Label = null);
```

`AlertTrigger.cs`: add `using ROROROblox.Core.Metrics;` above the namespace, add a paragraph to the summary after the `MetricValue` paragraph, and widen the record:

```csharp
/// <para>
/// <paramref name="Rule"/> is the <see cref="MetricRule"/> that breached, for a
/// <see cref="AlertKind.MetricBreach"/> only — trailing and optional for the same reason as
/// <paramref name="MetricValue"/>. It exists because the alert has to say WHAT fired (label, kind,
/// threshold, window, direction), and through 1.28 nothing past the coordinator knew, so every post
/// read "ps99.diamonds at 2974993" (live test, 2026-09-15). It is also the grouping key
/// <c>MetricBreachBatcher</c> uses. <paramref name="GameName"/> keeps carrying the metric id.
/// </para>
/// </summary>
public sealed record AlertTrigger(
    AlertKind Kind,
    Guid AccountId,
    string DisplayName,
    string RealName,
    string? GameName,
    long? PrivateBytes,
    DateTimeOffset OccurredAtUtc,
    double? MetricValue = null,
    MetricRule? Rule = null);
```

`MetricAlertCoordinator.cs`, replace lines 74-88 (the GameName comment tail and the return):

```csharp
            // GameName carries the metric id rather than a field of its own — four other kinds
            // share that record and none of them wants one. The VALUE gets its own field
            // (MetricValue, trailing and optional, so those four are untouched) because
            // PrivateBytes is a long? and truncating a metric through it renders "at 0" for a 0.79
            // ratio, reintroducing at the display layer the unknown-is-not-zero conflation this
            // core defends in three places. The RULE rides along too (2026-09-15): the alert's
            // wording comes from it, and MetricBreachBatcher groups by it.
            return [new AlertTrigger(
                AlertKind.MetricBreach,
                o.AccountId,
                displayName,
                realName,
                o.MetricId,
                null,
                now,
                verdict.Observed,
                rule)];
```

`LocalFileMetricRuleSource.cs`, replace the `rules.Add(...)` call (lines 160-165):

```csharp
                rules.Add(new MetricRule(
                    parsed.MetricId,
                    kind,
                    parsed.Threshold,
                    TimeSpan.FromMinutes(parsed.WindowMinutes),
                    parsed.AlertWhenBelow,
                    NormaliseLabel(parsed.Label)));
```

Add to `RuleRow` (after `AlertWhenBelow`):

```csharp
        [JsonPropertyName("label")] public string? Label { get; set; }
```

Add these members above `private sealed record RuleCache`:

```csharp
    /// <summary>The longest label an alert will print. Forty covers "Diamonds per minute in the clan
    /// battle" with room to spare; past it the label is cut with an ellipsis rather than dropped.</summary>
    internal const int LabelLimit = 40;

    /// <summary>
    /// Turns a hand-written or plugin-written label into something safe to put in a title: control
    /// characters (a newline above all, which would split the title from its own sentence) become
    /// spaces, runs of spaces collapse, the ends are trimmed, and anything over
    /// <see cref="LabelLimit"/> is cut with an ellipsis without splitting a surrogate pair. Blank
    /// becomes null, which makes the alert fall back to the metric id exactly as 1.28 did.
    /// </summary>
    internal static string? NormaliseLabel(string? raw)
    {
        if (raw is null) return null;

        var flattened = new StringBuilder(raw.Length);
        foreach (var c in raw) flattened.Append(char.IsControl(c) ? ' ' : c);

        var cleaned = string.Join(' ', flattened.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length == 0) return null;
        if (cleaned.Length <= LabelLimit) return cleaned;

        var cut = LabelLimit - 1;
        if (char.IsHighSurrogate(cleaned[cut - 1])) cut--;
        return cleaned[..cut].TrimEnd() + "…";
    }
```

Also extend the class summary's "Unsigned on purpose" paragraph: after "A rule names only a metric id, a kind, a threshold and a window," insert " plus an optional display label (2026-09-15)," so the sentence stays true.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj -c Release --filter "FullyQualifiedName~ROROROblox.Tests.Metrics|FullyQualifiedName~ScenarioTableTests|FullyQualifiedName~CoreStringBoundaryFenceTests"`
Expected: PASS (all existing metric tests still green: every widened record parameter is trailing and optional).

- [ ] **Step 5: Commit**

```powershell
git add src/ROROROblox.Core/Metrics/MetricRule.cs src/ROROROblox.Core/Discord/AlertTrigger.cs src/ROROROblox.Core/Metrics/MetricAlertCoordinator.cs src/ROROROblox.App/Metrics/LocalFileMetricRuleSource.cs src/ROROROblox.Tests/Metrics/LocalFileMetricRuleSourceTests.cs src/ROROROblox.Tests/Metrics/MetricAlertCoordinatorTests.cs
git commit -m "feat(metrics): the breach carries the rule that fired, and rules take a label" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: Wording from the rule

**Files:**
- Modify: `src/ROROROblox.Core/Discord/WebhookPayload.cs` (whole `ForAlert` metric arms + new private helpers)
- Modify: `src/ROROROblox.App/Notify/PushoverSender.cs:37-38,83`
- Test: `src/ROROROblox.Tests/Discord/WebhookPayloadTests.cs`
- Test: `src/ROROROblox.Tests/Notify/PhoneAlertTests.cs` (`PushoverTruncationTests`)

**Interfaces:**
- Consumes: `AlertTrigger.Rule`, `MetricRule.Label` (Task 1).
- Produces: `WebhookPayload.ForAlert(AlertKind.MetricBreach, ...)` renders the spec table. A trigger with `Rule == null` renders exactly as 1.28. `PushoverSender.TruncateTitleForPushover(string) : string`.

- [ ] **Step 1: Write the failing tests**

In `WebhookPayloadTests.cs` add `using System.Globalization;` and `using ROROROblox.Core.Metrics;`, then append inside the class:

```csharp
    private static readonly DateTimeOffset At = new(2026, 9, 15, 3, 14, 0, TimeSpan.Zero);

    private static AlertTrigger Fired(MetricRule rule, string name, double? value) =>
        new(AlertKind.MetricBreach, Guid.NewGuid(), name, $"real_{name}", rule.MetricId, null, At, value, rule);

    private static readonly MetricRule DiamondsAbove =
        new("ps99.diamonds", MetricRuleKind.Level, 0, TimeSpan.Zero, AlertWhenBelow: false, Label: "Diamonds");

    [Fact]
    public void TheLiveTestCase_ReadsAsASentenceWithSeparators()
    {
        // 2026-09-15, RoRoRo 1.28: this exact rule and value posted "CElCPapa — ps99.diamonds" /
        // "• CElCPapa — ps99.diamonds at 2974993".
        var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, [Fired(DiamondsAbove, "CElCPapa", 2974993)]);

        Assert.Equal("CElCPapa — Diamonds went above 0", payload.Title);
        Assert.Equal("• CElCPapa — now 2,974,993", payload.Body);
    }

    [Fact]
    public void Rate_SaysStoppedClimbing_WithTheRateTheWindowAndTheFloor()
    {
        var rule = new MetricRule("battle.points", MetricRuleKind.Rate, 5000, TimeSpan.FromMinutes(10), Label: "Points");

        var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, [Fired(rule, "CElCPapa", 1234.5)]);

        Assert.Equal("CElCPapa — Points stopped climbing", payload.Title);
        Assert.Equal("• CElCPapa — 1,234.5 a minute over 10 min (alert under 5,000)", payload.Body);
    }

    [Fact]
    public void LevelBelow_SaysFellBelow_AndKeepsTheFraction()
    {
        var rule = new MetricRule("battle.share", MetricRuleKind.Level, 0.8, TimeSpan.Zero, Label: "Share");

        var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, [Fired(rule, "BaronBloxwell", 0.79)]);

        Assert.Equal("BaronBloxwell — Share fell below 0.8", payload.Title);
        Assert.Equal("• BaronBloxwell — now 0.79", payload.Body);
    }

    [Fact]
    public void Event_SaysChanged()
    {
        var rule = new MetricRule("battle.place", MetricRuleKind.Event, 0, TimeSpan.Zero, Label: "Place");

        var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, [Fired(rule, "BaronBloxwell", 3)]);

        Assert.Equal("BaronBloxwell — Place changed", payload.Title);
        Assert.Equal("• BaronBloxwell — now 3", payload.Body);
    }

    [Fact]
    public void NoLabel_FallsBackToTheMetricId()
    {
        var rule = DiamondsAbove with { Label = null };

        var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, [Fired(rule, "CElCPapa", 5)]);

        Assert.Equal("CElCPapa — ps99.diamonds went above 0", payload.Title);
    }

    [Fact]
    public void EightAccounts_AreOneTitleAndOneLineEach()
    {
        var triggers = Enumerable.Range(1, 8).Select(i => Fired(DiamondsAbove, $"Alt{i}", 1000 * i)).ToList();

        var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, triggers);

        Assert.Equal("8 accounts — Diamonds went above 0", payload.Title);
        var lines = payload.Body.Split('\n');
        Assert.Equal(8, lines.Length);
        Assert.Equal("• Alt1 — now 1,000", lines[0]);
        Assert.Equal("• Alt8 — now 8,000", lines[7]);
    }

    [Fact]
    public void StreamerMasking_IsUnchanged_OnlyTheClanRoomGetsRealNames()
    {
        var trigger = Fired(DiamondsAbove, "DoctorDuck", 5);

        var masked = WebhookPayload.ForAlert(AlertKind.MetricBreach, [trigger]);
        var clan = WebhookPayload.ForAlert(AlertKind.MetricBreach, [trigger], useRealNames: true);

        Assert.Equal("DoctorDuck — Diamonds went above 0", masked.Title);
        Assert.DoesNotContain("real_", masked.Body, StringComparison.Ordinal);
        Assert.Equal("real_DoctorDuck — Diamonds went above 0", clan.Title);
        Assert.Equal("• real_DoctorDuck — now 5", clan.Body);
    }

    [Fact]
    public void AThreshold_PrintsAsWritten_NotRoundedToTwoPlaces()
    {
        var rule = new MetricRule("battle.share", MetricRuleKind.Level, 0.125, TimeSpan.Zero, Label: "Share");

        var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, [Fired(rule, "A", 0.1)]);

        Assert.Equal("A — Share fell below 0.125", payload.Title);
    }

    [Fact]
    public void Numbers_DoNotFollowTheMachineCulture()
    {
        // The sentence around the number is English, so the number is too: "2.974.993,5" in a German
        // locale would sit inside "now ..." and read as nonsense.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, [Fired(DiamondsAbove, "A", 2974993.5)]);
            Assert.Equal("• A — now 2,974,993.5", payload.Body);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ARuleBreachWithNoValue_ClaimsNoNumber()
    {
        // Unreachable from the evaluator (an unmeasurable rate is never a breach), but no reading is
        // not a reading of zero, here too.
        var rule = new MetricRule("battle.points", MetricRuleKind.Rate, 100, TimeSpan.FromMinutes(10), Label: "Points");

        var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, [Fired(rule, "A", null)]);

        Assert.Equal("• A", payload.Body);
    }
```

Keep the two existing metric tests (`ForAlert_MetricBreach_RendersTheObservedValueWithoutFlatteningIt`, `ForAlert_MetricBreachWithNoValue_FallsThroughToTheGenericLine`) unchanged: their triggers carry no `Rule`, and they now pin the 1.28 rendering of a rule-less trigger. Add this sentence to the top of the first one's comment: `// No Rule on this trigger: this pins the 1.28 rendering a rule-less trigger keeps (2026-09-15).`

Append inside `PushoverTruncationTests` in `PhoneAlertTests.cs`:

```csharp
    [Fact]
    public void TruncateTitleForPushover_CapsAtTheApiLimit()
    {
        // Pushover answers an over-limit title with the same bare 400 a bad credential gets, which
        // latches EndpointRejected for the session. A metric id is plugin-supplied and unbounded, and
        // a rule without a label puts it in the title.
        var title = "8 accounts — " + new string('x', 400) + " went above 0";

        var cut = PushoverSender.TruncateTitleForPushover(title);

        Assert.Equal(250, cut.Length);
        Assert.EndsWith("…", cut, StringComparison.Ordinal);
    }

    [Fact]
    public void TruncateTitleForPushover_ShortTitlePassesThroughUntouched()
    {
        Assert.Equal("8 accounts — Diamonds went above 0",
            PushoverSender.TruncateTitleForPushover("8 accounts — Diamonds went above 0"));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj -c Release --filter "FullyQualifiedName~WebhookPayloadTests|FullyQualifiedName~PushoverTruncationTests"`
Expected: build FAILS on `TruncateTitleForPushover`; after stubbing it, the new `WebhookPayloadTests` FAIL with titles like `CElCPapa — ps99.diamonds`.

- [ ] **Step 3: Implement**

`WebhookPayload.cs`: add `using System.Globalization;` and `using ROROROblox.Core.Metrics;` above the namespace. In `ForAlert`, replace the `MetricBreach` title arm:

```csharp
            // Worded from the rule that fired (2026-09-15). A trigger without one — nothing in
            // production builds that since the coordinator attaches the rule — keeps 1.28's
            // "{noun} — {metric id}".
            AlertKind.MetricBreach => MetricTitle(noun, triggers[0]),
```

In the lines switch, replace the comment block that begins `// GameName carries the metric id, MetricValue the observed number. No unit:` together with the one arm under it (ending `$"• {Name(t)} — {t.GameName} at {v:0.##}",`) with:

```csharp
            // Worded from the rule (2026-09-15): Rate gives the measured rate, its window and the
            // floor; Level and Event give the current value. The rule-less arm below it is 1.28's
            // line, kept for a trigger built without a rule. `0.##` there, invariant `#,0.##` here:
            // see FormatNumber.
            AlertKind.MetricBreach when t.Rule is { } rule =>
                MetricLine(Name(t), rule, t.MetricValue),
            AlertKind.MetricBreach when t.MetricValue is { } v =>
                $"• {Name(t)} — {t.GameName} at {v:0.##}",
```

Add these private members to the record, below `ForAlert`:

```csharp
    /// <summary>
    /// A metric breach's title, from the rule the FIRST trigger carries. A batch handed to
    /// <see cref="ForAlert"/> shares one (metric id, rule) — <c>MetricBreachBatcher</c> raises one
    /// group per call and <c>AlertRouter</c> keeps a call together — so the first trigger's rule is
    /// the batch's rule. The noun is already "{n} accounts" or the one account's name.
    /// </summary>
    private static string MetricTitle(string noun, AlertTrigger first)
    {
        if (first.Rule is not { } rule) return $"{noun} — {first.GameName}";

        var label = rule.Label ?? rule.MetricId;
        return rule.Kind switch
        {
            MetricRuleKind.Rate => $"{noun} — {label} stopped climbing",
            MetricRuleKind.Level when rule.AlertWhenBelow => $"{noun} — {label} fell below {FormatThreshold(rule.Threshold)}",
            MetricRuleKind.Level => $"{noun} — {label} went above {FormatThreshold(rule.Threshold)}",
            MetricRuleKind.Event => $"{noun} — {label} changed",
            _ => $"{noun} — {label}",
        };
    }

    /// <summary>
    /// One account's line. No value means no number, never zero. A Rate breach's value is the
    /// measured rate per minute, and it is never negative: <c>MetricHistory.RatePerMinute</c>
    /// returns null on any decrease or zero span, and <c>MetricEvaluator</c> treats null as no breach.
    /// </summary>
    private static string MetricLine(string name, MetricRule rule, double? value) => (rule.Kind, value) switch
    {
        (_, null) => $"• {name}",
        (MetricRuleKind.Rate, { } rate) =>
            $"• {name} — {FormatNumber(rate)} a minute over {FormatNumber(rule.Window.TotalMinutes)} min (alert under {FormatThreshold(rule.Threshold)})",
        (_, { } v) => $"• {name} — now {FormatNumber(v)}",
    };

    /// <summary>
    /// A reading: thousands separators from 1,000 up, at most two decimals below that ("50", "0.79",
    /// "2,974,993"). INVARIANT culture on purpose: the sentence around the number is English, and a
    /// German machine's "2.974.993" inside "now ..." reads as a decimal.
    /// </summary>
    private static string FormatNumber(double value) =>
        value.ToString("#,0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// A threshold, as the rule wrote it: every decimal the user typed survives (0.125 stays 0.125,
    /// where <see cref="FormatNumber"/> would round it to 0.13), with the same separators and culture.
    /// </summary>
    private static string FormatThreshold(double value) =>
        value.ToString("#,0.##########", CultureInfo.InvariantCulture);
```

`PushoverSender.cs`: below `MessageLimit` add

```csharp
    /// <summary>Pushover's documented title cap.</summary>
    private const int TitleLimit = 250;

    /// <summary>
    /// Cut an over-long title with an ellipsis. A grouped title uses the account COUNT, so it does
    /// not grow with accounts, but a rule without a label puts the plugin-supplied metric id in it,
    /// and that is unbounded. Over the cap, Pushover returns the bare 400 the caller treats as
    /// terminal for the session (2026-09-15).
    /// </summary>
    internal static string TruncateTitleForPushover(string title) =>
        title.Length <= TitleLimit ? title : title[..(TitleLimit - 1)] + "…";
```

and change the form field to `["title"] = TruncateTitleForPushover(payload.Title),`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj -c Release --filter "FullyQualifiedName~ROROROblox.Tests.Discord|FullyQualifiedName~ROROROblox.Tests.Notify|FullyQualifiedName~ROROROblox.Tests.Metrics"`
Expected: PASS, including the two unchanged rule-less metric tests and every other kind's payload test.

- [ ] **Step 5: Commit**

```powershell
git add src/ROROROblox.Core/Discord/WebhookPayload.cs src/ROROROblox.App/Notify/PushoverSender.cs src/ROROROblox.Tests/Discord/WebhookPayloadTests.cs src/ROROROblox.Tests/Notify/PhoneAlertTests.cs
git commit -m "feat(alerts): metric alerts say what fired, in words and readable numbers" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: One alert per read

**Files:**
- Create: `src/ROROROblox.Core/Metrics/MetricBreachBatcher.cs`
- Modify: `src/ROROROblox.App/Plugins/Adapters/MetricReportSinkAdapter.cs:7-20,32-51,106-107` (+ `Dispose`)
- Modify (comments only): `src/ROROROblox.App/App.xaml.cs:1868-1870`, `src/ROROROblox.App/Discord/AlertDispatcher.cs:18,34-35`, `src/ROROROblox.App/Tray/TrayService.cs:317-318`
- Test: `src/ROROROblox.Tests/Metrics/MetricBreachBatcherTests.cs` (create)
- Test: `src/ROROROblox.Tests/Metrics/MetricReportSinkAdapterTests.cs`
- Test: `src/ROROROblox.Tests/Metrics/MetricRoutingTests.cs`
- Test: `src/ROROROblox.Tests/Discord/AlertDispatcherTests.cs`

**Interfaces:**
- Consumes: `AlertTrigger.Rule`, `AlertTrigger.GameName` (metric id), `WebhookPayload` grouped titles (Tasks 1-2).
- Produces: `MetricBreachBatcher` (see Interface contract). `MetricReportSinkAdapter : IDisposable`; `AlertsRaised` now fires once per (metric id, rule) group, `MetricBreachBatcher.Window` after the group's first breach, on a thread-pool timer thread. App wiring (`WireAlertsAsync`) is unchanged.

- [ ] **Step 1: Write the failing batcher tests**

Create `src/ROROROblox.Tests/Metrics/MetricBreachBatcherTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ROROROblox.Core.Discord;
using ROROROblox.Core.Metrics;

namespace ROROROblox.Tests.Metrics;

public class MetricBreachBatcherTests
{
    private static readonly MetricRule Points =
        new("battle.points", MetricRuleKind.Rate, 100, TimeSpan.FromMinutes(10), Label: "Points");

    private static readonly MetricRule PointsLevel =
        new("battle.points", MetricRuleKind.Level, 1000, TimeSpan.Zero, Label: "Points");

    private static readonly MetricRule Diamonds =
        new("ps99.diamonds", MetricRuleKind.Level, 0, TimeSpan.Zero, AlertWhenBelow: false, Label: "Diamonds");

    private static AlertTrigger Breach(MetricRule rule, Guid account, double value) =>
        new(AlertKind.MetricBreach, account, "Masked", "Real", rule.MetricId, null, DateTimeOffset.UnixEpoch, value, rule);

    private static (MetricBreachBatcher Sut, FakeTimeProvider Clock, List<IReadOnlyList<AlertTrigger>> Flushed) New()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var sut = new MetricBreachBatcher(clock, NullLogger.Instance);
        var flushed = new List<IReadOnlyList<AlertTrigger>>();
        sut.Flushed += (_, batch) => { lock (flushed) flushed.Add(batch); };
        return (sut, clock, flushed);
    }

    [Fact]
    public void NothingLeaves_UntilTheWindowCloses()
    {
        var (sut, clock, flushed) = New();

        sut.Add([Breach(Points, Guid.NewGuid(), 50)]);
        clock.Advance(MetricBreachBatcher.Window - TimeSpan.FromTicks(1));
        Assert.Empty(flushed);

        clock.Advance(TimeSpan.FromTicks(1));
        Assert.Single(Assert.Single(flushed));
    }

    [Fact]
    public void TheWindowIsFixedFromTheFirstBreach_NotSliding()
    {
        // A steady trickle of breaches must not postpone the alert forever: the group closes
        // Window after its FIRST breach, and a breach after that starts a new group.
        var (sut, clock, flushed) = New();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        sut.Add([Breach(Points, a, 50)]);
        clock.Advance(MetricBreachBatcher.Window - TimeSpan.FromSeconds(1));
        sut.Add([Breach(Points, b, 40)]);
        clock.Advance(TimeSpan.FromSeconds(1));

        var first = Assert.Single(flushed);
        Assert.Equal(new[] { a, b }, first.Select(t => t.AccountId).ToArray());

        sut.Add([Breach(Points, c, 30)]);
        clock.Advance(MetricBreachBatcher.Window);

        Assert.Equal(2, flushed.Count);
        Assert.Equal(c, Assert.Single(flushed[1]).AccountId);
    }

    [Fact]
    public void DifferentStatsOrRules_StaySeparateAlerts()
    {
        // Same metric id, different rule, is a different alert too: "Points stopped climbing" and
        // "Points fell below 1,000" are different news.
        var (sut, clock, flushed) = New();

        sut.Add([Breach(Points, Guid.NewGuid(), 50)]);
        sut.Add([Breach(PointsLevel, Guid.NewGuid(), 500)]);
        sut.Add([Breach(Diamonds, Guid.NewGuid(), 2_974_993)]);
        sut.Add([Breach(Points, Guid.NewGuid(), 40)]);
        clock.Advance(MetricBreachBatcher.Window);

        Assert.Equal(3, flushed.Count);
        Assert.All(flushed, batch => Assert.Single(batch.Select(t => t.Rule).Distinct()));
        Assert.Equal(2, Assert.Single(flushed, batch => batch[0].Rule == Points).Count);
    }

    [Fact]
    public void TheSameAccountTwiceInOneWindow_IsListedOnce_WithItsLatestReading()
    {
        // Listing one alt twice at two values is the clan-channel defect the coordinator's
        // one-trigger-per-call rule exists to prevent; a window must not reintroduce it.
        var (sut, clock, flushed) = New();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        sut.Add([Breach(Points, a, 50)]);
        sut.Add([Breach(Points, b, 45)]);
        sut.Add([Breach(Points, a, 20)]);
        clock.Advance(MetricBreachBatcher.Window);

        var batch = Assert.Single(flushed);
        Assert.Equal(new[] { a, b }, batch.Select(t => t.AccountId).ToArray());
        Assert.Equal(20d, batch[0].MetricValue);
    }

    [Fact]
    public void ReportsFromManyThreadsAtOnce_AllLandInOneAlert()
    {
        // Reports arrive on gRPC handler threads, several at once during a plugin read.
        var (sut, clock, flushed) = New();
        var accounts = Enumerable.Range(0, 16).Select(_ => Guid.NewGuid()).ToArray();

        Parallel.ForEach(accounts, new ParallelOptions { MaxDegreeOfParallelism = 16 },
            account => sut.Add([Breach(Points, account, 1)]));
        clock.Advance(MetricBreachBatcher.Window);

        var batch = Assert.Single(flushed);
        Assert.Equal(accounts.Order().ToArray(), batch.Select(t => t.AccountId).Order().ToArray());
    }

    [Fact]
    public void ASubscriberThatThrows_StaysInsideTheTimer_AndTheNextGroupStillLeaves()
    {
        // An exception escaping a thread-pool timer callback ends the process. FakeTimeProvider runs
        // the callback inside Advance, so an uncaught throw would surface right here.
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var sut = new MetricBreachBatcher(clock, NullLogger.Instance);
        var calls = 0;
        sut.Flushed += (_, _) =>
        {
            if (++calls == 1) throw new InvalidOperationException("subscriber blew up");
        };

        sut.Add([Breach(Points, Guid.NewGuid(), 1)]);
        Assert.Null(Record.Exception(() => clock.Advance(MetricBreachBatcher.Window)));

        sut.Add([Breach(Points, Guid.NewGuid(), 1)]);
        clock.Advance(MetricBreachBatcher.Window);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Dispose_DropsWhatIsPending_AndIgnoresAnythingAfter()
    {
        var (sut, clock, flushed) = New();

        sut.Add([Breach(Points, Guid.NewGuid(), 1)]);
        sut.Dispose();
        clock.Advance(MetricBreachBatcher.Window);

        sut.Add([Breach(Points, Guid.NewGuid(), 1)]);
        clock.Advance(MetricBreachBatcher.Window);

        Assert.Empty(flushed);
        sut.Dispose();   // twice is harmless: the container and a test may both call it
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj -c Release --filter "FullyQualifiedName~MetricBreachBatcherTests"`
Expected: build FAILS — `MetricBreachBatcher` does not exist.

- [ ] **Step 3: Implement the batcher**

Create `src/ROROROblox.Core/Metrics/MetricBreachBatcher.cs`:

```csharp
using Microsoft.Extensions.Logging;
using ROROROblox.Core.Discord;

namespace ROROROblox.Core.Metrics;

/// <summary>
/// Holds metric breaches for a short, FIXED window and raises them one group per (metric id, rule),
/// so one plugin read that breaches for eight accounts is one alert per destination rather than
/// eight. Measured live on 2026-09-15: eight accounts in one read, ~100 ms apart, became 24
/// notifications, because a plugin reports one <c>ReportMetric</c> per account and
/// <see cref="AlertRouter"/> only groups triggers that arrive in the same call.
///
/// <para>
/// <b>Why here and not in the router.</b> The router's per-call grouping is shared with four
/// already-shipped kinds, and the metric sink is the only path through this class — so "only metric
/// breaches are grouped" is true by construction. Each group leaves as ONE <see cref="Flushed"/>
/// call, the sink hands that call to <c>AlertDispatcher.DispatchAsync</c> whole, and the router's
/// existing per-call behaviour then makes it one alert per destination, with mute, the
/// per-(account, kind) cooldown and desktop fallback applied exactly as before. Two groups never
/// share a call; if they did, the router would fold them into one alert with one rule's title.
/// </para>
/// <para>
/// <b>Fixed, not sliding.</b> A group closes <see cref="Window"/> after its first breach. A sliding
/// window would let a steady trickle postpone an alert indefinitely.
/// </para>
/// <para>
/// <b>Threads.</b> <see cref="Add"/> is called on gRPC handler threads, several at once; the flush
/// runs on a thread-pool timer thread. One lock guards the pending map. A group's timer is created
/// under that lock and its callback takes the lock first, so a flush can never run before its group
/// is registered. The event is raised outside the lock, and nothing it throws escapes: an
/// exception leaving a timer callback terminates the process.
/// </para>
/// <para>
/// <b>Exit.</b> <see cref="Dispose"/> drops what is still pending rather than flushing it. By then
/// the plugin host has been stopped and the container is disposing the tray and the HTTP clients a
/// flush would reach; the user is at the PC, quitting; and a condition that still holds breaches
/// again on the plugin's next read in the next session.
/// </para>
/// </summary>
/// <param name="log">The owning sink's logger. Not resolved from DI.</param>
public sealed class MetricBreachBatcher(TimeProvider time, ILogger log) : IDisposable
{
    /// <summary>
    /// How long a group stays open after its first breach. The live read spread was ~100 ms, so
    /// five seconds is fifty times that, and it is small beside the five-minute cooldown and a Rate
    /// window measured in minutes. <c>ScenarioTableTests</c> keeps it within half the smoke
    /// harness's alert window.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    private readonly TimeProvider _time = time ?? throw new ArgumentNullException(nameof(time));
    private readonly ILogger _log = log ?? throw new ArgumentNullException(nameof(log));
    private readonly object _gate = new();
    private readonly Dictionary<GroupKey, PendingGroup> _pending = [];
    private bool _disposed;

    /// <summary>One call per closed group. The sender is this batcher.</summary>
    public event EventHandler<IReadOnlyList<AlertTrigger>>? Flushed;

    public void Add(IReadOnlyList<AlertTrigger> triggers)
    {
        ArgumentNullException.ThrowIfNull(triggers);

        lock (_gate)
        {
            if (_disposed) return;

            foreach (var trigger in triggers)
            {
                var key = new GroupKey(trigger.GameName, trigger.Rule);
                if (!_pending.TryGetValue(key, out var group))
                {
                    group = new PendingGroup();
                    _pending[key] = group;
                    group.Timer = _time.CreateTimer(_ => Flush(key), null, Window, Timeout.InfiniteTimeSpan);
                }

                // The same account twice in one window keeps its place and its latest reading.
                var existing = group.Triggers.FindIndex(t => t.AccountId == trigger.AccountId);
                if (existing >= 0) group.Triggers[existing] = trigger;
                else group.Triggers.Add(trigger);
            }
        }
    }

    public void Dispose()
    {
        var dropped = 0;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var group in _pending.Values)
            {
                group.Timer?.Dispose();
                dropped += group.Triggers.Count;
            }
            _pending.Clear();
        }

        if (dropped > 0)
        {
            _log.LogInformation(
                "Exiting with {Count} metric breach(es) still inside the {Seconds}-second grouping window; they were not sent.",
                dropped, Window.TotalSeconds);
        }
    }

    private void Flush(GroupKey key)
    {
        List<AlertTrigger> triggers;
        lock (_gate)
        {
            if (!_pending.Remove(key, out var group)) return;
            group.Timer?.Dispose();
            triggers = group.Triggers;
        }

        try
        {
            Flushed?.Invoke(this, triggers);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "A grouped metric alert for {MetricId} was dropped.", key.MetricId);
        }
    }

    private readonly record struct GroupKey(string? MetricId, MetricRule? Rule);

    private sealed class PendingGroup
    {
        public readonly List<AlertTrigger> Triggers = [];
        public ITimer? Timer;
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj -c Release --filter "FullyQualifiedName~MetricBreachBatcherTests"`
Expected: PASS (7 tests).

- [ ] **Step 5: Write the failing sink tests, and keep the existing ones honest**

In `MetricReportSinkAdapterTests.cs`, every existing test must now let the window close before it asserts; an `Assert.Empty` checked before the window closes proves nothing. Make these exact edits:

1. `ABreach_RaisesATrigger_CarryingBothNames`: after the line `sut.Report(Acct.ToString(), M, 500, Ms(clock.GetUtcNow()));   // 50/min, floor is 100` insert
   ```csharp
           Assert.Empty(raised);                        // held for the grouping window
           clock.Advance(MetricBreachBatcher.Window);
   ```
2. `WhenTheSettingIsOff_NothingIsEvenRecorded`: immediately before `Assert.Empty(raised);` insert `clock.Advance(MetricBreachBatcher.Window);`.
3. `TurningTheGateOn_TakesEffectOnTheNextReport`: immediately before `Assert.Empty(raised);` insert `clock.Advance(MetricBreachBatcher.Window);`, and immediately before `Assert.Single(raised);` insert `clock.Advance(MetricBreachBatcher.Window);`.
4. `AFutureDatedReport_IsDropped_NotClamped`: immediately before `// Had it been clamped to now, this would have been a 50/min breach.` insert `clock.Advance(MetricBreachBatcher.Window);`.
5. `ASlightlyFutureReport_IsAccepted`: immediately before `Assert.Single(raised);` insert `clock.Advance(MetricBreachBatcher.Window);`.
6. `AnUnparseableSubject_BecomesTheGlobalCarrier`: immediately before `Assert.Equal(Guid.Empty, Assert.Single(raised).AccountId);` insert `clock.Advance(MetricBreachBatcher.Window);`.
7. `NoRules_MeansNoWork_AndNoAlert`: immediately before `Assert.Empty(raised);` insert `clock.Advance(MetricBreachBatcher.Window);`.
8. `ASubscriberThatThrows_DoesNotThrowIntoTheReporter`: replace
   ```csharp
           var ex = Record.Exception(() => sut.Report(Acct.ToString(), M, 400, Ms(clock.GetUtcNow())));
   ```
   with
   ```csharp
           // Since 2026-09-15 the subscriber runs when the grouping window closes, on the timer, so
           // both the report and the flush must stay quiet.
           var ex = Record.Exception(() =>
           {
               sut.Report(Acct.ToString(), M, 400, Ms(clock.GetUtcNow()));
               clock.Advance(MetricBreachBatcher.Window);
           });
   ```

Then append two tests:

```csharp
    [Fact]
    public void EightAccountsInOneRead_AreOneAlertOfEight()
    {
        // The live test on 2026-09-15: this rule, eight accounts in one Ur Score read ~100 ms apart,
        // and 24 notifications — one per account per destination.
        var rule = new MetricRule("ps99.diamonds", MetricRuleKind.Level, 0, TimeSpan.Zero,
            AlertWhenBelow: false, Label: "Diamonds");
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var sut = new MetricReportSinkAdapter(
            new FixedRules(rule), () => true, _ => ("Masked", "Real"), clock,
            NullLogger<MetricReportSinkAdapter>.Instance);
        var batches = new List<IReadOnlyList<AlertTrigger>>();
        sut.AlertsRaised += (_, t) => batches.Add(t);

        for (var i = 0; i < 8; i++)
        {
            sut.Report(Guid.NewGuid().ToString(), "ps99.diamonds", 2_974_993 + i, Ms(clock.GetUtcNow()));
            clock.Advance(TimeSpan.FromMilliseconds(12));
        }
        Assert.Empty(batches);

        clock.Advance(MetricBreachBatcher.Window);

        var batch = Assert.Single(batches);
        Assert.Equal(8, batch.Count);
        Assert.All(batch, t => Assert.Equal(rule, t.Rule));
    }

    [Fact]
    public void DisposingTheSink_DropsAGroupStillInsideTheWindow()
    {
        // The container disposes the sink on exit; its timers must not outlive it.
        var (sut, clock, raised) = New(rules: new FixedRules(new MetricRule(M, MetricRuleKind.Level, 500, TimeSpan.Zero)));

        sut.Report(Acct.ToString(), M, 400, Ms(clock.GetUtcNow()));
        sut.Dispose();
        clock.Advance(MetricBreachBatcher.Window);

        Assert.Empty(raised);
    }
```

- [ ] **Step 6: Run to verify the new sink tests fail**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj -c Release --filter "FullyQualifiedName~MetricReportSinkAdapterTests"`
Expected: build FAILS on `sut.Dispose()` (the sink is not `IDisposable`). Against today's synchronous raise, `EightAccountsInOneRead_AreOneAlertOfEight` would also fail at `Assert.Empty(batches)`, because it sees 8 immediate raises.

- [ ] **Step 7: Wire the batcher into the sink**

In `MetricReportSinkAdapter.cs`:

Class summary, replace `resolves the subject to the names an alert carries, and RAISES` / `the resulting triggers rather than sending them.` with:

```csharp
/// resolves the subject to the names an alert carries, and RAISES the resulting triggers rather than
/// sending them — grouped per (metric id, rule) by <see cref="MetricBreachBatcher"/>, so one plugin
/// read is one alert (2026-09-15).
```

Change the declaration line `ILogger<MetricReportSinkAdapter> log) : IMetricReportSink` to `ILogger<MetricReportSinkAdapter> log) : IMetricReportSink, IDisposable`.

Replace the `AlertsRaised` field-like event (lines 49-51) with:

```csharp
    private readonly MetricBreachBatcher _batcher = new(time, log);

    /// <summary>Fired once per (metric id, rule) group, <see cref="MetricBreachBatcher.Window"/> after
    /// the group's first breach, on a thread-pool timer thread. <c>App</c> subscribes this to the alert
    /// dispatcher. Forwarded to the batcher rather than raised here, so every subscriber sees groups and
    /// no subscriber can see a single ungrouped breach.</summary>
    public event EventHandler<IReadOnlyList<AlertTrigger>>? AlertsRaised
    {
        add => _batcher.Flushed += value;
        remove => _batcher.Flushed -= value;
    }
```

Replace `AlertsRaised?.Invoke(this, triggers);` (line 107) with `_batcher.Add(triggers);`.

Add below `Report`:

```csharp
    /// <summary>Stops the grouping timers and drops any group still inside its window. Called by the
    /// container on exit, after the plugin host has stopped.</summary>
    public void Dispose() => _batcher.Dispose();
```

- [ ] **Step 8: Run to verify the sink tests pass**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj -c Release --filter "FullyQualifiedName~MetricReportSinkAdapterTests|FullyQualifiedName~MetricReportWiringTests|FullyQualifiedName~ReportMetricHandlerTests"`
Expected: PASS.

- [ ] **Step 9: Pin the downstream contract the grouping relies on**

These pass against today's router and dispatcher. They are pins: if a later change to either breaks "one group, one alert per destination", they fail.

Append to `MetricRoutingTests`:

```csharp
    [Fact]
    public void AGroupedBatch_IsOneAlertPerDestination_AndAnAccountInCooldownDropsOut()
    {
        // MetricBreachBatcher raises one (metric, rule) group per call; this is what turns that call
        // into exactly one alert per destination, with a cooling account simply absent.
        var cooling = Guid.NewGuid();
        var batch = new[] { Breach(Guid.NewGuid()), Breach(cooling), Breach(Guid.NewGuid()) };
        var config = new DiscordConfig
        {
            MetricBreachDestinations = [AlertDestination.Local, AlertDestination.Mine, AlertDestination.Phone],
            MineWebhookUrl = "https://discord.com/api/webhooks/1/mine",
        };
        var lastSent = new Dictionary<(Guid, AlertKind), DateTimeOffset>
        {
            [(cooling, AlertKind.MetricBreach)] = DateTimeOffset.UnixEpoch,
        };

        var routed = AlertRouter.Route(batch, config, lastSent, DateTimeOffset.UnixEpoch.AddMinutes(1),
            phoneConfigured: true);

        Assert.Equal(
            new[] { AlertDestination.Local, AlertDestination.Mine, AlertDestination.Phone },
            routed.Select(r => r.Destination).ToArray());
        Assert.All(routed, r => Assert.Equal(2, r.Triggers.Count));
        Assert.All(routed, r => Assert.DoesNotContain(r.Triggers, t => t.AccountId == cooling));
    }
```

In `AlertDispatcherTests.cs` add `using ROROROblox.Core.Metrics;` and append:

```csharp
    [Fact]
    public async Task DispatchAsync_AGroupedMetricBatch_IsOneToastAndOnePost_NamingTheCount()
    {
        var rule = new MetricRule("battle.points", MetricRuleKind.Level, 1000, TimeSpan.Zero, Label: "Points");
        AlertTrigger Breach(string name) =>
            new(AlertKind.MetricBreach, Guid.NewGuid(), name, $"real_{name}", rule.MetricId, null,
                DateTimeOffset.UtcNow, 250, rule);

        var (sender, handler) = Sender(HttpStatusCode.NoContent);
        var tray = new SpyTrayService();
        var config = new DiscordConfig
        {
            MetricBreachDestinations = [AlertDestination.Local, AlertDestination.Mine],
            MineWebhookUrl = MineUrl,
        };

        await Build(sender, tray, config).DispatchAsync([Breach("A"), Breach("B"), Breach("C")]);

        Assert.StartsWith("3 accounts — Points fell below 1,000|", Assert.Single(tray.Toasts), StringComparison.Ordinal);
        // The POST body is JSON, which escapes the em dash, so assert on the ASCII runs either side.
        var body = Assert.Single(handler.Bodies);
        Assert.Contains("3 accounts", body, StringComparison.Ordinal);
        Assert.Contains("Points fell below 1,000", body, StringComparison.Ordinal);
    }
```

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj -c Release --filter "FullyQualifiedName~MetricRoutingTests|FullyQualifiedName~AlertDispatcherTests"`
Expected: PASS.

- [ ] **Step 10: Correct the comments this made stale**

`App.xaml.cs` (in `WireAlertsAsync`), replace

```csharp
            // above and one more: this event is raised on a gRPC handler thread, which must not
            // be parked on an HTTP POST while a plugin waits for its Empty.
```

with

```csharp
            // above and one more: this event is raised on a thread-pool timer thread when a metric
            // grouping window closes (corrected 2026-09-15; through 1.28 it was the gRPC handler
            // thread), and a timer callback must not be parked on an HTTP POST either.
```

`AlertDispatcher.cs`, replace `a gRPC handler thread serving a plugin's report` with `a thread-pool timer thread flushing a plugin's grouped reports (a gRPC handler thread until 2026-09-15)`; and replace

```csharp
    /// model, raising on the UI thread, and the metric sink, raising on whatever gRPC handler
    /// thread served a plugin's report (both wired in <c>App.xaml.cs</c>). A plain Dictionary was
```

with

```csharp
    /// model, raising on the UI thread, and the metric sink, raising on a thread-pool timer thread
    /// when a grouping window closes (a gRPC handler thread until 2026-09-15; both wired in
    /// <c>App.xaml.cs</c>). A plain Dictionary was
```

`TrayService.cs`, replace

```csharp
    /// <c>MetricReportSinkAdapter.AlertsRaised</c>, which is raised on whatever gRPC handler thread
    /// served a plugin's report. <c>_taskbarIcon</c> is a WPF <c>FrameworkElement</c>, so touching it
```

with

```csharp
    /// <c>MetricReportSinkAdapter.AlertsRaised</c>, which is raised on a thread-pool timer thread when a
    /// metric grouping window closes (a gRPC handler thread until 2026-09-15). <c>_taskbarIcon</c> is a
    /// WPF <c>FrameworkElement</c>, so touching it
```

- [ ] **Step 11: Build the solution and run the metric, alert and notify suites**

Run: `dotnet build ROROROblox.slnx -c Release` then `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ROROROblox.Tests.Metrics|FullyQualifiedName~ROROROblox.Tests.Discord|FullyQualifiedName~ROROROblox.Tests.Notify|FullyQualifiedName~MetricReportWiringTests|FullyQualifiedName~TypedHttpClientRegistrationTests"`
Expected: 0 errors; PASS.

- [ ] **Step 12: Commit**

```powershell
git add src/ROROROblox.Core/Metrics/MetricBreachBatcher.cs src/ROROROblox.App/Plugins/Adapters/MetricReportSinkAdapter.cs src/ROROROblox.App/App.xaml.cs src/ROROROblox.App/Discord/AlertDispatcher.cs src/ROROROblox.App/Tray/TrayService.cs src/ROROROblox.Tests/Metrics/MetricBreachBatcherTests.cs src/ROROROblox.Tests/Metrics/MetricReportSinkAdapterTests.cs src/ROROROblox.Tests/Metrics/MetricRoutingTests.cs src/ROROROblox.Tests/Discord/AlertDispatcherTests.cs
git commit -m "feat(metrics): one plugin read is one alert per destination" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: The smoke harness reads the new line and covers the grouping

**Files:**
- Modify: `tools/MetricSmoke/Scenarios.cs` (`SmokeMetrics`, `SmokeTimings.AlertWindow` doc, `SmokeRules`, `ScenarioTable` doc + `All`, `ValueRendersLegiblyAsync`, new `SeveralAccountsOneAlertAsync`)
- Modify: `tools/MetricSmoke/README.md:3-4,18,93`
- Modify: `docs/superpowers/smoke-metric-alerts.md:16,72,97,122-133` and a new row after line 204
- Test: `src/ROROROblox.Tests/SmokeHarness/ScenarioTableTests.cs`

**Interfaces:**
- Consumes: `MetricBreachBatcher.Window` (Task 3); line shape `• {account} — now {value}` and title `{n} accounts — {metric id} fell below 0.8` for label-less Level rules (Task 2); `DeliveredAlert.AccountCount` (existing `LogTail`).
- Produces: `SmokeMetrics.Group = "smoke.group"`; scenario `several-accounts-one-alert` naming smoke row `Breaches from several accounts in one read become one alert.`

- [ ] **Step 1: Write the failing fence changes**

In `ScenarioTableTests.cs`:
- Change `private const int RowsOnTheSmokeList = 21;` to `22`.
- In `AllMetricIds()`, change `SmokeMetrics.Streamer, SmokeMetrics.Fallback,` to `SmokeMetrics.Streamer, SmokeMetrics.Fallback, SmokeMetrics.Group,`.
- In `TheMarkedRowCountMatchesTheRowsTheTableCovers`, change the comment's `sixteen` / `fourteen` / `sixteenth and seventeenth` to `seventeen` / `fifteen` / `seventeenth and eighteenth`.
- In `TheObservedValueIsReadInWhateverCultureTheAppRenderedIt`, append to its comment: `// Since 1.29 the payload formats invariant; the tolerance stays for a 1.28 build under test.`
- Append:

```csharp
    [Fact]
    public void TheGroupingWindowLeavesTheAlertWindowRoomToDeliver()
    {
        // Every positive row now waits out MetricBreachBatcher.Window before its first line can exist.
        // If the grouping window grew toward the harness's alert window, positive rows would start
        // failing for a delay that is by design — keep it to half at most.
        Assert.True(MetricBreachBatcher.Window * 2 <= SmokeTimings.AlertWindow,
            $"The grouping window ({MetricBreachBatcher.Window}) is more than half the harness's alert window "
            + $"({SmokeTimings.AlertWindow}).");
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj -c Release --filter "FullyQualifiedName~ScenarioTableTests"`
Expected: build FAILS on `SmokeMetrics.Group`.

- [ ] **Step 3: Implement the harness changes**

`Scenarios.cs`:

In `SmokeMetrics` add `public const string Group = "smoke.group";` after `Fallback`.

In `SmokeTimings.AlertWindow`'s "Why not the whole cooldown" paragraph, after `The physical latency is a localhost POST.` insert: ` (Corrected 2026-09-15: one thing on the path does wait now — <c>MetricBreachBatcher</c> holds a breach for its grouping window, five seconds, so one read's accounts become one alert. <c>ScenarioTableTests</c> keeps that window within half of this one.)`

In `SmokeRules`, add to the class summary a paragraph: `/// <para>No row carries a <c>label</c>, deliberately: <see cref="LogWatch.DeliveredFor"/> attributes an alert by finding the metric id in its title, and a label replaces the id there.</para>`. In `Canonical`, add `Level(SmokeMetrics.Group),` after `Level(SmokeMetrics.Fallback),`.

In the `ScenarioTable` summary change `The sixteen scenarios, in run order.` to `The seventeen scenarios, in run order.` and `<b>Sixteen scenarios, fourteen rows.</b>` to `<b>Seventeen scenarios, fifteen rows.</b>`.

In `All`, after the `repeated-breaches-one-toast` entry insert:

```csharp
        new("several-accounts-one-alert",
            "Breaches from several accounts in one read become one alert.",
            SeveralAccountsOneAlertAsync),
```

In `ValueRendersLegiblyAsync`, replace everything from `var rendered = Regex.Match(` to the end of the method with:

```csharp
        // Since 1.29 the line reads "• <account> — now <value>" and the metric id is in the title only
        // (the harness's rules carry no label, so the title still names it). The em dash arrives
        // JSON-escaped in the raw body, so the anchor is the word "now"; the value must start with a
        // digit, so a name that merely contains "now" cannot match.
        var rendered = Regex.Match(body, @"\bnow (?<v>-?[0-9][^\\""]*)");
        if (!rendered.Success)
        {
            return ScenarioOutcome.Fail(
                $"the body naming {SmokeMetrics.Value} carries no 'now <value>' reading at all.");
        }

        // Parsed, not string-compared against "0.79": a 1.28 build formats under the running app's
        // culture ("0,79" in several shipped languages), and 1.29 formats invariant. The bug this row
        // is about (the value riding a long? and rendering 0) still fails: 0 does not parse to 0.79.
        var token = rendered.Groups["v"].Value.Trim();
        if (!TryParseObserved(token, out var value))
        {
            return ScenarioOutcome.Fail($"the body reads 'now {token}', which is not a number in any culture.");
        }

        return Math.Abs(value - 0.79) < 0.0001
            ? ScenarioOutcome.Pass($"the body reads 'now {token}' — the fraction survived")
            : ScenarioOutcome.Fail($"the body reads 'now {token}', which is not 0.79.");
    }
```

After `RepeatedBreachesAsync`, add:

```csharp
    /// <summary>
    /// Three accounts breaching one rule back to back — the shape of one plugin read — reach the
    /// desktop as ONE alert covering three accounts. On 2026-09-15 eight accounts in one read became
    /// twenty-four notifications, one per account per destination.
    /// <para>
    /// The whole window, no early exit: "exactly one" is half a negative, and the grouping window delays
    /// the line by design. Fresh subjects, so no account is inside another row's cooldown.
    /// </para>
    /// </summary>
    private static async Task<ScenarioOutcome> SeveralAccountsOneAlertAsync(ScenarioContext ctx)
    {
        var watch = ctx.WatchLog();

        for (var i = 0; i < 3; i++)
        {
            await ctx.ReportAsync(ctx.FreshSubject(), SmokeMetrics.Group, SmokeRules.BreachingLevel)
                .ConfigureAwait(false);
        }

        await watch.SettleAsync(SmokeTimings.AlertWindow).ConfigureAwait(false);

        var locals = watch.DeliveredFor(SmokeMetrics.Group, LocalDestination);
        return locals.Count switch
        {
            1 when locals[0].AccountCount == 3 =>
                ScenarioOutcome.Pass("three breaches in one read, one 'Alert → Local' covering 3 accounts"),
            1 => ScenarioOutcome.Fail(
                $"one 'Alert → Local' for {SmokeMetrics.Group}, but it covers {locals[0].AccountCount} "
                + "account(s), not 3."),
            0 => ScenarioOutcome.Fail(
                $"no 'Alert → Local' for {SmokeMetrics.Group} in {Describe(SmokeTimings.AlertWindow)}."),
            _ => ScenarioOutcome.Fail(
                $"{locals.Count} 'Alert → Local' lines for {SmokeMetrics.Group}; three breaches in one read "
                + "should be one alert."),
        };
    }
```

`tools/MetricSmoke/README.md`: line 3 `Drives 14 of the 21 rows` → `Drives 15 of the 22 rows`; line 4 `— 16 scenarios,` → `— 17 scenarios,`; line 17-18 after `repeated breaches cost one toast, not several;` insert ` several accounts in one read cost one alert;` and change `Every one of those sixteen scenarios has been deliberately broken and watched turn red` to `Sixteen of those seventeen scenarios have been deliberately broken and watched turn red (the one-read grouping scenario, added 2026-09-15, has not yet)`; line 93 `Runs the sixteen scenarios` → `Runs the seventeen scenarios`.

`docs/superpowers/smoke-metric-alerts.md`:
- Line 16: `14 rows carry it today — 16 scenarios,` → `15 rows carry it today — 17 scenarios,`.
- Line 72: `**The sixteen `[harness]` rows: one command.**` → `**The fifteen `[harness]` rows (seventeen scenarios): one command.**`.
- Line 97: `Runs the sixteen scenarios` → `Runs the seventeen scenarios`.
- The rules example (lines 122-127): change the first row to `{ "metricId": "smoke.points", "kind": "Rate",  "threshold": 100, "windowMinutes": 10, "label": "Points" },` and append to the paragraph ending `unused by `Level` and `Event`.` the sentence: `` `label` is optional (1.29+): the name an alert uses in place of the metric id, trimmed, one line, at most 40 characters. `` Leave the dated 2026-09-11 and 2026-09-12 paragraphs as written; they are records.
- Insert this row directly after the `Repeated breaches do not become repeated toasts.` row (after its line ending `nothing else on this list checks it end to end.`):

```markdown
- [ ] **Breaches from several accounts in one read become one alert.** `[harness]` Report a breaching
      value for three accounts back to back against one `Level` rule. Exactly one `Alert → Local`
      line, covering 3 accounts, not three lines. The alert waits out the grouping window
      (`MetricBreachBatcher.Window`, five seconds) before it leaves, so nothing should be read into a
      short pause. The 2026-09-15 live run is why this row exists: eight accounts in one Ur Score read
      became 24 notifications. By eye, once, on a real Discord channel with a labelled rule: the post
      reads `3 accounts — <label> fell below <threshold>` with one `• <account> — now <value>` line
      per account, and a number of 1,000 or more carries its commas.
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj -c Release --filter "FullyQualifiedName~ROROROblox.Tests.SmokeHarness"`
Expected: PASS (row count 22, the new row marked and covered, every metric id has a rule, the grouping-window fence holds).

- [ ] **Step 5: Commit**

```powershell
git add tools/MetricSmoke/Scenarios.cs tools/MetricSmoke/README.md docs/superpowers/smoke-metric-alerts.md src/ROROROblox.Tests/SmokeHarness/ScenarioTableTests.cs
git commit -m "test(smoke): read the new metric line and prove one read is one alert" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 6 (owner-launched, not blocking the PR): run the harness live**

With RoRoRo not running, from the repo root: `dotnet run --project tools/MetricSmoke`, then start a Release dev build of this branch when it prints `START RORORO NOW` (`dotnet run --project src/ROROROblox.App -c Release`). Expected: seventeen scenarios, none failed (the streamer and fallback rows may skip on this profile, as before). Record the date and result on the new smoke row; tick it only after the by-eye Discord check.

---

### Task 5: Registry, decision log, full verification, PR

**Files:**
- Modify: `docs/features.md:144,188`
- Modify: `docs/decisions.md` (append after the 2026-09-12 entry)

**Interfaces:**
- Consumes: everything above.
- Produces: the PR for `feat/metric-alert-wording`.

- [ ] **Step 1: Update `docs/features.md`**

Metric alerts row (line 144):
- After `raises a `MetricBreach` through the same router (mute, cooldown, coalescing, streamer masking) every other alert kind uses.` insert: ` Since 1.29 a rule row may carry a `label`, the alert is worded from the rule that fired ("Points stopped climbing", "Diamonds went above 0", numbers of 1,000 and up with separators, the metric id when there is no label), and breaches of the same metric and rule within `MetricBreachBatcher.Window` (5 s, fixed from the first) are one alert per destination listing every account.`
- After `` `Core/Metrics/MetricEvaluator.cs`, `` insert `` `Core/Metrics/MetricBreachBatcher.cs`, `Core/Discord/WebhookPayload.cs`, ``.
- Replace `14 of the 21 rows on the manual smoke list (16 scenarios — two rows carry two cases each) are now driven by `tools/MetricSmoke` and were proven able to go red against a live build (2026-09-11, `docs/superpowers/smoke-harness-verification.md`);` with `15 of the 22 rows on the manual smoke list (17 scenarios — two rows carry two cases each) are now driven by `tools/MetricSmoke`; sixteen of those scenarios were proven able to go red against a live build (2026-09-11, `docs/superpowers/smoke-harness-verification.md`) and the one-read grouping scenario (2026-09-15) has not yet been;`

Metric-alert smoke harness row (line 188):
- `drives 14 of the 21 rows` → `drives 15 of the 22 rows`; `— 16 scenarios (two rows carry two cases each): the plugin pipe, both consent paths, all four alert legs, live rules pickup, streamer masking, the opt-in gate —` → `— 17 scenarios (two rows carry two cases each): the plugin pipe, both consent paths, all four alert legs, live rules pickup, one-read grouping, streamer masking, the opt-in gate —`.
- `Every one of the 16 scenarios has been deliberately broken and watched go red (2026-09-11, `docs/superpowers/smoke-harness-verification.md`).` → `Sixteen of the 17 scenarios have been deliberately broken and watched go red (2026-09-11, `docs/superpowers/smoke-harness-verification.md`); the grouping scenario (2026-09-15) has not yet.`

- [ ] **Step 2: Append the decision to `docs/decisions.md`**

```markdown
### 2026-09-15 — Metric alerts are worded from the rule that fired, and one plugin read is one alert
**Context:** The first live run with a real reporter (2026-09-15, RoRoRo 1.28 Store, a temporary Level rule "Diamonds above 0" on `ps99.diamonds`) sent 24 notifications for one Ur Score read: 8 accounts x Local, Mine and Phone, each titled `CElCPapa — ps99.diamonds` over the line `• CElCPapa — ps99.diamonds at 2974993`. Two causes. `AlertTrigger` never carried which rule fired, so `WebhookPayload` could print only the metric id and a bare number. And a plugin reports one `ReportMetric` per account while `AlertRouter` groups only the triggers that arrive in one call, so every account became its own alert. Spec `docs/superpowers/specs/2026-09-15-metric-alert-wording-design.md`, approved by Este the same day.
**Consequences:** A rule row takes an optional `label` (normalised to one trimmed line of at most 40 characters). The trigger carries the `MetricRule` itself (`AlertTrigger.Rule`, trailing and optional like `MetricValue`), and `WebhookPayload` words the alert from it: Rate "stopped climbing" with the measured rate, window and floor; Level "fell below" or "went above" the threshold with the current value; Event "changed". Numbers use invariant-culture thousands separators, because the sentence around them is English. A new Core `MetricBreachBatcher`, owned by `MetricReportSinkAdapter`, holds breaches for 5 seconds from the first and raises one group per (metric id, rule) as one `AlertsRaised`. The router, dispatcher and App wiring are untouched, so one group becomes one alert per destination, and mute, the per-(account, kind) cooldown and desktop fallback apply exactly as before. The four other alert kinds never pass through the batcher. Pending groups are dropped, not flushed, on exit. Pushover titles are now capped at 250 characters, because a rule without a label puts an unbounded plugin-supplied id in the title and an over-limit 400 would latch the phone off for the session. The honest costs, accepted: every metric alert lands up to 5 seconds later; a reporter that spreads one read over more than 5 seconds gets two alerts for it; and an account that breaches two different stats inside one window alerts only for whichever group closes first, because the cooldown is still per (account, kind).
**Evidence:** plan `docs/superpowers/plans/2026-09-15-metric-alert-wording.md`; `Core/Metrics/MetricBreachBatcher.cs` and `Tests/Metrics/MetricBreachBatcherTests.cs`; `Tests/Discord/WebhookPayloadTests.cs` (`TheLiveTestCase_ReadsAsASentenceWithSeparators`); `Tests/Metrics/MetricReportSinkAdapterTests.cs` (`EightAccountsInOneRead_AreOneAlertOfEight`); smoke row "Breaches from several accounts in one read become one alert." (`several-accounts-one-alert`); the PR that merges `feat/metric-alert-wording`.
```

- [ ] **Step 3 (controller, not the implementer): log the same decision to the 626 Labs dashboard**

`mcp__626labs-cloud__manage_decisions` with action `log` against the RoRoRo project, title and body from Step 2, `filesChanged` and `nextSteps` passed as real arguments; then verify with a `search` that it landed (hand-written parameter tags get swallowed).

- [ ] **Step 4: Full verification**

```powershell
dotnet build ROROROblox.slnx -c Release
dotnet test  ROROROblox.slnx -c Release --no-build
```

Expected: 0 errors; unit count 2,298 (baseline 2,258 before Task 1), all passing, none newly skipped; harness count unchanged from baseline at 27 passed, 1 skip by design. **Recounted 2026-09-15 (Task 5):** the actual delta is +40, not the +30 estimated here before execution — Task 1 +5, Task 2 +19, Task 3 +15, Task 4 +1 (per-task counts from the ledger: `progress.md`). The estimate predated the C1-C3 carry-ins, whose own tests (the router/dispatcher cooldown cases for C1, the three-envelope and trailer cases for C2, the `allowed_mentions` case for C3) landed inside Tasks 2 and 3's counts rather than as a separate line item. Warnings: nothing new beyond the known 51-warning baseline; no CS9124 on `log` in `MetricReportSinkAdapter` appeared in any task (confirmed in each task's report).

Then `git diff main --name-only` names no file on the never-commit list. The pre-commit hooks (installed by `.claude/hooks/install.ps1`) and the CI `guards` job are the secret and local-path check; do not hand-roll one.

- [ ] **Step 5: Commit and open the PR**

```powershell
git add docs/features.md docs/decisions.md docs/superpowers/plans/2026-09-15-metric-alert-wording.md
git commit -m "docs: metric alerts worded from the rule, one read one alert" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
git push -u origin feat/metric-alert-wording
gh pr create --base main --head feat/metric-alert-wording --title "Metric alerts you can read, one per read" --body-file <scratchpad>/pr-body.md
```

The PR body names the spec, the plan, the baseline and final test counts, the rulings below in one line each, and ends with `🤖 Generated with [Claude Code](https://claude.com/claude-code)`. Write it to a scratchpad file first; never inline a body with backslashes.

- [ ] **Step 6: Wait for `ci.yml` green on the PR (x64, native arm64, `guards`). Merge only on the owner's OK.**

---

### Task 6: Release v1.29.0.0 (every step gated on the owner's OK)

Runbook: `docs/store/release-playbook.md`. Everything before the Partner Center submit click is the agent's; the submit click and the Discord post are Este's.

- [ ] **Step 1 (after merge): `ci.yml` green on `main`.** `gh run list --repo estevanhernandez-stack-ed/ROROROblox --branch main --workflow ci.yml --limit 1` shows success. `release.yml` does not run `guards`, so this gate is the only one.
- [ ] **Step 2 — Phase 1, version.** `1.29.0.0` (MINOR: user-visible feature; fourth component `0`).
- [ ] **Step 3 — Phase 2, notes and listing audit.** Write `docs/store/release-notes-1.29.0.0.md` (fenced `•` short list; "What changed": alerts say what fired, one alert per read; Compatibility: none, `label` optional, old rules files work), `docs/store/whats-new-1.29.0.0.md`, and `docs/store/reviewer-letter-1.29.0.0.md` (no network or manifest delta). Audit all three listing surfaces in `docs/store/listing-copy.md` (short description, long description, product features) plus the hub page `docs/index.md` against the notes; record "listing audited, unchanged" or the edits in `docs/store/submission-packet-1.29.0.0.md`.
- [ ] **Step 4 — Phase 3, Store MSIX x64 and arm64** (patches the csproj `<Version>` and `Package.appxmanifest`):

```powershell
powershell -ExecutionPolicy Bypass -File scripts/finalize-store-build.ps1 -Version 1.29.0.0 -IdentityName 626LabsLLC.RoRoRoBlox -PublisherCN "CN=177BCE59-0966-4975-9962-10E36652141F" -PublisherDisplayName "626Labs LLC"
powershell -ExecutionPolicy Bypass -File scripts/finalize-store-build.ps1 -Version 1.29.0.0 -IdentityName 626LabsLLC.RoRoRoBlox -PublisherCN "CN=177BCE59-0966-4975-9962-10E36652141F" -PublisherDisplayName "626Labs LLC" -Architecture arm64
```

Expected: `dist/RORORO-Store-x64-1.29.0.0.msix` and `dist/RORORO-Store-arm64-1.29.0.0.msix`.
- [ ] **Step 5 — Phase 4, sideload MSIX:** `scripts/build-msix.ps1 -Sideload -CertPath dev-cert.pfx -CertPassword $certPwd` (password from the `ROROROBLOX_DEV_CERT_PASSWORD` user variable); `Get-AuthenticodeSignature` shows a signer.
- [ ] **Step 6 — Phase 5, commit and tag, on the owner's OK:**

```powershell
git add src/ROROROblox.App/ROROROblox.App.csproj src/ROROROblox.App/Package.appxmanifest docs/store/release-notes-1.29.0.0.md docs/store/whats-new-1.29.0.0.md docs/store/reviewer-letter-1.29.0.0.md docs/store/submission-packet-1.29.0.0.md
git commit -m "chore(release): bump version to 1.29.0.0" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
git tag v1.29.0.0
git push origin main
git push origin v1.29.0.0
```

- [ ] **Step 7 — `release.yml` drafts the release.** `gh run watch --repo estevanhernandez-stack-ed/ROROROblox`. Do not also run `vpk upload github` or `gh release create` for this tag. Confirm the draft carries the Velopack assets plus `roblox-compat.json`, `.sig` and `plugins-catalog.json`.
- [ ] **Step 8 — Phase 6, finalize the GitHub release on the owner's OK** (attach the sideload MSIX and `dev-cert.cer`, set the body from the notes below the edit-log banner, `--draft=false --latest`). Not a pre-release: a pre-release reaches nobody.
- [ ] **Step 9 — Phase 7, Store submission:** both MSIX packages, the reviewer letter pasted in Partner Center, "What's new" on all ten listing rows through the listing console, any listing edits from the packet. The submit click is Este's.
- [ ] **Step 10 — Phase 8 and ledger:** draft the clan Discord post (Este posts); add the `v1.29.0.0` entry to `docs/feature-ledger.md` in the session the tag lands.

---

## Rulings made while planning

Each one is a call the spec leaves open: what was decided, why, and what it costs if it is wrong.

1. **The breach carries the whole `MetricRule`** (`AlertTrigger.Rule`) plus a `Label` on the rule, rather than five new trigger fields. *Why:* the label, kind, threshold, window and direction arrive together from the rule that fired, with no second copy to drift, and the rule doubles as the grouping key. `GameName` keeps the metric id so the harness and rule-less triggers keep working. *Cost if wrong:* `AlertTrigger` (Core.Discord) now references a Core.Metrics type; if another kind ever wants rule data, the shape gets revisited.
2. **A label is normalised when the rules file is parsed:** control characters become spaces, runs collapse, ends trim, blank becomes null (so the metric id speaks), and anything over 40 characters is cut with "…". A non-string `label` makes its row unreadable, like any other type-mismatched field. *Why:* the label is hand-written or plugin-written text that lands in a phone title and a bold Discord line, and a newline would split them. *Cost if wrong:* a label over 40 characters is shortened.
3. **Numbers use invariant culture** (`#,0.##`), not the machine's. *Why:* the payload is English, and "2.974.993" from a German machine reads as a decimal inside "now ...". *Cost if wrong:* a localized player sees English separators, which matches the English words around them.
4. **"A threshold prints the way the rule wrote it" means no rounding**: up to ten decimals, with the same separators as other numbers (0.125 stays 0.125; 5000 prints 5,000). *Why:* `0.##` would turn 0.125 into 0.13, which misstates the rule. *Cost if wrong:* if the owner meant the literal JSON token, `RuleRow` can capture the raw text instead. That is a small change.
5. **The Rate line and negative or unmeasurable rates:** confirmed that neither is ever a breach. `MetricHistory.RatePerMinute` returns null for fewer than two in-window samples, a zero span, any decrease, or trimmed data, and `MetricEvaluator` returns no breach for null. So a Rate breach always carries a rate of 0 or more, under the floor. No negative wording exists. A Rate trigger with no value (unreachable) renders `• {account}` with no number.
6. **Rule-less triggers keep the 1.28 wording** (`{noun} — {metric id}` / `… at {v:0.##}`). *Why:* the two existing tests pin it, and a trigger built without a rule keeps behaving as it did. *Cost if wrong:* two lines of code production no longer reaches.
7. **Grouping window: 5 seconds, fixed from the first breach, not sliding.** *Why:* the live read spread over about 100 ms, so 5 s is a 50x margin. It adds at most 5 s to an alert whose cooldown is 5 minutes. A fixed window means a trickle can't postpone delivery. And it fits inside the harness's 30 s alert window, which a new fence enforces at half or less. *Cost if wrong:* a reporter that spreads one read over more than 5 s gets two alerts for that read. The cooldown still suppresses the next read.
8. **Grouping lives in Core, in `MetricBreachBatcher`, owned by `MetricReportSinkAdapter`,** between the coordinator and `AlertsRaised`. `AlertsRaised` forwards its add/remove to the batcher's `Flushed`. `AlertRouter`, `AlertDispatcher` and `App.WireAlertsAsync` are unchanged. *Why:* only the metric path goes through the sink, so "only metric breaches are grouped" holds by construction. The router's per-call behaviour is shared with four shipped kinds, the reason the coordinator already gives. The App wiring line stays byte-identical. *Cost if wrong:* the event sender is now the batcher, not the adapter. Nothing reads the sender today.
9. **Each destination gets exactly one grouped alert** because each closed group is exactly one `AlertsRaised` → one `DispatchAsync` call. The router already groups a call by kind and emits one `RoutedAlert` per resolved destination, carrying the whole group. Two groups never share a call, which would merge them under one rule's title. The cooldown filter runs before the payload is built, so an account in cooldown drops out and the "{n} accounts" count reflects who is left. Both halves are pinned (`MetricRoutingTests`, `AlertDispatcherTests`).
10. **Mechanism:** `TimeProvider.CreateTimer` (so `FakeTimeProvider` drives the tests with no sleeps) and one lock over the pending map. Each group's timer is created under the lock and its callback takes the lock first, so it cannot flush before its group is registered. The event is raised outside the lock, and every exception is caught, since one escaping a timer callback would end the process. Concurrency is proven with 16 parallel `Add`s.
11. **The same account twice in one window is listed once, with its latest reading, in its first position.** *Why:* listing one alt twice at two values is the clan-channel defect the coordinator's one-trigger-per-call rule exists to stop. *Cost if wrong:* the earlier reading is not shown.
12. **On exit, pending groups are dropped, not flushed,** and the count is logged at Information. *Why:* `OnExit` stops the plugin host first. The container then disposes the sink alongside the tray and HTTP clients a flush would reach. The player is at the PC, quitting, and a condition that still holds breaches again on the next read in the next session (the cooldown map lives only in memory). *Cost if wrong:* a breach in the last 5 seconds before quitting is not sent.
13. **An account breaching two different stats in one window** gets separate groups, as the spec requires, but the unchanged per-(account, kind) cooldown is stamped by whichever group closes first. The account then drops out of the second group. *Why:* the spec keeps the cooldown as it is. *Cost if wrong:* that account's second stat waits up to 5 minutes. Fixing it would mean keying the cooldown per metric for this kind, which is a spec change.
14. **Phone title limits:** a grouped title uses the account count, so its length does not grow with accounts. It is bounded by the 40-character label, except that a label-less rule puts the plugin-supplied metric id there, which is unbounded. So `PushoverSender` caps titles at 250 characters, because an over-limit 400 would latch `EndpointRejected` for the session. ntfy's title header is already the static "RoRoRo", and the payload title leads its body, so nothing changes there. Pushover's existing 1,024-character body cap already covers many lines.
15. **Discord's 2,000-character message limit is not capped here.** A grouped Rate line is about 65 characters, so one post fits about 30 accounts. *Cost if wrong:* a group of more than 30 accounts gets a 400 from Discord. It is logged at Debug as `Failed` (not terminal), and that alert is lost on Mine and Clan while the toast and phone still arrive. A `TruncateForDiscord` like Pushover's would be the follow-up.
16. **The smoke harness keeps label-less rules** so `DeliveredFor` can still attribute alerts by the metric id in the title. The value row now reads `now <v>`, and a new `[harness]` row plus scenario covers grouping, taking the list to 22 rows, 15 marked and 17 scenarios. The new scenario is not yet proven red, and the docs say so.
17. **Version 1.29.0.0** (MINOR: a user-visible change to every metric alert).

## Noticed, not in scope

- **Superseded by C3 — shipped.** Discord parses mentions in webhook `content` unless `allowed_mentions` restricts it. A metric id (already, since 1.28) or a label containing `@everyone` could ping the clan channel. This was flagged here, before execution, as a likely one-line follow-up because it changes all five alert kinds rather than only this plan's scope. The controller pulled it into this plan as **controller ruling C3** before dispatch, and Task 2 built it: `DiscordWebhookSender` now sends `allowed_mentions: { parse: [] }` on every webhook POST, for all five alert kinds, not only metric breaches (`477554e`).

## Self-review

- **Spec coverage:** optional `label` (Task 1); breach carries label, kind, threshold, window, direction and the observed value, with Rate as a per-minute rate (Task 1; `MetricValue` was already the rate); the wording table, thousands separators, `0.##`, threshold as written, fallback to the metric id, masking unchanged (Task 2); grouping by metric + rule within a short window into `{n} accounts — …` through the same router, destinations, cooldown with drop-out, mute and fallback, with different stats kept separate and only metric breaches grouped (Task 3, pins in Step 9); rules file, toggles and plugin contract untouched (Global Constraints; no task touches them); old rules files work (Task 1 test with no label); release steps and decision logging (Tasks 5-6).
- **Placeholder scan:** none. The PR body is written at execution time from named contents, and `<scratchpad>` is the executor's scratchpad path.
- **Type consistency:** `MetricRule.Label`, `AlertTrigger.Rule`, `MetricBreachBatcher.Window` / `Flushed` / `Add` / `Dispose`, `LocalFileMetricRuleSource.NormaliseLabel` / `LabelLimit`, `PushoverSender.TruncateTitleForPushover`, and `SmokeMetrics.Group` are spelled the same in every task and in the Interface contract.

## Controller rulings added before execution (2026-09-15)

These override the plan where they differ.

- **C1. A metric alert's cooldown is per account and metric id**, not per account and alert kind. Two different stats breaching for one account in the same read both alert. Other alert kinds keep per-(account, kind). *Why:* a Points stall and a Diamonds alert in the same read are two different things to know. *Cost if wrong:* slightly more alerts when several stats breach together.
- **C2. A grouped alert caps its account lines** so every destination stays under its length limit (Discord content or embed description, the Pushover title and message, the ntfy title). It then ends with "and N more". *Why:* a group of about 30 or more accounts would otherwise fail to post to Discord while the toast and phone still arrive. *Cost if wrong:* a very large group names only the first accounts.
- **C3. Every RoRoRo webhook post sends `allowed_mentions` with no parse targets**, so a label, metric id or account name containing `@everyone`, `@here` or a role mention can never ping a channel. It applies to all alert kinds, which is a one-line change in the payload builder, with a test. *Why:* labels come from a hand-edited file and metric ids from plugins, and the clan destination is a shared channel. *Cost if wrong:* none known; RoRoRo's posts never intended to mention anyone.

## Execution record (2026-09-15)

**How it was built.** Five tasks, each dispatched to a subagent (superpowers:subagent-driven-development) and reviewed by a separate subagent before the next task started. A read-only pre-flight scan ahead of Task 1 found 3 BLOCKER findings — all three were the controller's own C1-C3 rulings contradicting the plan's own text — and carried each into the task that had to build it (C1 into Task 3, C2 and C3 into Task 2), plus 9 smaller FIX findings folded into task briefs. Every task landed Approved or Approved-with-parked-Minors; Task 4's commit trailer was the one Important finding at task level, and the controller amended it directly. After Task 5, a whole-branch review (`8e28eba..ae33ec7`) found one Important issue (the desktop toast inherited the webhook-sized payload and the shell cut it silently) and 12 Minors; one fix wave (3 commits) resolved the Important issue and four of the Minors, and a scoped re-review of that wave confirmed all four fixed with no new Critical or Important issues, ending **ready to merge**. A final documentation-only commit (`c3e0683`) cleaned up the decision entry's evidence list per the re-review's own last Minor.

**Tasks and commits.**

- Task 1 — the breach carries the rule that fired, labels: `dc44992`. Reviewed, Approved.
- Task 2 — wording from the rule, with C2 (length caps, every alert kind) and C3 (`allowed_mentions`): `477554e`. Reviewed, Approved.
- Task 3 — one alert per read (`MetricBreachBatcher`), with C1 (per-metric cooldown): `b245ef8`. Reviewed, Approved (`DONE_WITH_CONCERNS` at hand-off; the concerns were notes, not blockers).
- Task 4 — the smoke harness reads the new line, proves one read is one alert: `ac459df`, amended by the controller to `9318c4b` (wrong commit-trailer name, an Important finding).
- Task 5 — features/decisions docs, spec banner, full verification: `ae33ec7`. Reviewed, Approved.
- Final fix wave (one dispatch): `649f1f5` (the toast's own 63/255 envelope), `a02fa24` (pending groups drop right after the plugin host stops, not at the very end of exit), `ab0b35e` (decisions.md and features.md wording). Re-reviewed, all four rulings **Fixed**, ready to merge.
- Post-re-review cleanup: `c3e0683` (decisions.md's evidence list named one test file twice and stopped its commit list early; fixed).

**Controller rulings made during execution** (beyond the three pre-flight C1-C3 carry-ins above):

- Accepted the shared 992-character remote body cap for v1.29 as-is (the owner's clan runs 8 accounts; per-destination limits are a later change) and recorded the trade-off in the decision entry, rather than building per-destination remote limits now.
- Ruled the plugin contract's proto-file edit comment-only (no field, number or RPC change), so "no proto change" still holds for release purposes; routed it and two other stale-comment fixes into Task 5.
- Ran Task 5's own review and the whole-branch review in parallel, folding Task 5's review findings into the one final fix wave instead of a separate round.
- Dispatched the final fix wave as one unit: give the desktop toast its own length envelope, drop pending groups right after the plugin host stops (not at the very end of exit), correct two decisions.md lines, and add the missing C2 clause to both `features.md` rows — every other Minor stayed parked.

**Verification.** 2,311 unit tests + 27 harness tests passing, 1 harness test skipped by design (`ConsentRevocation_CancelsActiveStream_WithinOneSecond`) — the count after the final fix wave, unchanged by the docs-only cleanup commit. Baseline before Task 1 was 2,258 unit tests; the plan's own pre-execution estimate of "+30" undercounted the C1-C3 tests, and the real total came to +40 through Task 4, then +13 more from the fix wave.

**What's still owed.** The plan's own Task 4 Step 6 — running the 17-scenario harness live against a real RoRoRo build — is owner-launched and was not run during this SDD cycle; the new grouping scenario has therefore not yet been proven to go red. Also owed: a by-eye look at a real Windows desktop toast for a group of four or more accounts, because no automated test can see how far Windows' own balloon-as-toast rendering clips beyond the 63/255 character envelope. The 626 Labs dashboard decision log (`mcp__626labs-cloud__manage_decisions log`) is a separate controller step from this file and has been handled outside this ledger.

**Known costs recorded in `docs/decisions.md`** (2026-09-15 entry): every metric alert lands up to 5 seconds later than before; a plugin read spread over more than 5 seconds produces two alerts instead of one; the shared 992-character remote body shows "and N more" earlier than Discord's or ntfy's own limits would force (at roughly 28 dropped-out accounts, or 16 Rate lines); the desktop toast names far fewer accounts still (about 3 Rate lines) and Windows' own rendering may clip further than that, unseen by any automated test; two groups for the same metric under different rules can both alert for one account if they close within milliseconds of each other, because the dispatcher stamps its cooldown after sending rather than before; and `WebhookCatcherTests.AcceptLoop_ABadRequestThatFailsMidRead_CostsOnlyThatOneRequest` failed once under full-suite load this cycle and passed 5 of 5 alone — a pre-existing load flake, not caused by this branch, left for the backlog.

### Nice-to-haves — every Minor found, kept durably

Every "Minor" any review raised, plus the pre-flight NOTE rows that pointed at a real, still-checkable gap (not a closed "accept as designed" note), de-duplicated across sources. Checked against the tree at `c3e0683` on 2026-09-15.

**Counts:** 38 OPEN, 8 FIXED, 2 GONE (48 total).

**By source — open / fixed / gone:** Task 1 review 1/0/1 · Task 2 review 8/0/0 · Task 3 review 6/2/0 · Task 4 review 2/0/0 · Task 5 review 0/2/1 · Final whole-branch review 9/3/0 · Final scoped re-review 5/1/0 · Pre-flight-only 7/0/0.

**Task 1 review**

- MA-1.1 **GONE** — two doc comments named the grouping class ("MetricBreachBatcher") a task before it existed — self-resolved once Task 3 landed a class with that exact name — `src/ROROROblox.Core/Discord/AlertTrigger.cs`, `src/ROROROblox.Core/Metrics/MetricAlertCoordinator.cs` — task-1-review.md
- MA-1.2 **OPEN** — the code path that avoids cutting an emoji in half when a label is shortened has no test exercising it — `src/ROROROblox.App/Metrics/LocalFileMetricRuleSource.cs:210` — task-1-review.md

**Task 2 review**

- MA-2.1 **OPEN** — one shared length limit for Discord and ntfy means a big group of accounts shows "and N more" sooner than either service actually requires; accepted for v1.29 (the owner's clan is 8 accounts) and written into the decision log as a known cost — `src/ROROROblox.Core/Discord/WebhookPayload.cs:34` — task-2-review.md
- MA-2.2 **OPEN** — the code comment explaining why the limit is 992 characters describes Pushover's own trimming a little inaccurately — `src/ROROROblox.Core/Discord/WebhookPayload.cs:25-27` — task-2-review.md
- MA-2.3 **OPEN** — the memory-warning and recycled-space lines still print numbers in whatever language the PC is set to, while the new metric lines always print in English-style numbers — pre-existing, not new — `src/ROROROblox.Core/Discord/WebhookPayload.cs` (MemoryWarning/Recycled lines) — task-2-review.md
- MA-2.4 **OPEN** — a Rate alert's time window (e.g. "10 min") rounds to two decimal places instead of printing exactly what the rule says, unlike the threshold next to it — `src/ROROROblox.Core/Discord/WebhookPayload.cs:134` — task-2-review.md
- MA-2.5 **OPEN** — the test that checks numbers print in English style on a German PC only checks the message body, not the title — `src/ROROROblox.Tests/Discord/WebhookPayloadTests.cs:183-198` — task-2-review.md
- MA-2.6 **OPEN** — the 200-account test only checks "at least one account shown," not "as many accounts shown as actually fit" — `src/ROROROblox.Tests/Discord/WebhookPayloadTests.cs:243` — task-2-review.md
- MA-2.7 **OPEN** — the test proving ntfy's message never gets too big uses only plain English names, never names with emoji or non-English characters, which take more space — `src/ROROROblox.Tests/Discord/WebhookPayloadTests.cs:227-228` — task-2-review.md
- MA-2.8 **OPEN** — the safeguards against cutting an emoji in half in the phone title and in the general cutting logic have no test proving they work — `src/ROROROblox.App/Notify/PushoverSender.cs`, `src/ROROROblox.Core/Discord/WebhookPayload.cs:219` — task-2-review.md

**Task 3 review**

- MA-3.1 **OPEN** — nothing proves that two different rules on the same stat share one "already alerted, wait" timer the way the design intends — `src/ROROROblox.Tests/Metrics/MetricRoutingTests.cs` — task-3-review.md
- MA-3.2 **OPEN** (accepted, written into the decision log) — two alerts for the same account and stat that finish within milliseconds of each other can both go out, because the "already alerted" flag is set after sending, not before — `src/ROROROblox.App/Discord/AlertDispatcher.cs:53-57` — task-3-review.md
- MA-3.3 **FIXED** (Task 4, `9318c4b`) — a code comment in the smoke-test tool still said alerts fire from a different kind of background thread than they actually do — `tools/MetricSmoke/Program.cs:419` — task-3-review.md
- MA-3.4 **FIXED** (Task 4, `9318c4b`) — a code comment referenced a smoke-test safeguard that didn't exist yet at the time; Task 4 built it — `src/ROROROblox.Core/Metrics/MetricBreachBatcher.cs:48-50` — task-3-review.md
- MA-3.5 **OPEN** — in the extremely unlikely case the timer system itself fails to start a timer, that one group of accounts would silently never get its alert for the rest of the session — `src/ROROROblox.Core/Metrics/MetricBreachBatcher.cs:79-81` — task-3-review.md
- MA-3.6 **OPEN** — no test deliberately runs "a new breach arrives" at the exact same instant as "a group's timer goes off," the trickiest timing case in this feature — `src/ROROROblox.Tests/Metrics/MetricBreachBatcherTests.cs:107` — task-3-review.md
- MA-3.7 **OPEN** — a group's whole alert-sending work runs "tagged" as if it belonged to whichever account happened to trigger the group first, which is cosmetic but slightly wrong bookkeeping — `src/ROROROblox.Core/Metrics/MetricBreachBatcher.cs:81` — task-3-review.md
- MA-3.8 **OPEN** — the plugin-facing "an alert was raised" event's description doesn't say the sender changed from the plugin adapter to the new grouping class (the grouping class's own event description does say so) — `src/ROROROblox.App/Plugins/Adapters/MetricReportSinkAdapter.cs` (`AlertsRaised`) — task-3-review.md

**Task 4 review**

- MA-4.1 **OPEN** — the smoke tool's number-reading helper has no path for numbers with commas (like "1,000"), a pre-existing gap the new test didn't need but a future one might trip over — `tools/MetricSmoke/Scenarios.cs` (`TryParseObserved`) — task-4-review.md
- MA-4.2 **OPEN** (cosmetic) — one corrected code comment now reads as one long, dense sentence — `tools/MetricSmoke/Program.cs:419-422` — task-4-review.md

**Task 5 review**

- MA-5.1 **FIXED** (final fix wave, `ab0b35e`) — the Metric-alerts feature-doc row's opening sentence, read alone, implied the cooldown works identically to every other alert; the exception is now stated in that same opening clause — `docs/features.md:144` — task-5-review.md
- MA-5.2 **FIXED** (final fix wave, `ab0b35e`) — the general Alerts feature-doc row didn't mention the new "cap the account list, then say how many more" behaviour that applies to every alert kind, not only metric alerts — `docs/features.md:114` — task-5-review.md
- MA-5.3 **GONE** — the implementer's own written report cited the wrong line numbers for a doc fix; the fix itself, checked directly, was always at the right lines — `docs/plugins/AUTHOR_GUIDE.md:439-442` — task-5-review.md

**Final whole-branch review**

- MA-FR.1 **FIXED** (final fix wave, `a02fa24`) — an in-progress group of alerts could still be sent out during app shutdown instead of being dropped, because the cleanup ran too late in the shutdown sequence — `src/ROROROblox.App/App.xaml.cs`, `src/ROROROblox.App/Plugins/Adapters/MetricReportSinkAdapter.cs` — final-review.md
- MA-FR.2 **OPEN** — showing a desktop toast still blocks a background timer thread on the main UI thread instead of a non-blocking hand-off; pre-existing, not introduced by this work — `src/ROROROblox.App/Tray/TrayService.cs:339` — final-review.md
- MA-FR.3 **OPEN** — whether an alert is allowed to send at all, and whether a streamer's real name is hidden, are decided when the report first comes in, up to 5 seconds before the alert actually goes out; turning either setting off inside that 5-second window doesn't stop that one alert — `src/ROROROblox.App/Plugins/Adapters/MetricReportSinkAdapter.cs` — final-review.md
- MA-FR.4 **OPEN** — label clean-up strips ordinary control characters but not some invisible Unicode formatting characters (line separators, right-to-left override characters), which could still visually break a title — `src/ROROROblox.App/Metrics/LocalFileMetricRuleSource.cs:203` — final-review.md
- MA-FR.5 **OPEN** — when a rule has no label, the raw metric id (which can come from a plugin) goes straight into the title with none of the same clean-up a label gets — `src/ROROROblox.Core/Discord/WebhookPayload.cs` (title-building) — final-review.md
- MA-FR.6 **OPEN** — a label or metric id could still use Discord formatting tricks (bold, links) to make an alert look different than intended in the shared clan channel; pinging is blocked, formatting tricks are not — `src/ROROROblox.App/Discord/DiscordWebhookSender.cs` — final-review.md
- MA-FR.7 **OPEN** — the new English alert phrases and the new toast-specific length limit aren't yet listed in the future translation-work backlog item that already exists for alert text — `docs/store/localization-plan.md:131` — final-review.md
- MA-FR.8 **FIXED** (final fix wave, `a02fa24`/`ab0b35e`) — the decision log linked to a working file that lives outside the repo and will be deleted — `docs/decisions.md:501` — final-review.md
- MA-FR.9 **FIXED** (final fix wave, `ab0b35e`) — the decision log said routing and dispatching were "untouched," which its own next sentence contradicted — `docs/decisions.md:500` — final-review.md
- MA-FR.10 **OPEN** — this plan file itself still has two old planning notes (numbers 13 and 15) and a decision-log template that describe the cooldown and Discord limit the OLD way, before the controller's rulings changed them, with nothing marking them as superseded — `docs/superpowers/plans/2026-09-15-metric-alert-wording.md` (rulings 13, 15; Task 5 Step 2 template) — final-review.md
- MA-FR.11 **OPEN** — a smoke-test row's own description still says "three breaches," but the grouping change means the test now only truly exercises the "already alerted, wait" behaviour once, not the twice the description claims — `tools/MetricSmoke/Scenarios.cs:894` — final-review.md
- MA-FR.12 **OPEN** — the new "several accounts, one alert" smoke-test row only checks the desktop notification, never checking that the personal and clan Discord webhooks also got exactly one post each — `tools/MetricSmoke/Scenarios.cs:921` — final-review.md

**Final scoped re-review**

- MA-RR.1 **OPEN** — no test proves the emoji-safety cutting logic still works correctly now that the desktop toast's title limit is so much shorter (63 characters instead of 250) — `src/ROROROblox.Tests/Discord/WebhookPayloadTests.cs` — final-rereview.md
- MA-RR.2 **OPEN** — when a title gets cut short, it can leave an awkward trailing dash or space right before the "…", e.g. "Name —… went above 0" — `src/ROROROblox.Core/Discord/WebhookPayload.cs:168-178` — final-rereview.md
- MA-RR.3 **OPEN** (cosmetic) — the general Alerts doc row still doesn't mention one rare title-cutting fallback, and the Metric-alerts doc row now mentions the same cooldown fact twice — `docs/features.md:114,144` — final-rereview.md
- MA-RR.4 **FIXED** (`c3e0683`) — the decision log's evidence list named one test file twice and its list of commits stopped partway through, missing the fix-wave commits — `docs/decisions.md:501` — final-rereview.md
- MA-RR.5 **OPEN** — the automated check that proves shutdown cleans up in the right order reads to the end of the file instead of stopping at the end of the specific method, and its final check has no explanation message if it ever fails — `src/ROROROblox.Tests/Metrics/MetricReportSinkAdapterTests.cs:292-306` — final-rereview.md
- MA-RR.6 **OPEN** — the smoke-test tool's own notes don't mention that the desktop toast's title is now capped at 63 characters, which could someday cause a confusing false alarm from the test tool — `tools/MetricSmoke/Scenarios.cs:149-150` — final-rereview.md

**Pre-flight-only findings** (real gaps the pre-flight scan raised that no later review picked back up)

- MA-PF.1 **OPEN** — a metric rule can fire on a broken (NaN/Infinity) reading, and nothing rejects a broken number before it reaches an alert, which would print "now NaN" — `src/ROROROblox.Core/Metrics/MetricEvaluator.cs`, `src/ROROROblox.App/Plugins/Adapters/MetricReportSinkAdapter.cs` — preflight.md 3.4/5.10
- MA-PF.2 **OPEN** — no test proves that a rule with a badly-typed label (e.g. a number instead of text) is dropped cleanly, the way a badly-typed threshold already is — `src/ROROROblox.Tests/Metrics/LocalFileMetricRuleSourceTests.cs` — preflight.md 3.26
- MA-PF.3 **OPEN** — editing a rule's label while a group of alerts is still gathering can split that group into two smaller alerts instead of one; flagged for a one-line mention in the decision log, never added — `src/ROROROblox.App/Metrics/LocalFileMetricRuleSource.cs`, `docs/decisions.md` — preflight.md 3.27
- MA-PF.4 **OPEN** — the smoke-test tool doesn't recognise the grouping class's own "dropped on exit" log line as a failure, so that specific failure mode is invisible to the test tool — `tools/MetricSmoke/LogTail.cs` — preflight.md 3.38/5.9
- MA-PF.5 **OPEN** — the tests proving the new per-metric cooldown work were never deliberately run against the OLD cooldown code first to confirm they would have caught the bug; they passed the first time they were run — preflight.md 5.2
- MA-PF.6 **OPEN** (optional) — no test proves the toast's account count updates correctly ("2 accounts") after someone in a group of three drops out from being on cooldown; only the underlying routing decision is tested, not the toast wording — preflight.md 6.5
- MA-PF.7 **OPEN** (optional) — no automated check proves the four older alert kinds still skip the new grouping step entirely, the way the design intends "by construction" — preflight.md 6.6
