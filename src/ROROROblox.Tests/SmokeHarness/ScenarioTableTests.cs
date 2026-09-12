using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Metrics;
using ROROROblox.Core.Discord;
using ROROROblox.Core.Metrics;
using ROROROblox.MetricSmoke;

namespace ROROROblox.Tests.SmokeHarness;

/// <summary>
/// The fence between <see cref="ScenarioTable"/> and <c>docs/superpowers/smoke-metric-alerts.md</c>.
/// <para>
/// A smoke harness that quietly stops covering a row is worse than one that never covered it: the row
/// carries a `[harness]` marker, a reader believes one command checks it, and nobody runs it by hand
/// again. Nothing else in the suite can see that happen — the table is data and the list is prose —
/// so this file reads the list off disk and checks the mapping in BOTH directions: no scenario naming
/// a row that is not marked, no marked row without a scenario.
/// </para>
/// <para>
/// The other half of this file pins the things the scenarios assume about production and cannot
/// reference: that the rules file the harness writes is one the app's own parser reads, that its
/// deliberately truncated file really is unparseable, that the values it calls breaching breach under
/// the production evaluator, and that its waits are still keyed to the constants they were derived
/// from. Every one of those, wrong, is a harness that passes while the feature is broken.
/// </para>
/// </summary>
public class ScenarioTableTests
{
    /// <summary>
    /// Every row on the list, as it is written there today. A change to this number is a change to the
    /// smoke list, and it belongs in the same commit as a decision about whether the new row is
    /// covered — which is the whole reason it is asserted as equality rather than as a floor.
    /// </summary>
    private const int RowsOnTheSmokeList = 21;

    private const string HarnessMarker = "`[harness]`";

    private static readonly Regex RowPattern = new(@"^- \[(?<state>[ x-])\] \*\*(?<title>.+?)\*\*", RegexOptions.Compiled);

    private sealed record SmokeListRow(string Title, bool HarnessCovered);

    private static string SmokeListPath()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate ROROROblox.slnx above the test assembly.");
        var path = Path.Combine(root!, "docs", "superpowers", "smoke-metric-alerts.md");
        Assert.True(File.Exists(path), $"The smoke list is not at {path}.");
        return path;
    }

    /// <summary>
    /// Reads the list's rows. Titles are normalised — backticks dropped, runs of whitespace collapsed —
    /// because the prose is allowed to mark up a row's title (<c>subject_id</c> is in code font) and a
    /// scenario should not have to spell the markup to name the row.
    /// </summary>
    private static IReadOnlyList<SmokeListRow> ReadSmokeList()
    {
        var rows = new List<SmokeListRow>();
        foreach (var line in File.ReadAllLines(SmokeListPath()))
        {
            var match = RowPattern.Match(line);
            if (!match.Success) continue;
            rows.Add(new SmokeListRow(Normalise(match.Groups["title"].Value), line.Contains(HarnessMarker, StringComparison.Ordinal)));
        }
        return rows;
    }

    private static string Normalise(string title) =>
        Regex.Replace(title.Replace("`", string.Empty), @"\s+", " ").Trim();

    [Fact]
    public void TheSmokeListStillHasTheRowsThisFenceWasWrittenAgainst()
    {
        // A vacuity floor, and the only one that matters here: a broken filesystem walk or a changed
        // bullet style would make every assertion below pass over an empty list.
        var rows = ReadSmokeList();
        Assert.Equal(RowsOnTheSmokeList, rows.Count);
    }

    [Fact]
    public void EveryScenarioNamesARowThatExistsAndIsMarkedForTheHarness()
    {
        var rows = ReadSmokeList();
        var titles = rows.Select(r => r.Title).ToHashSet(StringComparer.Ordinal);
        var marked = rows.Where(r => r.HarnessCovered).Select(r => r.Title).ToHashSet(StringComparer.Ordinal);

        foreach (var scenario in ScenarioTable.All)
        {
            var named = Normalise(scenario.SmokeRow);
            Assert.True(titles.Contains(named),
                $"Scenario '{scenario.Name}' names a smoke row that does not exist: \"{scenario.SmokeRow}\". "
                + "Row titles must match the list exactly (backticks and whitespace aside).");
            Assert.True(marked.Contains(named),
                $"Scenario '{scenario.Name}' names \"{scenario.SmokeRow}\", which is on the list but not marked "
                + $"{HarnessMarker}. Mark the row in the same commit, or the list understates what is automated.");
        }
    }

    [Fact]
    public void EveryMarkedRowHasAtLeastOneScenario()
    {
        var marked = ReadSmokeList().Where(r => r.HarnessCovered).Select(r => r.Title).ToList();
        var claimed = ScenarioTable.All.Select(s => Normalise(s.SmokeRow)).ToHashSet(StringComparer.Ordinal);

        var orphans = marked.Where(t => !claimed.Contains(t)).ToList();
        Assert.True(orphans.Count == 0,
            $"These rows are marked {HarnessMarker} but no scenario covers them: {string.Join(" | ", orphans)}. "
            + "A marker nobody backs is the drift this fence exists to catch.");
    }

    [Fact]
    public void TheMarkedRowCountMatchesTheRowsTheTableCovers()
    {
        // The count check the brief asks for, stated over DISTINCT rows rather than scenarios: sixteen
        // scenarios cover fourteen rows because two rows carry two cases each (granted-then-revoked
        // versus never-declared; live pickup versus absence). Counting scenarios here would wrongly
        // demand a sixteenth and seventeenth marker.
        var marked = ReadSmokeList().Count(r => r.HarnessCovered);
        var covered = ScenarioTable.All.Select(s => Normalise(s.SmokeRow)).Distinct(StringComparer.Ordinal).Count();

        Assert.Equal(marked, covered);
        Assert.True(ScenarioTable.All.Count >= marked,
            "There cannot be fewer scenarios than the rows they cover.");
    }

    [Fact]
    public void ScenarioNamesAreUnique()
    {
        // The name is what a run prints per row; two scenarios sharing one makes a failure unreadable.
        var duplicates = ScenarioTable.All
            .GroupBy(s => s.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.True(duplicates.Count == 0, $"Duplicate scenario names: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void TheNegativeWindowIsKeyedToTheAlertCooldown()
    {
        // Spec §4.4: the wait a negative row sits through is derived from the cooldown, not chosen. If
        // someone replaces it with a literal, or retunes the cooldown in a direction that makes the
        // window shorter than the app's own coarsest cadence, a negative row starts being able to pass
        // before the app could have fired — which is the false green this harness exists to prevent.
        Assert.Equal(AlertRouter.Cooldown / 10, SmokeTimings.AlertWindow);
        Assert.True(SmokeTimings.AlertWindow >= SmokeTimings.AppRoutineTick,
            $"The alert window ({SmokeTimings.AlertWindow}) is shorter than the app's own routine tick "
            + $"({SmokeTimings.AppRoutineTick}), so a negative row could pass before the app looked.");
        Assert.Equal(SmokeTimings.AppRoutineTick * 2, SmokeTimings.SettingsPickup);
    }

    [Fact]
    public void TheAppRoutineTickMatchesTheViewModelsTimer()
    {
        // MainViewModel's interval is a literal in its constructor and PeriodicTick is internal, so the
        // harness cannot reference either — it carries its own copy, and this reads the real one off
        // disk. Without this, doubling the app's tick would leave the harness waiting half as long as a
        // settings pickup now takes and the opt-in row failing for a reason nobody would look for here.
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate ROROROblox.slnx above the test assembly.");
        var source = File.ReadAllText(Path.Combine(root!, "src", "ROROROblox.App", "ViewModels", "MainViewModel.cs"));

        var seconds = (int)SmokeTimings.AppRoutineTick.TotalSeconds;
        Assert.Contains($"new DispatcherTimer {{ Interval = TimeSpan.FromSeconds({seconds}) }}", source);
    }

    [Fact]
    public void TheRulesFileTheHarnessWritesIsOneTheAppCanRead()
    {
        // The harness composes that JSON by hand; the app parses it with LocalFileMetricRuleSource. A
        // property name the parser does not recognise would leave every rule at its default — threshold
        // 0, window 0 — and every breaching report would quietly stop breaching while the table looked
        // fine. This runs the real parser over the real bytes.
        var path = Path.Combine(Path.GetTempPath(), $"rororo-smoke-rules-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, SmokeRules.ToJson(SmokeRules.Canonical));
            var parsed = new LocalFileMetricRuleSource(path, NullLogger<LocalFileMetricRuleSource>.Instance)
                .CurrentRules();

            Assert.Equal(SmokeRules.Canonical.Count, parsed.Count);
            foreach (var expected in SmokeRules.Canonical)
            {
                var actual = Assert.Single(parsed.Where(r => r.MetricId == expected.MetricId));
                Assert.Equal(Enum.Parse<MetricRuleKind>(expected.Kind), actual.Kind);
                Assert.Equal(expected.Threshold, actual.Threshold);
                Assert.Equal(TimeSpan.FromMinutes(expected.WindowMinutes), actual.Window);
                Assert.Equal(expected.AlertWhenBelow, actual.AlertWhenBelow);
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void TheMalformedFileTheHarnessWritesReallyIsUnparseable()
    {
        // The malformed-rules row asserts that a truncated file costs the user their rules and not
        // their session. If the string the harness writes happened to parse, the row would assert
        // nothing at all — and would still go green.
        var path = Path.Combine(Path.GetTempPath(), $"rororo-smoke-rules-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, SmokeRules.MalformedJson);
            Assert.Empty(new LocalFileMetricRuleSource(path, NullLogger<LocalFileMetricRuleSource>.Instance)
                .CurrentRules());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void EveryMetricTheScenariosAlertOnHasARule()
    {
        // Two ids deliberately have no canonical rule, and both absences are load-bearing: the
        // accepted-report row must not raise an alert to prove an RPC works, and the live-pickup row's
        // whole subject is a rule that was not in the file the app started with. Every OTHER id must
        // have one, or the row that uses it asserts silence against nothing configured.
        var canonical = SmokeRules.Canonical.Select(r => r.MetricId).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(SmokeMetrics.Accepted, canonical);
        Assert.DoesNotContain(SmokeMetrics.Live, canonical);
        Assert.Contains(SmokeMetrics.Live, SmokeRules.WithLiveRule.Select(r => r.MetricId));

        foreach (var id in AllMetricIds().Where(id => id != SmokeMetrics.Accepted && id != SmokeMetrics.Live))
        {
            Assert.True(canonical.Contains(id), $"{id} is reported by a scenario but has no canonical rule.");
        }

        // One rule per id: two rules sharing one would raise at most one trigger per report anyway, so a
        // duplicate is a silent no-op rather than an error.
        Assert.Equal(SmokeRules.Canonical.Count, canonical.Count);
    }

    [Fact]
    public void TheValuesTheScenariosCallBreachingDoBreach()
    {
        // Pure Core, no app: the production evaluator over the production rule the harness writes. A
        // threshold on the wrong side of a value would make every positive row fail at Task 6 for a
        // reason the table's own shape should have caught here.
        var account = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var level = Assert.Single(SmokeRules.Canonical.Where(r => r.MetricId == SmokeMetrics.Value));
        Assert.True(Breaches(level, account, now, [(SmokeRules.BreachingLevel, TimeSpan.Zero)]),
            $"{SmokeRules.BreachingLevel} does not breach the Level rule the harness writes.");
        Assert.True(Breaches(level, account, now, [(0.79, TimeSpan.Zero)]),
            "0.79 does not breach the Level rule, so the value-rendering row would never produce a body.");

        // The toast row: two samples a minute apart, a real rate of one per minute against the floor.
        var rate = Assert.Single(SmokeRules.Canonical.Where(r => r.MetricId == SmokeMetrics.Toast));
        Assert.False(Breaches(rate, account, now, [(0, TimeSpan.FromMinutes(1))]),
            "one sample produced a rate verdict; unknown is not zero, and the row's second report exists "
            + "because of that.");
        Assert.True(Breaches(rate, account, now, [(0, TimeSpan.FromMinutes(1)), (1, TimeSpan.Zero)]),
            "the toast row's two samples do not breach the Rate rule the harness writes.");
    }

    [Fact]
    public void TheResetTheScenarioReportsBreachesNothing()
    {
        // The other direction, and the one that would be a false FAIL rather than a false pass: if the
        // reset row's samples breached at any step, the row would report a defect that is its own setup.
        var account = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var rule = Assert.Single(SmokeRules.Canonical.Where(r => r.MetricId == SmokeMetrics.Reset));

        Assert.False(Breaches(rule, account, now,
            [(0, TimeSpan.FromMinutes(2)), (500, TimeSpan.FromMinutes(1))]),
            "the reset row's two rising samples breach on their own, before the drop it is about.");
        Assert.False(Breaches(rule, account, now,
            [(0, TimeSpan.FromMinutes(2)), (500, TimeSpan.FromMinutes(1)), (50, TimeSpan.Zero)]),
            "the apparent drop read as a rate collapse — the row would fail on its own setup.");
    }

    [Fact]
    public async Task AWatchThatNeverPolledStillSeesADispatchFailure_OnceRefreshed()
    {
        // The hole this closes had the same shape as the defect the recogniser was added for. A watch
        // only knows what it has read, and five rows end on an early-exit poll — so a swallowed dispatch
        // landing a few milliseconds after that poll sat on disk, unread, and the row passed while the
        // alert it asserted on was dropped. The runner's final read is what makes the check honest.
        var dir = Path.Combine(Path.GetTempPath(), $"rororo-smoke-logs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var logPath = Path.Combine(dir, $"rororoblox-{DateTime.Now:yyyyMMdd}.log");
        try
        {
            await File.WriteAllTextAsync(logPath, "nothing interesting yet" + Environment.NewLine);

            var watch = LogWatch.Open(dir);

            await File.AppendAllTextAsync(logPath,
                "2026-09-11 14:22:07.400 -07:00 [WRN] v1.25.0.0 ROROROblox.App.Discord.AlertDispatcher "
                + "Alert dispatch failed; the alert was dropped." + Environment.NewLine);

            // Before the read: invisible. This is the state a row that early-exited would be judged in.
            Assert.Equal(0, watch.DispatchFailures);

            await watch.RefreshAsync();
            Assert.Equal(1, watch.DispatchFailures);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Theory]
    [InlineData("0.79")]
    [InlineData("0,79")]
    public void TheObservedValueIsReadInWhateverCultureTheAppRenderedIt(string token)
    {
        // WebhookPayload formats {v:0.##} under the RUNNING APP's culture, and this app ships six —
        // fr, de, ru, pt-BR, pl, es — several of which write a comma. A literal "0.79" comparison would
        // fail the value row on a localised install for a formatting difference that is correct.
        Assert.True(ScenarioTable.TryParseObserved(token, out var value));
        Assert.Equal(0.79, value, precision: 4);
    }

    [Fact]
    public void TheObservedValueReaderStillRejectsTheBugItWasWrittenFor()
    {
        // The defect the row exists for: the value rode a long? until 2026-09-09 and rendered "at 0" for
        // 0.79 and for nothing alike. Culture tolerance must not swallow that.
        Assert.True(ScenarioTable.TryParseObserved("0", out var zero));
        Assert.NotEqual(0.79, zero, precision: 4);
        Assert.False(ScenarioTable.TryParseObserved("not-a-number", out _));
    }

    private static IEnumerable<string> AllMetricIds() =>
    [
        SmokeMetrics.Toast, SmokeMetrics.Accepted, SmokeMetrics.Denied, SmokeMetrics.Gate,
        SmokeMetrics.Value, SmokeMetrics.Subject, SmokeMetrics.Skew, SmokeMetrics.Reset,
        SmokeMetrics.Repeat, SmokeMetrics.Live, SmokeMetrics.Absent, SmokeMetrics.Malformed,
        SmokeMetrics.Streamer, SmokeMetrics.Fallback,
    ];

    /// <summary>Feeds the samples a scenario reports through the production history and evaluator, and
    /// answers whether the LAST of them breaches.</summary>
    private static bool Breaches(
        MetricRuleRow row, Guid account, DateTimeOffset now, IReadOnlyList<(double Value, TimeSpan Ago)> samples)
    {
        var history = new MetricHistory();
        foreach (var (value, ago) in samples)
        {
            history.Add(new MetricObservation(account, row.MetricId, value, now - ago));
        }

        var rule = new MetricRule(
            row.MetricId,
            Enum.Parse<MetricRuleKind>(row.Kind),
            row.Threshold,
            TimeSpan.FromMinutes(row.WindowMinutes),
            row.AlertWhenBelow);

        return MetricEvaluator.Evaluate(rule, history, account, now).Breached;
    }
}
