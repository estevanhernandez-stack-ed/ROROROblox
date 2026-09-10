# External Metric Alerts — Plan 1 of 3: Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A metric observation from any source becomes a properly muted, cooled-down, coalesced alert on the destinations the user already configured.

**Architecture:** Core gains a fifth `AlertKind`, a small per-subject sample history, and three evaluation rules (rate / level / event). A coordinator turns observations into `AlertTrigger`s and hands them to the **existing** `AlertRouter`, so metric alerts inherit mute, per-(account, kind) cooldown, coalescing and fallback-to-Local for free. Nothing in this plan performs I/O, knows an endpoint, or names a vendor — the observation arrives from a caller, which in plan 1 is a test.

**Tech Stack:** .NET 10, C# 14, xUnit, `Microsoft.Extensions.TimeProvider.Testing`.

**Spec:** [`docs/superpowers/specs/2026-09-09-external-metric-alerts-design.md`](../specs/2026-09-09-external-metric-alerts-design.md)

**Plans 2 and 3 (not this document):** the plugin RPC that feeds observations in, and the signed manifest feed plus a reference plugin. This plan deliberately stops at the seam so it can ship and be smoke-tested alone.

## Global Constraints

- **Thresholding lives in core, never in a caller.** The one guarantee this feature must not break: *a bad night does not become forty notifications.* Mute, cooldown and coalescing are the router's, and metric alerts must go through it. (Spec §1.2)
- **The binary ships no endpoint, no field path, and no vendor name.** Nothing in this plan may reference Big Games, `biggamesapi.io`, Pet Simulator, or any URL. (Spec §1.6)
- **New setting = a default parameter on the private `SettingsBlob` record at the bottom of `Core/AppSettings.cs`, plus an `IAppSettings` member.** Four test files carry private fakes and stop compiling until updated: `MainViewModelTests`, `RobloxLauncherTests`, `StreamerIdentityProviderTests`, `Discord/DiscordTestHarness`. `SettingsReachabilityTests` requires the key be reachable from a control or allow-listed. (CLAUDE.md)
- **Core returns keys and data, never prose.** `CoreStringBoundaryFenceTests` bans `string Message` members in Core. Viewer-facing sentences live in `App/Localization/CoreMessageCatalog.cs` behind a `CoreMsg_*` resx key. (CLAUDE.md; spec §0.7)
- **New resx keys ship translated in the same change**, not English-only. v1.27 made the app fully localized; English-only keys regress six locales. Add to `Strings.resx` **and** all six `docs/store/translations/ui-*.json`, then `python scripts/gen-culture-resx.py <c>` for each and `python scripts/lint-translations.py`.
- **Always name `ROROROblox.slnx`.** A bare `dotnet build` errors MSB1011 while a gitignored legacy `.sln` exists. Build Release, or quit a running dev build first — it locks `bin`.
- **Time comes from an injected `TimeProvider`, never `DateTime.Now` or `Stopwatch`.** Established by `FpsCapSettler`, `AlertDispatcher`, `RobloxLauncher`, and enforced in spirit by the 2026-09-09 flake fix. A test that sleeps is a test that flakes on arm64.

## File Structure

| File | Responsibility |
| --- | --- |
| `src/ROROROblox.Core/Metrics/MetricObservation.cs` (new) | The value a caller reports: one number, for one account, at one instant. |
| `src/ROROROblox.Core/Metrics/MetricRule.cs` (new) | The three rule shapes and their parameters. Pure data. |
| `src/ROROROblox.Core/Metrics/MetricHistory.cs` (new) | Bounded per-key sample window; rate over a span; counter-reset and gap handling. Pure. |
| `src/ROROROblox.Core/Metrics/MetricEvaluator.cs` (new) | Given history + rule + now, does this breach? Pure, no state. |
| `src/ROROROblox.Core/Metrics/MetricAlertCoordinator.cs` (new) | Holds histories, applies rules, emits `AlertTrigger`. The seam plan 2 feeds. |
| `src/ROROROblox.Core/Discord/AlertTrigger.cs` (modify) | `AlertKind.MetricBreach` + correct the "not extensible" comment. |
| `src/ROROROblox.Core/Discord/DiscordConfig.cs` (modify) | `MetricBreachDestinations` + the `DestinationsFor` case. |
| `src/ROROROblox.Core/AppSettings.cs` (modify) | `MetricAlertsEnabled` on the blob + `IAppSettings`. |
| `src/ROROROblox.App/Localization/CoreMessageCatalog.cs` (modify) | The breach sentence, behind a resx key. |
| `src/ROROROblox.Tests/Metrics/*` (new) | One test file per unit above. |

Metrics live in their own `Core/Metrics/` folder rather than under `Core/Discord/` because they are not Discord — the alert *destination* machinery happens to live in a folder named for its first consumer, and adding a second unrelated concept there would entrench that.

---

### Task 1: The vocabulary — `MetricObservation` and `AlertKind.MetricBreach`

**Files:**
- Create: `src/ROROROblox.Core/Metrics/MetricObservation.cs`
- Modify: `src/ROROROblox.Core/Discord/AlertTrigger.cs:1-19`
- Modify: `src/ROROROblox.Core/Discord/DiscordConfig.cs` (add `MetricBreachDestinations`, extend `DestinationsFor`)
- Test: `src/ROROROblox.Tests/Metrics/MetricVocabularyTests.cs`

**Interfaces:**
- Produces: `MetricObservation(Guid AccountId, string MetricId, double Value, DateTimeOffset ObservedAtUtc)`; `AlertKind.MetricBreach`; `DiscordConfig.MetricBreachDestinations` (`IReadOnlyList<AlertDestination>`).

**Design note the implementer needs.** The observation carries a **RoRoRo `AccountId`**, not an external subject id. Mapping "external id 4168075450 → my alt Foo" is the caller's job (plan 3, from user config). Core never guesses, and this keeps mute and cooldown keyed exactly as every other alert kind is. An observation about something not mapped to an account uses `Guid.Empty`, which follows `UptimeMark`'s precedent: a global carrier that no single account's mute can silence.

- [ ] **Step 1: Write the failing test**

```csharp
using ROROROblox.Core.Discord;
using ROROROblox.Core.Metrics;

namespace ROROROblox.Tests.Metrics;

public class MetricVocabularyTests
{
    [Fact]
    public void MetricBreach_IsAnAlertKind()
    {
        Assert.True(Enum.IsDefined(AlertKind.MetricBreach));
    }

    [Fact]
    public void DestinationsFor_MetricBreach_ReturnsItsOwnList()
    {
        var config = new DiscordConfig
        {
            MetricBreachDestinations = [AlertDestination.Phone],
            DroppedOutDestinations = [AlertDestination.Local],
        };

        Assert.Equal([AlertDestination.Phone], config.DestinationsFor(AlertKind.MetricBreach));
    }

    [Fact]
    public void DestinationsFor_MetricBreach_DefaultsToNothing()
    {
        // Off until the user turns it on, same as every other kind. A new alert kind that
        // starts firing on upgrade is a bug, not a feature.
        Assert.Empty(new DiscordConfig().DestinationsFor(AlertKind.MetricBreach));
    }

    [Fact]
    public void Observation_CarriesAccountMetricValueAndInstant()
    {
        var id = Guid.NewGuid();
        var o = new MetricObservation(id, "battle.points", 48200, DateTimeOffset.UnixEpoch);

        Assert.Equal(id, o.AccountId);
        Assert.Equal("battle.points", o.MetricId);
        Assert.Equal(48200, o.Value);
        Assert.Equal(DateTimeOffset.UnixEpoch, o.ObservedAtUtc);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~MetricVocabularyTests"`
Expected: FAIL — `error CS0117: 'AlertKind' does not contain a definition for 'MetricBreach'`.

- [ ] **Step 3: Add the observation record**

Create `src/ROROROblox.Core/Metrics/MetricObservation.cs`:

```csharp
namespace ROROROblox.Core.Metrics;

/// <summary>
/// One number, reported for one account, at one instant.
///
/// <para>
/// <paramref name="AccountId"/> is a RoRoRo account, NOT an external subject id. Mapping an
/// external identity onto an account is the caller's job, so that mute and per-(account, kind)
/// cooldown key exactly as they do for every other alert kind. An observation that belongs to no
/// account uses <see cref="Guid.Empty"/> — the same global-carrier convention
/// <c>AlertKind.UptimeMark</c> uses, and for the same reason: one account's mute must not
/// silence something that is not about that account.
/// </para>
/// <para>
/// <paramref name="Value"/> is whatever the source reported, raw. It is frequently CUMULATIVE
/// (points so far, total earned) rather than a rate; deriving a rate is
/// <see cref="MetricHistory"/>'s job, from two observations.
/// </para>
/// </summary>
public sealed record MetricObservation(
    Guid AccountId,
    string MetricId,
    double Value,
    DateTimeOffset ObservedAtUtc);
```

- [ ] **Step 4: Add the alert kind and correct the enum's comment**

In `src/ROROROblox.Core/Discord/AlertTrigger.cs`, replace the enum's summary and add the member. The existing summary says "The two things worth waking someone up for" and there are already four — it was stale before this change and must not be left claiming a closed set it no longer describes.

```csharp
/// <summary>
/// The things worth waking someone up for. NOT extensible without a design decision:
/// session-expired and landed-elsewhere were considered and cut (spec §11), and
/// <see cref="MetricBreach"/> was added only with a written design
/// (specs/2026-09-09-external-metric-alerts-design.md).
/// </summary>
public enum AlertKind
{
    AccountDroppedOut,
    MemoryWarning,

    /// <summary>A recycle completed — row button, plugin, or any future auto path. The
    /// away-from-PC completion notice (Este, 2026-09-05).</summary>
    Recycled,

    /// <summary>The periodic all-good mark while accounts are running (every 2 hours,
    /// <c>UptimeMarkTracker</c>). Its ABSENCE is the only dead-PC signal a PC-fired alert
    /// system can give. Carrier AccountId is Guid.Empty — a global mark must not be silenced
    /// by any one account's mute.</summary>
    UptimeMark,

    /// <summary>A user-configured metric crossed its rule — a contribution rate fell below a
    /// floor, a level crossed a threshold, a tracked value changed. The number arrives from
    /// outside the app; the RULE is evaluated here so this kind inherits mute, cooldown and
    /// coalescing like every other.</summary>
    MetricBreach,
}
```

- [ ] **Step 5: Add the destination list**

In `src/ROROROblox.Core/Discord/DiscordConfig.cs`, beside `UptimeMarkDestinations`:

```csharp
    /// <summary>Where a <see cref="AlertKind.MetricBreach"/> goes. Empty until the user opts in.</summary>
    public IReadOnlyList<AlertDestination> MetricBreachDestinations { get; init; } = [];
```

and add the arm to `DestinationsFor`. Match the existing arms' shape exactly — several read `(PluralList, LegacySingular)` to migrate a pre-fanout blob. `MetricBreach` has no legacy singular because it never shipped one, so it returns its list directly:

```csharp
            AlertKind.MetricBreach => (MetricBreachDestinations, AlertDestination.None),
```

The arms destructure a `(PluralList, LegacySingular)` tuple to migrate a pre-fanout config
blob. `MetricBreach` has no legacy singular because it never shipped one, so it passes
`AlertDestination.None` — the same shape `Recycled` and `UptimeMark` use.

- [ ] **Step 6: Run the tests**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~MetricVocabularyTests"`
Expected: PASS, 4 tests.

- [ ] **Step 7: Run the full suite**

Run: `dotnet build ROROROblox.slnx -c Release && dotnet test src/ROROROblox.Tests/ -c Release --no-build`
Expected: PASS. A `switch` over `AlertKind` elsewhere may now warn or fail to compile — fix each at its site rather than adding a catch-all `_ =>`, because a silent default is how a new kind gets dropped.

- [ ] **Step 8: Commit**

```bash
git add src/ROROROblox.Core/Metrics/MetricObservation.cs \
        src/ROROROblox.Core/Discord/AlertTrigger.cs \
        src/ROROROblox.Core/Discord/DiscordConfig.cs \
        src/ROROROblox.Tests/Metrics/MetricVocabularyTests.cs
git commit -m "feat(metrics): AlertKind.MetricBreach and the observation record"
```

---

### Task 2: `MetricHistory` — turning cumulative numbers into a rate

**Files:**
- Create: `src/ROROROblox.Core/Metrics/MetricHistory.cs`
- Test: `src/ROROROblox.Tests/Metrics/MetricHistoryTests.cs`

**Interfaces:**
- Consumes: `MetricObservation` (Task 1).
- Produces: `MetricHistory(int capacity = 64)`; `void Add(MetricObservation o)`; `double? RatePerMinute(Guid accountId, string metricId, TimeSpan window, DateTimeOffset nowUtc)`; `double? Latest(Guid accountId, string metricId)`; `int Count(Guid accountId, string metricId)`.

**The three cases the implementer must get right**, all of which arise in practice and none of which are edge cases:

1. **Not enough history.** One sample cannot yield a rate. Return `null`, never `0` — `0` means "earning nothing", which is exactly the alert condition, so returning it on startup pages everyone the moment the app opens.
2. **A counter reset.** The value goes *down* (a battle ended, a season rolled). A naive subtraction yields a huge negative rate. Treat a decrease as a reset: drop the older samples and return `null` until the window refills.
3. **A gap.** A poll was missed, the PC slept. The samples either side are real, so the rate across them is real — but only if they are both still inside the window. Do not extrapolate.

- [ ] **Step 1: Write the failing tests**

```csharp
using ROROROblox.Core.Metrics;

namespace ROROROblox.Tests.Metrics;

public class MetricHistoryTests
{
    private static readonly Guid Acct = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Metric = "battle.points";
    private static DateTimeOffset T(int minutes) => DateTimeOffset.UnixEpoch.AddMinutes(minutes);

    private static MetricHistory Seeded(params (int Minute, double Value)[] samples)
    {
        var h = new MetricHistory();
        foreach (var (m, v) in samples) h.Add(new MetricObservation(Acct, Metric, v, T(m)));
        return h;
    }

    [Fact]
    public void OneSample_HasNoRate()
    {
        // null, never 0. Zero means "earning nothing", which IS the alert condition -- returning
        // it from a cold start would page every user the moment the app opens.
        var h = Seeded((0, 1000));
        Assert.Null(h.RatePerMinute(Acct, Metric, TimeSpan.FromMinutes(10), T(1)));
    }

    [Fact]
    public void TwoSamples_RateIsTheDerivative()
    {
        var h = Seeded((0, 1000), (10, 2000));   // +1000 over 10 minutes
        Assert.Equal(100d, h.RatePerMinute(Acct, Metric, TimeSpan.FromMinutes(10), T(10))!.Value, 3);
    }

    [Fact]
    public void OnlySamplesInsideTheWindowCount()
    {
        // The 0-minute sample is outside a 10-minute window ending at minute 20.
        var h = Seeded((0, 0), (12, 5000), (20, 5500));
        Assert.Equal(62.5d, h.RatePerMinute(Acct, Metric, TimeSpan.FromMinutes(10), T(20))!.Value, 3);
    }

    [Fact]
    public void ADecreaseIsTreatedAsAResetAndSuppressesTheRate()
    {
        // A battle ended and the counter went back to zero. Subtracting would report a large
        // negative rate, which reads as catastrophic underperformance.
        var h = Seeded((0, 9000), (5, 9500), (10, 0));
        Assert.Null(h.RatePerMinute(Acct, Metric, TimeSpan.FromMinutes(10), T(10)));
    }

    [Fact]
    public void AfterAResetTheRateReturnsOnceTheWindowRefills()
    {
        var h = Seeded((0, 9000), (10, 0), (20, 500));
        Assert.Equal(50d, h.RatePerMinute(Acct, Metric, TimeSpan.FromMinutes(15), T(20))!.Value, 3);
    }

    [Fact]
    public void AGapDoesNotExtrapolate()
    {
        // 30 minutes with no poll, then two fresh samples. The rate is measured across what was
        // actually observed inside the window, not inferred across the hole.
        var h = Seeded((0, 1000), (30, 1000), (40, 2000));
        Assert.Equal(100d, h.RatePerMinute(Acct, Metric, TimeSpan.FromMinutes(10), T(40))!.Value, 3);
    }

    [Fact]
    public void KeysAreIsolatedByAccountAndMetric()
    {
        var other = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var h = new MetricHistory();
        h.Add(new MetricObservation(Acct, Metric, 0, T(0)));
        h.Add(new MetricObservation(Acct, Metric, 1000, T(10)));
        h.Add(new MetricObservation(other, Metric, 0, T(0)));

        Assert.Equal(100d, h.RatePerMinute(Acct, Metric, TimeSpan.FromMinutes(10), T(10))!.Value, 3);
        Assert.Null(h.RatePerMinute(other, Metric, TimeSpan.FromMinutes(10), T(10)));
        Assert.Null(h.RatePerMinute(Acct, "other.metric", TimeSpan.FromMinutes(10), T(10)));
    }

    [Fact]
    public void HistoryIsBoundedPerKey()
    {
        var h = new MetricHistory(capacity: 4);
        for (var i = 0; i < 20; i++) h.Add(new MetricObservation(Acct, Metric, i * 100, T(i)));
        Assert.Equal(4, h.Count(Acct, Metric));
    }

    [Fact]
    public void LatestIsTheMostRecentValue()
    {
        Assert.Equal(2000d, Seeded((0, 1000), (10, 2000)).Latest(Acct, Metric));
        Assert.Null(new MetricHistory().Latest(Acct, Metric));
    }
}
```

- [ ] **Step 2: Run and watch fail**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~MetricHistoryTests"`
Expected: FAIL — `MetricHistory` does not exist.

- [ ] **Step 3: Implement**

Create `src/ROROROblox.Core/Metrics/MetricHistory.cs`:

```csharp
using System.Collections.Concurrent;

namespace ROROROblox.Core.Metrics;

/// <summary>
/// A bounded sample window per (account, metric), and the derivative over it.
///
/// <para>
/// Exists because reported values are usually CUMULATIVE — points so far, total earned — while
/// the interesting question is a RATE. Two samples make a rate; one makes nothing.
/// </para>
/// <para>
/// Thread-safe for concurrent <see cref="Add"/> from a reporting path and reads from an
/// evaluation path, because those are different callers in the shipped shape.
/// </para>
/// </summary>
public sealed class MetricHistory(int capacity = 64)
{
    private readonly record struct Sample(double Value, DateTimeOffset AtUtc);

    private readonly ConcurrentDictionary<(Guid, string), List<Sample>> _series = new();
    private readonly int _capacity = capacity > 1
        ? capacity
        : throw new ArgumentOutOfRangeException(nameof(capacity), "need room for at least two samples");

    public void Add(MetricObservation o)
    {
        ArgumentNullException.ThrowIfNull(o);
        var list = _series.GetOrAdd((o.AccountId, o.MetricId), _ => []);
        lock (list)
        {
            list.Add(new Sample(o.Value, o.ObservedAtUtc));
            if (list.Count > _capacity) list.RemoveRange(0, list.Count - _capacity);
        }
    }

    public int Count(Guid accountId, string metricId)
    {
        if (!_series.TryGetValue((accountId, metricId), out var list)) return 0;
        lock (list) return list.Count;
    }

    public double? Latest(Guid accountId, string metricId)
    {
        if (!_series.TryGetValue((accountId, metricId), out var list)) return null;
        lock (list) return list.Count == 0 ? null : list[^1].Value;
    }

    /// <summary>
    /// Change per minute across the samples inside <paramref name="window"/>, or <c>null</c> when
    /// that cannot be measured: fewer than two samples in the window, a zero-length span, or a
    /// DECREASE anywhere in it.
    ///
    /// <para>
    /// A decrease means the counter reset — a battle ended, a season rolled — and subtracting
    /// across it reports a large negative rate that reads as catastrophic underperformance.
    /// Returning null makes the caller wait for the window to refill, which is the honest
    /// answer: after a reset there genuinely is no rate yet.
    /// </para>
    /// </summary>
    public double? RatePerMinute(Guid accountId, string metricId, TimeSpan window, DateTimeOffset nowUtc)
    {
        if (!_series.TryGetValue((accountId, metricId), out var list)) return null;

        Sample[] inWindow;
        lock (list)
        {
            var cutoff = nowUtc - window;
            inWindow = [.. list.Where(s => s.AtUtc >= cutoff && s.AtUtc <= nowUtc)];
        }
        if (inWindow.Length < 2) return null;

        for (var i = 1; i < inWindow.Length; i++)
        {
            if (inWindow[i].Value < inWindow[i - 1].Value) return null;   // reset
        }

        var first = inWindow[0];
        var last = inWindow[^1];
        var minutes = (last.AtUtc - first.AtUtc).TotalMinutes;
        return minutes <= 0 ? null : (last.Value - first.Value) / minutes;
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~MetricHistoryTests"`
Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/Metrics/MetricHistory.cs src/ROROROblox.Tests/Metrics/MetricHistoryTests.cs
git commit -m "feat(metrics): bounded per-key history with reset-aware rate"
```

---

### Task 3: `MetricRule` and `MetricEvaluator` — the three rules

**Files:**
- Create: `src/ROROROblox.Core/Metrics/MetricRule.cs`
- Create: `src/ROROROblox.Core/Metrics/MetricEvaluator.cs`
- Test: `src/ROROROblox.Tests/Metrics/MetricEvaluatorTests.cs`

**Interfaces:**
- Consumes: `MetricHistory` (Task 2), `MetricObservation` (Task 1).
- Produces: `enum MetricRuleKind { Rate, Level, Event }`; `sealed record MetricRule(string MetricId, MetricRuleKind Kind, double Threshold, TimeSpan Window, bool AlertWhenBelow = true)`; `static MetricBreachVerdict MetricEvaluator.Evaluate(MetricRule rule, MetricHistory history, Guid accountId, DateTimeOffset nowUtc)`; `sealed record MetricBreachVerdict(bool Breached, double? Observed, string? Reason)`.

**Semantics the implementer must not improvise:**

- **Rate** — breach when `RatePerMinute(window)` is **below** `Threshold`. A `null` rate is **never** a breach (see Task 2 step 1's reasoning: unknown is not zero).
- **Level** — breach when the latest value crosses `Threshold`, direction set by `AlertWhenBelow`. `Window` is unused.
- **Event** — breach when the latest value **differs from** the previous one. `Threshold` and `AlertWhenBelow` are unused; this is a change detector, not a comparison.

- [ ] **Step 1: Write the failing tests**

```csharp
using ROROROblox.Core.Metrics;

namespace ROROROblox.Tests.Metrics;

public class MetricEvaluatorTests
{
    private static readonly Guid Acct = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string M = "battle.points";
    private static DateTimeOffset T(int minutes) => DateTimeOffset.UnixEpoch.AddMinutes(minutes);

    private static MetricHistory Seeded(params (int Minute, double Value)[] s)
    {
        var h = new MetricHistory();
        foreach (var (m, v) in s) h.Add(new MetricObservation(Acct, M, v, T(m)));
        return h;
    }

    private static MetricRule Rate(double floor) =>
        new(M, MetricRuleKind.Rate, floor, TimeSpan.FromMinutes(10));

    [Fact]
    public void Rate_BelowTheFloor_Breaches()
    {
        var v = MetricEvaluator.Evaluate(Rate(100), Seeded((0, 0), (10, 500)), Acct, T(10));
        Assert.True(v.Breached);
        Assert.Equal(50d, v.Observed!.Value, 3);
    }

    [Fact]
    public void Rate_AtOrAboveTheFloor_DoesNot()
    {
        Assert.False(MetricEvaluator.Evaluate(Rate(100), Seeded((0, 0), (10, 1000)), Acct, T(10)).Breached);
        Assert.False(MetricEvaluator.Evaluate(Rate(100), Seeded((0, 0), (10, 2000)), Acct, T(10)).Breached);
    }

    [Fact]
    public void Rate_UnknownIsNeverABreach()
    {
        // The single most important line in this file. Unknown is not zero, and zero is the
        // alert condition -- conflating them pages every user on startup and after every reset.
        var oneSample = MetricEvaluator.Evaluate(Rate(100), Seeded((0, 0)), Acct, T(1));
        Assert.False(oneSample.Breached);
        Assert.Null(oneSample.Observed);

        var afterReset = MetricEvaluator.Evaluate(Rate(100), Seeded((0, 9000), (10, 0)), Acct, T(10));
        Assert.False(afterReset.Breached);
    }

    [Fact]
    public void Level_BelowThreshold_Breaches_WhenAlertWhenBelow()
    {
        var rule = new MetricRule(M, MetricRuleKind.Level, 500, TimeSpan.Zero, AlertWhenBelow: true);
        Assert.True(MetricEvaluator.Evaluate(rule, Seeded((0, 400)), Acct, T(0)).Breached);
        Assert.False(MetricEvaluator.Evaluate(rule, Seeded((0, 600)), Acct, T(0)).Breached);
    }

    [Fact]
    public void Level_AboveThreshold_Breaches_WhenAlertWhenAbove()
    {
        var rule = new MetricRule(M, MetricRuleKind.Level, 500, TimeSpan.Zero, AlertWhenBelow: false);
        Assert.True(MetricEvaluator.Evaluate(rule, Seeded((0, 600)), Acct, T(0)).Breached);
        Assert.False(MetricEvaluator.Evaluate(rule, Seeded((0, 400)), Acct, T(0)).Breached);
    }

    [Fact]
    public void Level_WithNoSamples_DoesNotBreach()
    {
        var rule = new MetricRule(M, MetricRuleKind.Level, 500, TimeSpan.Zero);
        Assert.False(MetricEvaluator.Evaluate(rule, new MetricHistory(), Acct, T(0)).Breached);
    }

    [Fact]
    public void Event_BreachesOnChange_NotOnRepeat()
    {
        var rule = new MetricRule(M, MetricRuleKind.Event, 0, TimeSpan.Zero);
        Assert.True(MetricEvaluator.Evaluate(rule, Seeded((0, 3), (5, 4)), Acct, T(5)).Breached);
        Assert.False(MetricEvaluator.Evaluate(rule, Seeded((0, 3), (5, 3)), Acct, T(5)).Breached);
    }

    [Fact]
    public void Event_WithOneSample_DoesNotBreach()
    {
        var rule = new MetricRule(M, MetricRuleKind.Event, 0, TimeSpan.Zero);
        Assert.False(MetricEvaluator.Evaluate(rule, Seeded((0, 3)), Acct, T(0)).Breached);
    }

    [Fact]
    public void Verdict_CarriesAReasonKeyNotProse()
    {
        // CoreStringBoundaryFenceTests bans prose in Core. Reason is a KEY the App renders.
        var v = MetricEvaluator.Evaluate(Rate(100), Seeded((0, 0), (10, 500)), Acct, T(10));
        Assert.Equal("rate_below", v.Reason);
    }
}
```

- [ ] **Step 2: Run and watch fail**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~MetricEvaluatorTests"`
Expected: FAIL — `MetricRule` does not exist.

- [ ] **Step 3: Implement the rule shapes**

Create `src/ROROROblox.Core/Metrics/MetricRule.cs`:

```csharp
namespace ROROROblox.Core.Metrics;

/// <summary>
/// How a metric is judged. Three kinds, because "points per minute" is too narrow: a tracked
/// number can be a climbing counter, a standing level, or a thing that simply changed.
/// </summary>
public enum MetricRuleKind
{
    /// <summary>The value climbs. Breach when its change per minute over the window falls
    /// below the threshold. An unmeasurable rate is never a breach.</summary>
    Rate,

    /// <summary>The value is absolute. Breach when it crosses the threshold in the stated
    /// direction. The window is unused.</summary>
    Level,

    /// <summary>Breach when the value differs from the previous observation. Threshold and
    /// direction are unused — this is a change detector.</summary>
    Event,
}

/// <summary>One rule against one metric. Supplied by the caller; core never invents one.</summary>
/// <param name="AlertWhenBelow">Level only. Ignored by Rate (always "below") and Event.</param>
public sealed record MetricRule(
    string MetricId,
    MetricRuleKind Kind,
    double Threshold,
    TimeSpan Window,
    bool AlertWhenBelow = true);

/// <summary>
/// The outcome. <paramref name="Reason"/> is a resx KEY fragment, never a sentence —
/// <c>CoreStringBoundaryFenceTests</c> bans prose in Core, and the App renders it through
/// <c>CoreMessageCatalog</c>.
/// </summary>
public sealed record MetricBreachVerdict(bool Breached, double? Observed, string? Reason);
```

- [ ] **Step 4: Implement the evaluator**

Create `src/ROROROblox.Core/Metrics/MetricEvaluator.cs`:

```csharp
namespace ROROROblox.Core.Metrics;

/// <summary>Applies one <see cref="MetricRule"/> to one account's history. Pure and stateless.</summary>
public static class MetricEvaluator
{
    private static readonly MetricBreachVerdict NoBreach = new(false, null, null);

    public static MetricBreachVerdict Evaluate(
        MetricRule rule, MetricHistory history, Guid accountId, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(history);

        return rule.Kind switch
        {
            MetricRuleKind.Rate => EvaluateRate(rule, history, accountId, nowUtc),
            MetricRuleKind.Level => EvaluateLevel(rule, history, accountId),
            MetricRuleKind.Event => EvaluateEvent(rule, history, accountId),
            _ => NoBreach,
        };
    }

    private static MetricBreachVerdict EvaluateRate(
        MetricRule rule, MetricHistory history, Guid accountId, DateTimeOffset nowUtc)
    {
        var rate = history.RatePerMinute(accountId, rule.MetricId, rule.Window, nowUtc);
        // Unknown is NOT zero. Zero is the alert condition, so treating an unmeasurable rate as
        // zero pages every user at startup and after every counter reset.
        if (rate is null) return NoBreach;
        return rate.Value < rule.Threshold
            ? new MetricBreachVerdict(true, rate.Value, "rate_below")
            : new MetricBreachVerdict(false, rate.Value, null);
    }

    private static MetricBreachVerdict EvaluateLevel(MetricRule rule, MetricHistory history, Guid accountId)
    {
        var latest = history.Latest(accountId, rule.MetricId);
        if (latest is null) return NoBreach;
        var breached = rule.AlertWhenBelow ? latest.Value < rule.Threshold : latest.Value > rule.Threshold;
        return breached
            ? new MetricBreachVerdict(true, latest.Value, rule.AlertWhenBelow ? "level_below" : "level_above")
            : new MetricBreachVerdict(false, latest.Value, null);
    }

    private static MetricBreachVerdict EvaluateEvent(MetricRule rule, MetricHistory history, Guid accountId)
    {
        if (history.Count(accountId, rule.MetricId) < 2) return NoBreach;
        var changed = history.Changed(accountId, rule.MetricId);
        var latest = history.Latest(accountId, rule.MetricId);
        return changed
            ? new MetricBreachVerdict(true, latest, "value_changed")
            : new MetricBreachVerdict(false, latest, null);
    }
}
```

- [ ] **Step 5: Add the `Changed` helper the evaluator needs**

`MetricEvaluator.EvaluateEvent` calls `history.Changed(...)`, which Task 2 did not define. Add it to `MetricHistory`:

```csharp
    /// <summary>True when the two most recent samples differ. Used by
    /// <see cref="MetricRuleKind.Event"/>; false when there are fewer than two.</summary>
    public bool Changed(Guid accountId, string metricId)
    {
        if (!_series.TryGetValue((accountId, metricId), out var list)) return false;
        lock (list) return list.Count >= 2 && list[^1].Value != list[^2].Value;
    }
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~MetricEvaluatorTests"`
Expected: PASS, 9 tests.

- [ ] **Step 7: Commit**

```bash
git add src/ROROROblox.Core/Metrics/MetricRule.cs \
        src/ROROROblox.Core/Metrics/MetricEvaluator.cs \
        src/ROROROblox.Core/Metrics/MetricHistory.cs \
        src/ROROROblox.Tests/Metrics/MetricEvaluatorTests.cs
git commit -m "feat(metrics): rate, level and event rules"
```

---

### Task 4: `MetricAlertCoordinator` — observations become triggers

**Files:**
- Create: `src/ROROROblox.Core/Metrics/MetricAlertCoordinator.cs`
- Test: `src/ROROROblox.Tests/Metrics/MetricAlertCoordinatorTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–3.
- Produces: `MetricAlertCoordinator(TimeProvider time, int historyCapacity = 64)`; `void SetRules(IReadOnlyList<MetricRule> rules)`; `IReadOnlyList<AlertTrigger> Observe(MetricObservation o, string displayName, string realName)`.

**Why the coordinator returns triggers rather than sending them:** the caller hands the result to `AlertDispatcher`, which already routes through `AlertRouter`. That is what makes mute, per-(account, kind) cooldown and coalescing apply for free (spec §1.1). A coordinator that dispatched directly would bypass all three, which is the exact failure mode the spec forbids.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.Extensions.Time.Testing;
using ROROROblox.Core.Discord;
using ROROROblox.Core.Metrics;

namespace ROROROblox.Tests.Metrics;

public class MetricAlertCoordinatorTests
{
    private static readonly Guid Acct = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string M = "battle.points";

    private static (MetricAlertCoordinator Sut, FakeTimeProvider Clock) New(params MetricRule[] rules)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var sut = new MetricAlertCoordinator(clock);
        sut.SetRules(rules);
        return (sut, clock);
    }

    private static MetricObservation Obs(double v, DateTimeOffset at) => new(Acct, M, v, at);

    [Fact]
    public void NoRuleForTheMetric_ProducesNothing()
    {
        var (sut, clock) = New();
        Assert.Empty(sut.Observe(Obs(100, clock.GetUtcNow()), "Masked", "Real"));
    }

    [Fact]
    public void ARateBreach_ProducesOneMetricBreachTrigger()
    {
        var (sut, clock) = New(new MetricRule(M, MetricRuleKind.Rate, 100, TimeSpan.FromMinutes(10)));

        sut.Observe(Obs(0, clock.GetUtcNow()), "Masked", "Real");
        clock.Advance(TimeSpan.FromMinutes(10));
        var triggers = sut.Observe(Obs(500, clock.GetUtcNow()), "Masked", "Real");   // 50/min

        var t = Assert.Single(triggers);
        Assert.Equal(AlertKind.MetricBreach, t.Kind);
        Assert.Equal(Acct, t.AccountId);
        Assert.Equal("Masked", t.DisplayName);
        Assert.Equal("Real", t.RealName);
    }

    [Fact]
    public void HealthyRate_ProducesNothing()
    {
        var (sut, clock) = New(new MetricRule(M, MetricRuleKind.Rate, 100, TimeSpan.FromMinutes(10)));

        sut.Observe(Obs(0, clock.GetUtcNow()), "Masked", "Real");
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Empty(sut.Observe(Obs(2000, clock.GetUtcNow()), "Masked", "Real"));
    }

    [Fact]
    public void FirstObservation_NeverAlerts()
    {
        // A cold start has no rate. This is the startup-page bug, guarded at the seam as well as
        // in the evaluator, because this is the surface a plugin actually touches.
        var (sut, clock) = New(new MetricRule(M, MetricRuleKind.Rate, 100, TimeSpan.FromMinutes(10)));
        Assert.Empty(sut.Observe(Obs(0, clock.GetUtcNow()), "Masked", "Real"));
    }

    [Fact]
    public void TheCoordinatorDoesNotDeduplicate_TheRouterDoes()
    {
        // Deliberate: repeated breaches produce repeated triggers here, and AlertRouter's
        // per-(account, kind) cooldown is what stops them becoming forty notifications. Keeping
        // suppression in ONE place is the point -- two half-implementations disagree eventually.
        var (sut, clock) = New(new MetricRule(M, MetricRuleKind.Rate, 100, TimeSpan.FromMinutes(10)));

        sut.Observe(Obs(0, clock.GetUtcNow()), "Masked", "Real");
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Single(sut.Observe(Obs(500, clock.GetUtcNow()), "Masked", "Real"));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Single(sut.Observe(Obs(550, clock.GetUtcNow()), "Masked", "Real"));
    }

    [Fact]
    public void TriggerCarriesNoProse()
    {
        // Core emits data; the App renders the sentence. GameName is reused as the metric id
        // carrier rather than adding a field to a record four other kinds depend on.
        var (sut, clock) = New(new MetricRule(M, MetricRuleKind.Rate, 100, TimeSpan.FromMinutes(10)));
        sut.Observe(Obs(0, clock.GetUtcNow()), "Masked", "Real");
        clock.Advance(TimeSpan.FromMinutes(10));

        var t = Assert.Single(sut.Observe(Obs(500, clock.GetUtcNow()), "Masked", "Real"));
        Assert.Equal(M, t.GameName);
    }
}
```

- [ ] **Step 2: Run and watch fail**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~MetricAlertCoordinatorTests"`
Expected: FAIL — `MetricAlertCoordinator` does not exist.

- [ ] **Step 3: Implement**

Create `src/ROROROblox.Core/Metrics/MetricAlertCoordinator.cs`:

```csharp
using ROROROblox.Core.Discord;

namespace ROROROblox.Core.Metrics;

/// <summary>
/// The seam between "a number arrived" and "the alert system knows". Holds history, applies the
/// configured rules, and RETURNS triggers rather than sending them.
///
/// <para>
/// Returning is the whole design. The caller hands these to <c>AlertDispatcher</c>, which routes
/// through <c>AlertRouter</c> — so mute, per-(account, kind) cooldown, coalescing and
/// fallback-to-Local all apply exactly as they do for every other kind. A coordinator that
/// dispatched directly would bypass all four, and "a bad night becomes forty notifications" is
/// the one outcome this feature must not produce.
/// </para>
/// </summary>
public sealed class MetricAlertCoordinator(TimeProvider time, int historyCapacity = 64)
{
    private readonly TimeProvider _time = time ?? throw new ArgumentNullException(nameof(time));
    private readonly MetricHistory _history = new(historyCapacity);
    private volatile IReadOnlyList<MetricRule> _rules = [];

    /// <summary>Replaces the rule set wholesale. Called on settings change and on manifest load.</summary>
    public void SetRules(IReadOnlyList<MetricRule> rules) =>
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));

    /// <summary>
    /// Records an observation and returns any triggers it caused. Empty is the common case and
    /// is not an error.
    /// </summary>
    /// <param name="displayName">Already streamer-masked by the caller, like every other trigger.</param>
    /// <param name="realName">The true account name. Only the clan destination is allowed to use it.</param>
    public IReadOnlyList<AlertTrigger> Observe(MetricObservation o, string displayName, string realName)
    {
        ArgumentNullException.ThrowIfNull(o);
        _history.Add(o);

        var now = _time.GetUtcNow();
        List<AlertTrigger>? triggers = null;

        foreach (var rule in _rules)
        {
            if (!string.Equals(rule.MetricId, o.MetricId, StringComparison.Ordinal)) continue;

            var verdict = MetricEvaluator.Evaluate(rule, _history, o.AccountId, now);
            if (!verdict.Breached) continue;

            // GameName carries the metric id rather than a fifth field on AlertTrigger: four
            // other kinds share that record, and widening it for one would touch every one of
            // them. PrivateBytes carries the observed value for the same reason.
            (triggers ??= []).Add(new AlertTrigger(
                AlertKind.MetricBreach,
                o.AccountId,
                displayName,
                realName,
                o.MetricId,
                verdict.Observed is null ? null : (long)verdict.Observed.Value,
                now));
        }

        return (IReadOnlyList<AlertTrigger>?)triggers ?? [];
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~MetricAlertCoordinatorTests"`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/Metrics/MetricAlertCoordinator.cs \
        src/ROROROblox.Tests/Metrics/MetricAlertCoordinatorTests.cs
git commit -m "feat(metrics): coordinator turning observations into router-bound triggers"
```

---

### Task 5: Router integration — a breach survives mute and cooldown

**Files:**
- Test: `src/ROROROblox.Tests/Metrics/MetricRoutingTests.cs`
- Modify: `src/ROROROblox.Core/Discord/AlertRouter.cs` only if a test fails.

**Interfaces:**
- Consumes: `AlertKind.MetricBreach` (Task 1), `MetricBreachDestinations` (Task 1).

This task is written test-first with **no expected production change** — `AlertRouter` is kind-agnostic and Task 1 gave it a destination list, so these should pass immediately. That is the point: it proves the claim in spec §1.1 rather than asserting it. If any fails, fix `AlertRouter` at that site.

- [ ] **Step 1: Write the tests**

```csharp
using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Metrics;

public class MetricRoutingTests
{
    private static AlertTrigger Breach(Guid id) =>
        new(AlertKind.MetricBreach, id, "Masked", "Real", "battle.points", 50, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Configured_RoutesToItsDestination()
    {
        var config = new DiscordConfig { MetricBreachDestinations = [AlertDestination.Phone] };

        var routed = AlertRouter.Route([Breach(Guid.NewGuid())], config,
            new Dictionary<(Guid, AlertKind), DateTimeOffset>(), DateTimeOffset.UnixEpoch,
            phoneConfigured: true);

        Assert.Equal(AlertDestination.Phone, Assert.Single(routed).Destination);
    }

    [Fact]
    public void Unconfigured_FallsBackToDesktop()
    {
        var config = new DiscordConfig { MetricBreachDestinations = [AlertDestination.Phone] };

        var routed = AlertRouter.Route([Breach(Guid.NewGuid())], config,
            new Dictionary<(Guid, AlertKind), DateTimeOffset>(), DateTimeOffset.UnixEpoch,
            phoneConfigured: false);

        Assert.Equal(AlertDestination.Local, Assert.Single(routed).Destination);
    }

    [Fact]
    public void AMutedAccount_IsSilent()
    {
        var id = Guid.NewGuid();
        var config = new DiscordConfig
        {
            MetricBreachDestinations = [AlertDestination.Local],
            MutedAccountIds = [id],
        };

        Assert.Empty(AlertRouter.Route([Breach(id)], config,
            new Dictionary<(Guid, AlertKind), DateTimeOffset>(), DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void InsideTheCooldown_IsSuppressed()
    {
        // The guarantee the whole design rests on: repeated breaches do not become repeated pages.
        var id = Guid.NewGuid();
        var config = new DiscordConfig { MetricBreachDestinations = [AlertDestination.Local] };
        var last = new Dictionary<(Guid, AlertKind), DateTimeOffset>
        {
            [(id, AlertKind.MetricBreach)] = DateTimeOffset.UnixEpoch,
        };

        Assert.Empty(AlertRouter.Route([Breach(id)], config, last,
            DateTimeOffset.UnixEpoch + AlertRouter.Cooldown - TimeSpan.FromSeconds(1)));

        Assert.Single(AlertRouter.Route([Breach(id)], config, last,
            DateTimeOffset.UnixEpoch + AlertRouter.Cooldown + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void TheCooldownIsPerKind_ADropOutDoesNotSuppressABreach()
    {
        var id = Guid.NewGuid();
        var config = new DiscordConfig
        {
            MetricBreachDestinations = [AlertDestination.Local],
            DroppedOutDestinations = [AlertDestination.Local],
        };
        var last = new Dictionary<(Guid, AlertKind), DateTimeOffset>
        {
            [(id, AlertKind.AccountDroppedOut)] = DateTimeOffset.UnixEpoch,
        };

        Assert.Single(AlertRouter.Route([Breach(id)], config, last,
            DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(1)));
    }
}
```

- [ ] **Step 2: Run them**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~MetricRoutingTests"`
Expected: PASS, 5 tests, with no production change. If `Unconfigured_FallsBackToDesktop` fails, `AlertRouter.Resolve`'s blank-endpoint arm does not cover `Phone` for this kind — fix it there, not in the test.

- [ ] **Step 3: Commit**

```bash
git add src/ROROROblox.Tests/Metrics/MetricRoutingTests.cs
git commit -m "test(metrics): pin that MetricBreach inherits mute, cooldown and fallback"
```

---

### Task 6: Settings, prose, and the translated strings

**Files:**
- Modify: `src/ROROROblox.Core/AppSettings.cs` (blob record at the bottom + `IAppSettings`)
- Modify: `src/ROROROblox.App/Localization/CoreMessageCatalog.cs`
- Modify: `src/ROROROblox.App/Properties/Strings.resx`
- Modify: `docs/store/translations/ui-{fr,de,ru,pt-BR,pl,es}.json`
- Modify: the four `IAppSettings` fakes — `src/ROROROblox.Tests/MainViewModelTests.cs`, `RobloxLauncherTests.cs`, `StreamerIdentityProviderTests.cs`, `Discord/DiscordTestHarness.cs`
- Test: `src/ROROROblox.Tests/Metrics/MetricSettingsTests.cs`

**Interfaces:**
- Produces: `Task<bool> IAppSettings.GetMetricAlertsEnabledAsync()`, `Task SetMetricAlertsEnabledAsync(bool)`.

- [ ] **Step 1: Write the failing test**

```csharp
using ROROROblox.Core;

namespace ROROROblox.Tests.Metrics;

public class MetricSettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rororo-metrics-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task MetricAlerts_DefaultOff()
    {
        // A feature that starts alerting on upgrade is a bug. Opt-in, like every alert kind.
        Assert.False(await new AppSettings(_path).GetMetricAlertsEnabledAsync());
    }

    [Fact]
    public async Task MetricAlerts_RoundTrip()
    {
        var s = new AppSettings(_path);
        await s.SetMetricAlertsEnabledAsync(true);
        Assert.True(await new AppSettings(_path).GetMetricAlertsEnabledAsync());
    }

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch (Exception) { /* temp file */ }
    }
}
```

- [ ] **Step 2: Run and watch fail**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~MetricSettingsTests"`
Expected: FAIL — `GetMetricAlertsEnabledAsync` is not defined.

- [ ] **Step 3: Add the setting**

In `src/ROROROblox.Core/AppSettings.cs`, add to the private `SettingsBlob` record at the bottom of the file as a **default parameter** (so old files deserialize):

```csharp
    bool MetricAlertsEnabled = false,
```

Add the accessors beside the existing pairs, following `GetAlwaysShowRecycleAsync`'s shape exactly:

```csharp
    public async Task<bool> GetMetricAlertsEnabledAsync()
    {
        try { return (await LoadAsync().ConfigureAwait(false)).MetricAlertsEnabled; }
        catch (Exception) { return false; }
    }

    public async Task SetMetricAlertsEnabledAsync(bool enabled)
    {
        await MutateAsync(s => s with { MetricAlertsEnabled = enabled }).ConfigureAwait(false);
    }
```

Add both to the `IAppSettings` interface.

- [ ] **Step 4: Fix the four fakes**

They stop compiling. Add to each of `MainViewModelTests`, `RobloxLauncherTests`, `StreamerIdentityProviderTests`, `Discord/DiscordTestHarness`:

```csharp
        public Task<bool> GetMetricAlertsEnabledAsync() => Task.FromResult(false);
        public Task SetMetricAlertsEnabledAsync(bool enabled) => Task.CompletedTask;
```

- [ ] **Step 5: Add the sentence, in Core's catalog, behind a key**

In `src/ROROROblox.App/Localization/CoreMessageCatalog.cs`, add a case mapping a `MetricBreach` trigger to a key. The trigger carries the metric id in `GameName` and the observed value in `PrivateBytes` (Task 4):

```csharp
        AlertKind.MetricBreach =>
            Loc.Format("CoreMsg_MetricBreach", t.DisplayName, t.GameName ?? "", t.PrivateBytes ?? 0),
```

- [ ] **Step 6: Add the key, translated, in the same change**

Add to `src/ROROROblox.App/Properties/Strings.resx`:

```
CoreMsg_MetricBreach = {0} is under target on {1} — {2} per minute.
```

Add the same key to all six `docs/store/translations/ui-*.json`:

| lang | value |
| --- | --- |
| fr | `{0} est sous l'objectif sur {1} — {2} par minute.` |
| de | `{0} liegt bei {1} unter dem Ziel — {2} pro Minute.` |
| ru | `{0} ниже цели по {1} — {2} в минуту.` |
| pt-BR | `{0} está abaixo da meta em {1} — {2} por minuto.` |
| pl | `{0} jest poniżej celu w {1} — {2} na minutę.` |
| es | `{0} está por debajo del objetivo en {1} — {2} por minuto.` |

- [ ] **Step 7: Regenerate and lint the catalogs**

```bash
for c in fr de ru pt-BR pl es; do python scripts/gen-culture-resx.py $c; done
python scripts/lint-translations.py
```
Expected: all catalogs clean, key count risen by one, zero parity candidates.

- [ ] **Step 8: Run everything**

Run: `dotnet build ROROROblox.slnx -c Release && dotnet test src/ROROROblox.Tests/ -c Release --no-build`
Expected: PASS. `SettingsReachabilityTests` will fail if the new key is not reachable from a control — add it to that test's allow-list with a comment saying the UI lands in plan 3.

- [ ] **Step 9: Commit**

```bash
git add src/ROROROblox.Core/AppSettings.cs \
        src/ROROROblox.App/Localization/CoreMessageCatalog.cs \
        src/ROROROblox.App/Properties/ docs/store/translations/ \
        src/ROROROblox.Tests/
git commit -m "feat(metrics): opt-in setting and the breach sentence in seven languages"
```

---

## Self-Review

**Spec coverage.** §1.1 `MetricBreach` on existing rails → Tasks 1, 5. §1.2 plugin fetches / core decides → Task 4 returns rather than dispatches; the RPC itself is plan 2. §1.4 three metric kinds → Task 3. §3 nothing changes for existing kinds → Task 5 pins it. §5.1 unit coverage of rate gaps, resets, level crossings, router cases → Tasks 2, 3, 5.

**Deliberately deferred, with the plan that owns each:** §1.3 and §1.5 (the signed manifest, its fallback chain and rotation drill) → plan 3. §1.6's fence asserting no vendor hostname ships → plan 3, because there is nothing to assert against until a manifest and plugin exist. §5.2's RPC capability-map fences → plan 2. §5.4 live smoke → Este-gated after plan 3.

**Two judgement calls an implementer should not silently reverse.** `AlertTrigger` is reused rather than widened — `GameName` carries the metric id and `PrivateBytes` the observed value, because four other kinds share that record and adding a fifth field touches all of them. And `MetricHistory` returns `null`, never `0`, for an unmeasurable rate; three separate tests pin this because conflating them pages every user at startup and after every counter reset.

**One known wart, recorded rather than hidden.** `PrivateBytes` is a `long`, so a fractional rate truncates in the alert sentence (50.7/min renders as 50). Acceptable for a threshold notification. Fixing it properly means a field on `AlertTrigger`, which is the widening this plan avoids — revisit if a metric ever needs decimals to be legible.
