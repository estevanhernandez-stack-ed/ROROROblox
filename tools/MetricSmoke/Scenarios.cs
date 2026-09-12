using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Grpc.Core;
using ROROROblox.App.Plugins;
using ROROROblox.Core.Discord;

namespace ROROROblox.MetricSmoke;

/// <summary>
/// The plugin ids the harness reports as. Three, because two smoke rows are about being REFUSED and
/// a refusal has to happen to something other than the id every other scenario depends on — a
/// scenario that revoked <see cref="Reporting"/> would break the nine rows after it.
/// </summary>
public static class SmokePluginIds
{
    /// <summary>The consented one. Granted at setup, revoked by the restore.</summary>
    public const string Reporting = "rororo.smoke";

    /// <summary>Granted, then revoked mid-run — the first of the two denial paths.</summary>
    public const string Revoked = "rororo.smoke.revoked";

    /// <summary>Never granted anything. Absence is denial, and this is the absence.</summary>
    public const string Undeclared = "rororo.smoke.undeclared";
}

/// <summary>
/// One metric id per scenario, and that is not tidiness — it is isolation. <c>MetricHistory</c> keys
/// its series by (account, metric) and the rules file is a flat list, so two scenarios sharing a
/// metric id would share a sample series: the reset row's deliberate decrease would sit in the
/// window the toast row measures its rate over, and each would silently change the other's verdict.
/// </summary>
public static class SmokeMetrics
{
    public const string Toast = "smoke.toast";
    public const string Accepted = "smoke.accepted";
    public const string Denied = "smoke.denied";
    public const string Gate = "smoke.gate";
    public const string Value = "smoke.value";
    public const string Subject = "smoke.subject";
    public const string Skew = "smoke.skew";
    public const string Reset = "smoke.reset";
    public const string Repeat = "smoke.repeat";
    public const string Live = "smoke.live";
    public const string Absent = "smoke.absent";
    public const string Malformed = "smoke.malformed";
    public const string Streamer = "smoke.streamer";
    public const string Fallback = "smoke.fallback";
}

/// <summary>
/// How long the harness waits, and why each number is derived rather than picked.
/// </summary>
public static class SmokeTimings
{
    /// <summary>
    /// The one window every assertion is measured against — spec §4.4's open question, answered.
    /// <para>
    /// <b>One window, two uses.</b> A positive row may leave it early, the moment the line it wants
    /// appears; a negative row has to sit through all of it. That symmetry is the point: two numbers
    /// would let the harness be patient about successes and impatient about failures, which is
    /// exactly how "no alert fired" becomes a false green — the one failure this whole harness exists
    /// to prevent.
    /// </para>
    /// <para>
    /// <b>Keyed to <see cref="AlertRouter.Cooldown"/>, not chosen.</b> The cooldown is the app's own
    /// statement of the timescale alerting decisions happen on, and it is the only production
    /// constant on this path that says anything about time at all. A tenth of it, so that retuning
    /// the cooldown moves the harness's patience with it in the right direction, and so the number
    /// cannot drift away from the app by being a literal here. At today's five minutes that is 30
    /// seconds, which is also — checked, not coincidental to rely on — at least the app's own
    /// coarsest routine cadence (<see cref="AppRoutineTick"/>); <c>ScenarioTableTests</c> fails if a
    /// future cooldown change pushes it below that.
    /// </para>
    /// <para>
    /// <b>Why not the whole cooldown.</b> The cooldown does not DELAY a delivery, it suppresses one.
    /// Nothing on the report path waits: <c>ReportMetric</c> is a synchronous pass-through, the sink
    /// raises on the gRPC thread, and <c>App</c> wires that straight into
    /// <c>AlertDispatcher.DispatchAsync</c> fire-and-forget, whose first act is the log line this
    /// harness reads. The physical latency is a localhost POST. So waiting out five whole minutes buys
    /// nothing a tenth of it does not, five times over, on five negative rows — and a harness that
    /// takes half an hour is one nobody runs twice, which is the same as not having it.
    /// </para>
    /// </summary>
    public static readonly TimeSpan AlertWindow = AlertRouter.Cooldown / 10;

    /// <summary>
    /// The app's own routine tick — <c>MainViewModel</c>'s <c>DispatcherTimer</c>, 30 seconds, which
    /// is what re-reads the metric-alert opt-in out of <c>settings.json</c>
    /// (<c>App.RefreshMetricAlertsGateAsync</c>, wired to <c>PeriodicTick</c>).
    /// <para>
    /// A literal here because there is nothing to reference: the interval is a literal inside
    /// <c>MainViewModel</c>'s constructor and the event is <c>internal</c>, so this tool cannot see
    /// either. <c>ScenarioTableTests</c> reads that constructor off disk and fails if the two stop
    /// agreeing — the same source-scanning fence this repo uses elsewhere, because the alternative is
    /// a harness that waits 60 seconds for a pickup the app now takes 120.
    /// </para>
    /// </summary>
    public static readonly TimeSpan AppRoutineTick = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long to wait after writing a setting before the running app is believed to have read it.
    /// <para>
    /// TWO ticks, not one, and the reason is a race rather than caution. The tick reads
    /// <c>settings.json</c> asynchronously; a tick that began its read before our write landed can
    /// commit the old value (its generation check defends against the Settings toggle nudging it, not
    /// against the file changing underneath). So the first tick after a write is not guaranteed to
    /// see it and the second is.
    /// </para>
    /// <para>
    /// Only the opt-in gate needs this. Streamer mode does NOT re-read at all —
    /// <c>StreamerIdentityProvider.IsActive</c> is set once at startup and changed only through the
    /// app's own toggle — which is why the runner writes it before the app starts rather than flipping
    /// it mid-run.
    /// </para>
    /// </summary>
    public static readonly TimeSpan SettingsPickup = AppRoutineTick * 2;

    /// <summary>How often the log is re-read while waiting. Small enough to be invisible next to
    /// <see cref="AlertWindow"/>, large enough not to spin on a file the app has open.</summary>
    public static readonly TimeSpan LogPollInterval = TimeSpan.FromMilliseconds(250);
}

/// <summary>One row of <c>metric-rules.json</c>, in the shape
/// <c>LocalFileMetricRuleSource.RuleRow</c> reads.</summary>
public sealed record MetricRuleRow(
    string MetricId,
    string Kind,
    double Threshold = 0,
    double WindowMinutes = 0,
    bool AlertWhenBelow = true);

/// <summary>
/// The rules file the harness writes, and the JSON encoder for it.
/// <para>
/// <see cref="SmokeMetrics.Accepted"/> deliberately has NO rule: the row it serves asserts only that
/// a consented plugin's report is accepted, and giving it a rule would fire an alert (and burn a
/// cooldown) to prove something about the RPC.
/// </para>
/// <para>
/// <see cref="SmokeMetrics.Live"/> is deliberately absent too — the live-pickup row's whole subject is
/// a rule that was not there when the app started.
/// </para>
/// </summary>
public static class SmokeRules
{
    /// <summary>A Level rule below this threshold breaches on one report of
    /// <see cref="BreachingLevel"/>, which is what lets most rows be a single report rather than a
    /// two-sample rate dance.</summary>
    public const double LevelThreshold = 0.8;

    public const double BreachingLevel = 0.1;

    /// <summary>The floor for the two Rate rows, in units per minute. Both feed samples whose real
    /// rate is far below it, so a breach is unambiguous rather than borderline.</summary>
    public const double RateFloor = 100;

    public const double RateWindowMinutes = 10;

    private static MetricRuleRow Level(string metricId) =>
        new(metricId, "Level", LevelThreshold, 0, AlertWhenBelow: true);

    private static MetricRuleRow Rate(string metricId) =>
        new(metricId, "Rate", RateFloor, RateWindowMinutes);

    /// <summary>What the runner writes at setup, before the app starts.</summary>
    public static IReadOnlyList<MetricRuleRow> Canonical { get; } =
    [
        Rate(SmokeMetrics.Toast),
        Rate(SmokeMetrics.Reset),
        Level(SmokeMetrics.Denied),
        Level(SmokeMetrics.Gate),
        Level(SmokeMetrics.Value),
        Level(SmokeMetrics.Subject),
        Level(SmokeMetrics.Skew),
        Level(SmokeMetrics.Repeat),
        Level(SmokeMetrics.Absent),
        Level(SmokeMetrics.Malformed),
        Level(SmokeMetrics.Streamer),
        Level(SmokeMetrics.Fallback),
    ];

    /// <summary><see cref="Canonical"/> plus the rule the live-pickup row adds while the app runs.</summary>
    public static IReadOnlyList<MetricRuleRow> WithLiveRule { get; } =
        [.. Canonical, Level(SmokeMetrics.Live)];

    /// <summary>Truncated mid-object, which is what the smoke row asks for — a file that parses as
    /// nothing, not a file with one bad row in it.</summary>
    public const string MalformedJson = "[ { \"metricId\": \"smoke.malformed\", \"kind\": \"Lev";

    public static string ToJson(IEnumerable<MetricRuleRow> rows) => JsonSerializer.Serialize(
        rows.Select(r => new
        {
            metricId = r.MetricId,
            kind = r.Kind,
            threshold = r.Threshold,
            windowMinutes = r.WindowMinutes,
            alertWhenBelow = r.AlertWhenBelow,
        }),
        new JsonSerializerOptions { WriteIndented = true });
}

/// <summary>Passed, failed, or not runnable on this profile. Skipped is never counted as green.</summary>
public enum ScenarioStatus
{
    Passed,
    Failed,
    Skipped,
}

/// <summary>What a scenario decided, and the sentence a human reads for it.</summary>
public sealed record ScenarioOutcome(ScenarioStatus Status, string Detail)
{
    public static ScenarioOutcome Pass(string detail) => new(ScenarioStatus.Passed, detail);

    public static ScenarioOutcome Fail(string detail) => new(ScenarioStatus.Failed, detail);

    /// <summary>Not runnable here, with the precondition that was missing. A skip is a gap in the
    /// run, reported as one — never folded into the pass count.</summary>
    public static ScenarioOutcome Skip(string detail) => new(ScenarioStatus.Skipped, detail);
}

/// <summary>
/// One automated smoke row.
/// <para>
/// <see cref="SmokeRow"/> is the load-bearing field. It is the title of the row in
/// <c>docs/superpowers/smoke-metric-alerts.md</c> that this scenario satisfies, and
/// <c>ScenarioTableTests</c> refuses to pass unless every one of them names a row that exists there
/// and is marked <c>[harness]</c>, and unless every marked row is named by at least one scenario.
/// That fence is the only thing standing between this table and the list drifting apart — and drift
/// there means the harness silently stops covering something everyone believes is covered, which is
/// worse than an uncovered row nobody claimed.
/// </para>
/// </summary>
public sealed record SmokeScenario(
    string Name,
    string SmokeRow,
    Func<ScenarioContext, Task<ScenarioOutcome>> Run);

/// <summary>
/// A read position in the app's log plus everything appended since, with the recognisers applied.
/// <para>
/// Opened per scenario rather than once per run, which resolves the newest log file each time: the
/// file rolls daily and at 25 MB, and a run that straddled a roll would otherwise keep tailing a file
/// nothing writes to any more — every later row reading as "nothing fired".
/// </para>
/// </summary>
public sealed class LogWatch
{
    private readonly LogTail _tail;
    private readonly List<string> _lines = [];

    private LogWatch(LogTail tail) => _tail = tail;

    /// <summary>Starts watching at the current end of the newest log file in
    /// <paramref name="logDirectory"/>.</summary>
    public static LogWatch Open(string logDirectory) => new(LogTail.OpenAt(NewestLogFile(logDirectory)));

    /// <summary>
    /// The newest <c>rororoblox-*.log</c> in the directory, or today's conventional name when there is
    /// none (<see cref="LogTail.OpenAt"/> tolerates a file that does not exist yet).
    /// <para>
    /// Newest-by-write-time rather than a computed name because Serilog rolls on TWO axes — daily,
    /// which appends the date, and at 25 MB, which appends <c>_001</c> — and only one of those is
    /// predictable from the clock. The path is derived from the data root the guard reports, never
    /// from a hardcoded profile path.
    /// </para>
    /// </summary>
    public static string NewestLogFile(string logDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);

        var conventional = Path.Combine(
            logDirectory, $"rororoblox-{DateTime.Now:yyyyMMdd}.log");
        if (!Directory.Exists(logDirectory)) return conventional;

        var newest = new DirectoryInfo(logDirectory)
            .EnumerateFiles("rororoblox-*.log")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
        return newest?.FullName ?? conventional;
    }

    /// <summary>Every line seen so far. Never printed wholesale by the runner: a delivered-alert line
    /// to the clan channel carries a REAL account name, and the harness's own console is not a place
    /// for one.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>
    /// Delivered alerts for one metric id, optionally for one destination.
    /// <para>
    /// Filtered by metric id, not just counted, because <see cref="LogTail.Delivered"/> sees EVERY
    /// alert kind. A genuine drop-out or memory warning landing mid-run would otherwise be counted as
    /// this scenario's alert. <c>WebhookPayload.ForAlert</c> puts the metric id in a metric breach's
    /// title, which is what makes the attribution possible at all.
    /// </para>
    /// </summary>
    public IReadOnlyList<DeliveredAlert> DeliveredFor(string metricId, string? destination = null)
        => [.. _tail.Delivered(_lines)
            .Where(d => d.Title.Contains(metricId, StringComparison.Ordinal))
            .Where(d => destination is null || string.Equals(d.Destination, destination, StringComparison.Ordinal))];

    /// <summary>
    /// How many triggers raised but reached no destination. NOT attributable to a metric — the line
    /// carries a count and nothing else — so a row asserting zero of these is asserting that nothing
    /// AT ALL routed nowhere in its window. An unrelated alert kind with no destination configured
    /// would fail such a row. That is a false red, never a false green, and rerunning settles it.
    /// </summary>
    public int RoutedNowhere => _tail.RoutedNowhereCount(_lines);

    /// <summary>The metric ids named on future-dated reports the sink dropped.</summary>
    public IReadOnlyList<string> SkewDrops => _tail.SkewDrops(_lines);

    /// <summary>
    /// Alerts the dispatcher logged and then lost to a swallowed exception. Checked by the runner after
    /// EVERY scenario rather than by individual rows, because the hazard is universal: the delivered
    /// line is written BEFORE the send, so any row asserting on that line passes while the delivery it
    /// names failed. See <see cref="LogTail.DispatchFailureCount"/> for why this is worth a false red.
    /// </summary>
    public int DispatchFailures => _tail.DispatchFailureCount(_lines);

    /// <summary>
    /// Reads until <paramref name="satisfied"/> is true or <paramref name="within"/> is spent. Early
    /// exit is allowed here and deliberately NOT in <see cref="SettleAsync"/>: proving a line arrived
    /// is finished the moment it arrives, while proving one never arrives is only finished when the
    /// window is.
    /// </summary>
    public async Task<bool> PollForAsync(Func<LogWatch, bool> satisfied, TimeSpan within)
    {
        ArgumentNullException.ThrowIfNull(satisfied);
        var deadline = DateTime.UtcNow + within;

        while (true)
        {
            _lines.AddRange(await _tail.NewLinesAsync().ConfigureAwait(false));
            if (satisfied(this)) return true;
            if (DateTime.UtcNow >= deadline) return false;
            await Task.Delay(SmokeTimings.LogPollInterval).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads for the whole of <paramref name="window"/> with no early exit — the negative assertion's
    /// method. Keeps polling rather than sleeping once and reading, so a line that lands mid-window is
    /// seen even if the app rolls the file underneath.
    /// </summary>
    public async Task SettleAsync(TimeSpan window)
    {
        var deadline = DateTime.UtcNow + window;
        while (DateTime.UtcNow < deadline)
        {
            _lines.AddRange(await _tail.NewLinesAsync().ConfigureAwait(false));
            await Task.Delay(SmokeTimings.LogPollInterval).ConfigureAwait(false);
        }
        _lines.AddRange(await _tail.NewLinesAsync().ConfigureAwait(false));
    }
}

/// <summary>
/// Everything a scenario is handed. Built once by the runner, after the profile is guarded, the
/// catcher is listening, the webhook swap has been read back, and the app has been seen to answer on
/// the pipe.
/// </summary>
public sealed class ScenarioContext
{
    public required ProfileGuard Guard { get; init; }

    public required WebhookCatcher Catcher { get; init; }

    /// <summary>The consented reporter — <see cref="SmokePluginIds.Reporting"/>.</summary>
    public required MetricReporter Reporter { get; init; }

    /// <summary>Where the app's Serilog files are. Derived from the guard's data root.</summary>
    public required string LogDirectory { get; init; }

    public required Action<string> Log { get; init; }

    /// <summary>
    /// True when <see cref="AlertDestination.Phone"/> is in the metric-breach destination set, which the
    /// runner does once it has replaced <c>notify.dat</c> with an unconfigured record AND read that back.
    /// False means the replacement could not be confirmed, so Phone was left out and the fallback row
    /// skips — routing it against live credentials would page a real phone.
    /// </summary>
    public required bool PhoneRouted { get; init; }

    /// <summary>
    /// An account id the host can resolve to a name, or null when it could not get one. Only the
    /// masked-naming row needs it, and only that row skips without it — every other scenario uses a
    /// fresh random subject on purpose (see <see cref="FreshSubject"/>).
    /// </summary>
    public string? ResolvableSubject { get; set; }

    /// <summary>The masked display name the host reports for <see cref="ResolvableSubject"/>. Held in
    /// memory, compared, and never printed or logged.</summary>
    public string? MaskedName { get; set; }

    /// <summary>
    /// A new subject id for every scenario, and this is a correctness requirement rather than hygiene.
    /// The cooldown is keyed by (account, kind): one shared subject would mean the first delivered
    /// breach suppressed every later scenario's for five minutes, turning eight rows green for the
    /// wrong reason and red for the rest. A fresh Guid gives each scenario its own cooldown slot and
    /// its own sample series.
    /// <para>
    /// Every subject the harness invents is therefore one RoRoRo has no record of, which is what the
    /// "unrecognised subject_id" row is about — that row is distinguished by what it ASSERTS, not by a
    /// setup only it has. The harness cannot manufacture a recognised one; the naming row is the only
    /// one that needs it and it asks the host (see <see cref="ResolvableSubject"/>).
    /// </para>
    /// </summary>
    public string FreshSubject() => Guid.NewGuid().ToString();

    /// <summary>
    /// Every <see cref="LogWatch"/> the scenario in flight opened. The runner clears this before each
    /// scenario and reads <see cref="DispatchFailures"/> after it, which is what makes the
    /// swallowed-dispatch check impossible for a new scenario to forget: the row does not opt in, it
    /// just opens its watch the way every row already does.
    /// </summary>
    private readonly List<LogWatch> _watches = [];

    public LogWatch WatchLog()
    {
        var watch = LogWatch.Open(LogDirectory);
        _watches.Add(watch);
        return watch;
    }

    /// <summary>Called by the runner before each scenario.</summary>
    public void BeginScenario() => _watches.Clear();

    /// <summary>Swallowed dispatch failures seen by any watch the scenario in flight opened.</summary>
    public int DispatchFailures => _watches.Sum(w => w.DispatchFailures);

    public Task ReportAsync(string subjectId, string metricId, double value)
        => Reporter.ReportAsync(subjectId, metricId, value, DateTimeOffset.UtcNow);

    public Task ReportAsync(string subjectId, string metricId, double value, TimeSpan ago)
        => Reporter.ReportAsync(subjectId, metricId, value, DateTimeOffset.UtcNow - ago);

    public Task WriteRulesAsync(IReadOnlyList<MetricRuleRow> rows)
        => Guard.WriteRulesAsync(SmokeRules.ToJson(rows));

    public Task WriteCanonicalRulesAsync() => WriteRulesAsync(SmokeRules.Canonical);

    /// <summary>Takes whatever the catcher already holds and throws it away, so a scenario's drain
    /// cannot pick up a post a previous scenario's alert produced late.</summary>
    public Task ClearCatcherAsync() => Catcher.DrainAsync(TimeSpan.Zero);
}

/// <summary>
/// The sixteen scenarios, in run order.
/// <para>
/// <b>Order is not arbitrary.</b> The pipe probe runs first because everything after it is meaningless
/// if the host is not answering. The opt-in row runs LAST because it is the only one that needs the
/// gate off, and picking the setting up costs two of the app's 30-second ticks each way — leaving the
/// gate off at the end costs nothing, since the guard restores the user's value regardless.
/// </para>
/// <para>
/// <b>Sixteen scenarios, fourteen rows.</b> Two rows carry two cases each — granted-then-revoked
/// versus never-declared, and live pickup versus absence — and each case is its own scenario because
/// each is its own path through the code. <c>ScenarioTableTests</c> checks the mapping in both
/// directions rather than the count alone, so neither a new scenario naming nothing nor a marked row
/// nobody covers can pass.
/// </para>
/// </summary>
public static class ScenarioTable
{
    private const string LocalDestination = nameof(AlertDestination.Local);
    private const string MineDestination = nameof(AlertDestination.Mine);
    private const string ClanDestination = nameof(AlertDestination.Clan);
    private const string PhoneDestination = nameof(AlertDestination.Phone);

    public static IReadOnlyList<SmokeScenario> All { get; } =
    [
        new("pipe-binds",
            "The plugin pipe still binds with the new RPC present.",
            PipeBindsAsync),
        new("consented-report-accepted",
            "A consented plugin can report at all.",
            ConsentedReportAcceptedAsync),
        new("denied-after-revoke",
            "An unconsented plugin is denied.",
            DeniedAfterRevokeAsync),
        new("denied-never-declared",
            "An unconsented plugin is denied.",
            DeniedNeverDeclaredAsync),
        new("breach-reaches-toast",
            "A breach reaches the desktop toast.",
            BreachReachesToastAsync),
        new("value-renders-legibly",
            "The observed value renders legibly.",
            ValueRendersLegiblyAsync),
        new("unrecognised-subject",
            "An unrecognised subject_id still reaches the user.",
            UnrecognisedSubjectAsync),
        new("clock-skew-visible",
            "A clock-skewed reporter is visible, not silent.",
            ClockSkewVisibleAsync),
        new("counter-reset-no-false-breach",
            "A resetting cumulative counter does not fire a false rate breach.",
            CounterResetAsync),
        new("repeated-breaches-one-toast",
            "Repeated breaches do not become repeated toasts.",
            RepeatedBreachesAsync),
        new("rules-picked-up-live",
            "The rules file is picked up live, and its absence is inert.",
            RulesPickedUpLiveAsync),
        new("rules-absence-inert",
            "The rules file is picked up live, and its absence is inert.",
            RulesAbsenceInertAsync),
        new("malformed-rules-survivable",
            "A malformed rules file does not take the app down.",
            MalformedRulesAsync),
        new("streamer-mode-masks",
            "Streamer mode masks the metric alert.",
            StreamerModeMasksAsync),
        new("unconfigured-destination-falls-back",
            "An unconfigured destination falls back to the desktop toast.",
            UnconfiguredDestinationFallsBackAsync),
        new("opt-in-gates-it",
            "The opt-in setting actually gates it.",
            OptInGatesItAsync),
    ];

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The ungated <c>GetHostInfo</c> answered, which is the whole row: a capability-map entry missing
    /// for the new RPC faults the bind task, is logged at Debug, and leaves plugins off for the entire
    /// session without crashing or showing anything.
    /// </summary>
    private static async Task<ScenarioOutcome> PipeBindsAsync(ScenarioContext ctx)
    {
        return await ctx.Reporter.IsHostReachableAsync().ConfigureAwait(false)
            ? ScenarioOutcome.Pass($"GetHostInfo answered on {ctx.Reporter.PipeName}")
            : ScenarioOutcome.Fail(
                $"GetHostInfo did not answer on {ctx.Reporter.PipeName}. Plugins are off for this "
                + "session — check the log at Debug for a faulted plugin-host bind.");
    }

    /// <summary>
    /// A consented report is accepted rather than refused. Reported against
    /// <see cref="SmokeMetrics.Accepted"/>, which has no rule on purpose: the claim is about the RPC,
    /// and raising an alert to prove it would spend a cooldown and a toast on the wrong question.
    /// </summary>
    private static async Task<ScenarioOutcome> ConsentedReportAcceptedAsync(ScenarioContext ctx)
    {
        try
        {
            await ctx.ReportAsync(ctx.FreshSubject(), SmokeMetrics.Accepted, 1).ConfigureAwait(false);
            return ScenarioOutcome.Pass($"ReportMetric accepted for {SmokePluginIds.Reporting}");
        }
        catch (RpcException ex)
        {
            return ScenarioOutcome.Fail(
                $"ReportMetric was refused with {ex.StatusCode}. The consent grant for "
                + $"{SmokePluginIds.Reporting} is not in force.");
        }
    }

    /// <summary>
    /// The first denial path: a capability granted and then taken away. Proves the grant was live
    /// BEFORE revoking it, because a PermissionDenied against an id that never had the capability in
    /// force proves nothing about revocation — it is the other row.
    /// </summary>
    private static async Task<ScenarioOutcome> DeniedAfterRevokeAsync(ScenarioContext ctx)
    {
        await ctx.Guard.GrantConsentAsync(SmokePluginIds.Revoked, [PluginCapability.HostMetricsReport])
            .ConfigureAwait(false);

        using var reporter = new MetricReporter(SmokePluginIds.Revoked);
        try
        {
            await reporter.ReportAsync(ctx.FreshSubject(), SmokeMetrics.Accepted, 1, DateTimeOffset.UtcNow)
                .ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            return ScenarioOutcome.Fail(
                $"the grant this row revokes never took effect ({ex.StatusCode}), so the denial below "
                + "would have proven nothing.");
        }

        await ctx.Guard.RevokeConsentAsync(SmokePluginIds.Revoked).ConfigureAwait(false);
        return await AssertDeniedAsync(ctx, reporter, SmokePluginIds.Revoked).ConfigureAwait(false);
    }

    /// <summary>The second denial path: an id that never declared or was granted anything. Absence is
    /// denial, and nothing is written to <c>consent.dat</c> for this id at all.</summary>
    private static async Task<ScenarioOutcome> DeniedNeverDeclaredAsync(ScenarioContext ctx)
    {
        using var reporter = new MetricReporter(SmokePluginIds.Undeclared);
        return await AssertDeniedAsync(ctx, reporter, SmokePluginIds.Undeclared).ConfigureAwait(false);
    }

    /// <summary>
    /// Shared by both denial rows: the call is refused with <see cref="StatusCode.PermissionDenied"/>
    /// AND no alert reaches the host.
    /// <para>
    /// Both halves, because they are different failures. A gate that refused the caller while still
    /// recording the report would look identical from the plugin's side — which is exactly what the
    /// smoke row says to check. <see cref="SmokeMetrics.Denied"/> carries a rule that a breaching value
    /// WOULD fire, so the silence is attributable to the denial rather than to nothing being
    /// configured.
    /// </para>
    /// </summary>
    private static async Task<ScenarioOutcome> AssertDeniedAsync(
        ScenarioContext ctx, MetricReporter reporter, string pluginId)
    {
        var watch = ctx.WatchLog();
        try
        {
            await reporter.ReportAsync(
                ctx.FreshSubject(), SmokeMetrics.Denied, SmokeRules.BreachingLevel, DateTimeOffset.UtcNow)
                .ConfigureAwait(false);
            return ScenarioOutcome.Fail(
                $"{pluginId} holds no grant yet ReportMetric was ACCEPTED. Absence must be denial.");
        }
        catch (RpcException ex) when (ex.StatusCode != StatusCode.PermissionDenied)
        {
            return ScenarioOutcome.Fail($"expected PermissionDenied for {pluginId}, got {ex.StatusCode}.");
        }
        catch (RpcException)
        {
            // PermissionDenied — the first half. Now the second.
        }

        await watch.SettleAsync(SmokeTimings.AlertWindow).ConfigureAwait(false);
        var delivered = watch.DeliveredFor(SmokeMetrics.Denied);
        return delivered.Count == 0
            ? ScenarioOutcome.Pass($"PermissionDenied for {pluginId}, and no alert was recorded")
            : ScenarioOutcome.Fail(
                $"{pluginId} was denied at the RPC but the host still raised {delivered.Count} alert(s) "
                + $"for {SmokeMetrics.Denied} — the report was recorded anyway.");
    }

    /// <summary>
    /// A Rate rule, two samples a minute apart, a real rate of one per minute against a floor of a
    /// hundred. The first report cannot breach (one sample makes no rate — unknown is not zero), which
    /// is why the row wants two.
    /// </summary>
    private static async Task<ScenarioOutcome> BreachReachesToastAsync(ScenarioContext ctx)
    {
        var subject = ctx.FreshSubject();
        var watch = ctx.WatchLog();

        await ctx.ReportAsync(subject, SmokeMetrics.Toast, 0, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
        await ctx.ReportAsync(subject, SmokeMetrics.Toast, 1).ConfigureAwait(false);

        // The whole window, no early exit, because "ONE line" is half a negative: leaving the moment the
        // first arrives would assert the absence of a second after watching for milliseconds. The first
        // report of the pair cannot breach (one sample makes no rate), so a second line would mean the
        // history or the window is not behaving as the rule says.
        await watch.SettleAsync(SmokeTimings.AlertWindow).ConfigureAwait(false);

        var count = watch.DeliveredFor(SmokeMetrics.Toast, LocalDestination).Count;
        return count switch
        {
            1 => ScenarioOutcome.Pass("exactly one 'Alert → Local' for a rate under the floor"),
            0 => ScenarioOutcome.Fail(
                $"no 'Alert → Local' for {SmokeMetrics.Toast} in {Describe(SmokeTimings.AlertWindow)}. "
                + "The opt-in gate, the rules file, and the metric-breach destination set are the three "
                + "things that can swallow this."),
            _ => ScenarioOutcome.Fail($"expected one 'Alert → Local', saw {count}."),
        };
    }

    /// <summary>
    /// The observed value has to survive to the body as <c>0.79</c>. Asserted on the POST the app
    /// actually made, because the log carries an alert's title and never its body — the reason a local
    /// webhook catcher exists at all. It rode a <c>long?</c> until 2026-09-09 and rendered "at 0" for
    /// 0.79 and for nothing alike.
    /// </summary>
    private static async Task<ScenarioOutcome> ValueRendersLegiblyAsync(ScenarioContext ctx)
    {
        await ctx.ClearCatcherAsync().ConfigureAwait(false);
        var watch = ctx.WatchLog();

        await ctx.ReportAsync(ctx.FreshSubject(), SmokeMetrics.Value, 0.79).ConfigureAwait(false);

        var arrived = await watch
            .PollForAsync(w => w.DeliveredFor(SmokeMetrics.Value, MineDestination).Count >= 1,
                SmokeTimings.AlertWindow)
            .ConfigureAwait(false);
        if (!arrived)
        {
            return ScenarioOutcome.Fail(
                $"no 'Alert → Mine' for {SmokeMetrics.Value} within {Describe(SmokeTimings.AlertWindow)}, "
                + "so there is no body to read.");
        }

        var posts = await ctx.Catcher.DrainAsync(SmokeTimings.AlertWindow).ConfigureAwait(false);
        var body = posts.FirstOrDefault(p => p.Body.Contains(SmokeMetrics.Value, StringComparison.Ordinal))?.Body;
        if (body is null)
        {
            return ScenarioOutcome.Fail(
                $"the log says the alert was sent to Mine but the catcher never received a post naming "
                + $"{SmokeMetrics.Value} ({posts.Count} post(s) drained, "
                + $"{ctx.Catcher.RequestFaults.Count} request fault(s) recorded).");
        }

        var rendered = Regex.Match(body, Regex.Escape(SmokeMetrics.Value) + @" at (?<v>[^\\""]+)");
        if (!rendered.Success)
        {
            return ScenarioOutcome.Fail(
                $"the body naming {SmokeMetrics.Value} carries no 'at <value>' reading at all.");
        }

        // Parsed, not string-compared against "0.79". WebhookPayload formats with {v:0.##} under the
        // running app's CURRENT culture, and this app ships six — fr, de, ru, pt-BR, pl, es — several of
        // which write "0,79". A literal comparison would fail this row on a localised install for a
        // formatting difference that is correct, which is the opposite of what the row is about. The bug
        // it IS about (the value riding a long? and rendering "at 0") still fails: 0 does not parse to
        // 0.79 in any culture.
        var token = rendered.Groups["v"].Value.Trim();
        if (!TryParseObserved(token, out var value))
        {
            return ScenarioOutcome.Fail($"the body reads 'at {token}', which is not a number in any culture.");
        }

        return Math.Abs(value - 0.79) < 0.0001
            ? ScenarioOutcome.Pass($"the body reads 'at {token}' — the fraction survived")
            : ScenarioOutcome.Fail($"the body reads 'at {token}', which is not 0.79.");
    }

    /// <summary>
    /// A subject id RoRoRo has no record of must still reach the user rather than vanish because a
    /// plugin guessed an id wrong. Every subject this harness invents is unrecognised, so what makes
    /// this a row of its own is the assertion, not the setup.
    /// </summary>
    private static async Task<ScenarioOutcome> UnrecognisedSubjectAsync(ScenarioContext ctx)
    {
        var watch = ctx.WatchLog();
        await ctx.ReportAsync(ctx.FreshSubject(), SmokeMetrics.Subject, SmokeRules.BreachingLevel)
            .ConfigureAwait(false);

        var arrived = await watch
            .PollForAsync(w => w.DeliveredFor(SmokeMetrics.Subject, LocalDestination).Count >= 1,
                SmokeTimings.AlertWindow)
            .ConfigureAwait(false);

        return arrived
            ? ScenarioOutcome.Pass("an alert for an account id the app has never seen still landed")
            : ScenarioOutcome.Fail(
                $"no alert for {SmokeMetrics.Subject} within {Describe(SmokeTimings.AlertWindow)} — an "
                + "unrecognised subject id appears to have been dropped rather than keyed globally.");
    }

    /// <summary>
    /// A report stamped hours ahead is dropped, and SAID SO by name. The failure this guards is
    /// asymmetric and silent: a skewed clock puts every sample outside the rate window, so Rate rules
    /// go quiet while Level and Event keep working. <see cref="SmokeMetrics.Skew"/> carries a Level rule
    /// that the clamped value would fire, so a broken skew check shows up as an alert here rather than
    /// as nothing.
    /// </summary>
    private static async Task<ScenarioOutcome> ClockSkewVisibleAsync(ScenarioContext ctx)
    {
        var watch = ctx.WatchLog();
        await ctx.Reporter.ReportAsync(
            ctx.FreshSubject(), SmokeMetrics.Skew, SmokeRules.BreachingLevel,
            DateTimeOffset.UtcNow.AddHours(3)).ConfigureAwait(false);

        // One settle for both halves rather than a poll for the drop line and then a glance at the
        // deliveries: the second half is a negative, and a negative checked the instant the positive
        // lands has watched for milliseconds of a thirty-second window.
        await watch.SettleAsync(SmokeTimings.AlertWindow).ConfigureAwait(false);

        if (!watch.SkewDrops.Contains(SmokeMetrics.Skew, StringComparer.Ordinal))
        {
            return ScenarioOutcome.Fail(
                $"nothing in the log names {SmokeMetrics.Skew} as dropped for a future stamp within "
                + $"{Describe(SmokeTimings.AlertWindow)}.");
        }

        var delivered = watch.DeliveredFor(SmokeMetrics.Skew);
        return delivered.Count == 0
            ? ScenarioOutcome.Pass($"the drop is named in the log and raised no alert")
            : ScenarioOutcome.Fail(
                $"the drop was logged but {delivered.Count} alert(s) fired for {SmokeMetrics.Skew} anyway.");
    }

    /// <summary>
    /// A cumulative counter that resets must not read as a collapse in rate. Three samples: two rising
    /// far above the floor, then one lower. The decrease makes the rate unmeasurable, and unmeasurable
    /// is not a breach.
    /// </summary>
    private static async Task<ScenarioOutcome> CounterResetAsync(ScenarioContext ctx)
    {
        var subject = ctx.FreshSubject();
        var watch = ctx.WatchLog();

        await ctx.ReportAsync(subject, SmokeMetrics.Reset, 0, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        await ctx.ReportAsync(subject, SmokeMetrics.Reset, 500, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
        await ctx.ReportAsync(subject, SmokeMetrics.Reset, 50).ConfigureAwait(false);

        await watch.SettleAsync(SmokeTimings.AlertWindow).ConfigureAwait(false);
        var delivered = watch.DeliveredFor(SmokeMetrics.Reset);
        return delivered.Count == 0
            ? ScenarioOutcome.Pass($"no alert off the apparent drop, watched for {Describe(SmokeTimings.AlertWindow)}")
            : ScenarioOutcome.Fail(
                $"{delivered.Count} alert(s) fired for {SmokeMetrics.Reset}: a counter reset read as a rate "
                + "collapse.");
    }

    /// <summary>
    /// Three breaching reports inside the cooldown, one delivery. The guarantee the whole design rests
    /// on — thresholding lives in the host precisely so a bad night cannot become forty notifications.
    /// <para>
    /// The follow-up reports wait for the first breach's LAST destination line before they are sent,
    /// which sequences around a known dispatcher race rather than testing it: the cooldown stamp lands
    /// inside the fan-out loop, after each destination's send, so two dispatches genuinely in flight
    /// together can both pass the check and both send (documented in <c>AlertDispatcher</c> and
    /// deliberately not fixed there). Racing it would make this row flaky about something it is not
    /// asserting. Phone is last in the set the runner writes, but Phone is unconfigured and dedupes into
    /// the Local already there, so Local is the last destination actually dispatched — and the loop is
    /// sequential, so its line means the first destination's iteration, stamp included, is behind us.
    /// </para>
    /// </summary>
    private static async Task<ScenarioOutcome> RepeatedBreachesAsync(ScenarioContext ctx)
    {
        var subject = ctx.FreshSubject();
        var watch = ctx.WatchLog();

        await ctx.ReportAsync(subject, SmokeMetrics.Repeat, SmokeRules.BreachingLevel).ConfigureAwait(false);

        var first = await watch
            .PollForAsync(w => w.DeliveredFor(SmokeMetrics.Repeat, LocalDestination).Count >= 1,
                SmokeTimings.AlertWindow)
            .ConfigureAwait(false);
        if (!first)
        {
            return ScenarioOutcome.Fail(
                $"the first breach never reached the desktop within "
                + $"{Describe(SmokeTimings.AlertWindow)}, so there is no cooldown to test.");
        }

        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        await ctx.ReportAsync(subject, SmokeMetrics.Repeat, SmokeRules.BreachingLevel).ConfigureAwait(false);
        await ctx.ReportAsync(subject, SmokeMetrics.Repeat, SmokeRules.BreachingLevel).ConfigureAwait(false);

        await watch.SettleAsync(SmokeTimings.AlertWindow).ConfigureAwait(false);

        var locals = watch.DeliveredFor(SmokeMetrics.Repeat, LocalDestination).Count;
        return locals == 1
            ? ScenarioOutcome.Pass("three breaches inside the cooldown, one toast")
            : ScenarioOutcome.Fail(
                $"three breaches inside the {AlertRouter.Cooldown.TotalMinutes:0}-minute cooldown produced "
                + $"{locals} 'Alert → Local' line(s), not one.");
    }

    /// <summary>
    /// A rule added while the app is running takes effect on the next report, no restart.
    /// <see cref="SmokeMetrics.Live"/> is absent from the file the app started with, so a delivery here
    /// can only mean the file was re-read.
    /// </summary>
    private static async Task<ScenarioOutcome> RulesPickedUpLiveAsync(ScenarioContext ctx)
    {
        await ctx.WriteRulesAsync(SmokeRules.WithLiveRule).ConfigureAwait(false);

        var watch = ctx.WatchLog();
        await ctx.ReportAsync(ctx.FreshSubject(), SmokeMetrics.Live, SmokeRules.BreachingLevel)
            .ConfigureAwait(false);

        var arrived = await watch
            .PollForAsync(w => w.DeliveredFor(SmokeMetrics.Live, LocalDestination).Count >= 1,
                SmokeTimings.AlertWindow)
            .ConfigureAwait(false);

        return arrived
            ? ScenarioOutcome.Pass("a rule written mid-session fired on the next report")
            : ScenarioOutcome.Fail(
                $"{SmokeMetrics.Live} was added to the rules file and the next report did not alert within "
                + $"{Describe(SmokeTimings.AlertWindow)} — the file is not being re-read.");
    }

    /// <summary>
    /// With no rules file at all, a breaching report is accepted and produces nothing. Both halves
    /// matter: "inert" means no alert AND no error — a plugin whose report started failing because the
    /// user has no rules would be a worse outcome than silence.
    /// </summary>
    private static async Task<ScenarioOutcome> RulesAbsenceInertAsync(ScenarioContext ctx)
    {
        ctx.Guard.DeleteRules();
        try
        {
            var watch = ctx.WatchLog();
            try
            {
                await ctx.ReportAsync(ctx.FreshSubject(), SmokeMetrics.Absent, SmokeRules.BreachingLevel)
                    .ConfigureAwait(false);
            }
            catch (RpcException ex)
            {
                return ScenarioOutcome.Fail(
                    $"with no rules file the report failed with {ex.StatusCode}; absence must be inert, "
                    + "not an error.");
            }

            await watch.SettleAsync(SmokeTimings.AlertWindow).ConfigureAwait(false);
            var delivered = watch.DeliveredFor(SmokeMetrics.Absent);
            if (delivered.Count > 0)
            {
                return ScenarioOutcome.Fail(
                    $"{delivered.Count} alert(s) fired for {SmokeMetrics.Absent} with no rules file present.");
            }

            return await ctx.Reporter.IsHostReachableAsync().ConfigureAwait(false)
                ? ScenarioOutcome.Pass($"accepted and silent with no rules file, watched for {Describe(SmokeTimings.AlertWindow)}")
                : ScenarioOutcome.Fail("the host stopped answering while there was no rules file.");
        }
        finally
        {
            // Every later scenario needs the rules back, and the guard's restore is about the USER's
            // file, not about keeping the run working.
            await ctx.WriteCanonicalRulesAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A rules file truncated mid-object costs the user their rules, never their session. Then a valid
    /// file works again — which is the half that proves the app did not cache the failure forever.
    /// </summary>
    private static async Task<ScenarioOutcome> MalformedRulesAsync(ScenarioContext ctx)
    {
        await ctx.Guard.WriteRulesAsync(SmokeRules.MalformedJson).ConfigureAwait(false);
        try
        {
            var watch = ctx.WatchLog();
            try
            {
                await ctx.ReportAsync(ctx.FreshSubject(), SmokeMetrics.Malformed, SmokeRules.BreachingLevel)
                    .ConfigureAwait(false);
            }
            catch (RpcException ex)
            {
                return ScenarioOutcome.Fail(
                    $"a malformed rules file made ReportMetric fail with {ex.StatusCode}. The rules are "
                    + "supposed to be what is lost, not the plugin host.");
            }

            await watch.SettleAsync(SmokeTimings.AlertWindow).ConfigureAwait(false);
            if (watch.DeliveredFor(SmokeMetrics.Malformed).Count > 0)
            {
                return ScenarioOutcome.Fail(
                    $"an alert fired for {SmokeMetrics.Malformed} while the rules file was unparseable.");
            }

            if (!await ctx.Reporter.IsHostReachableAsync().ConfigureAwait(false))
            {
                return ScenarioOutcome.Fail("the plugin host stopped answering after a malformed rules file.");
            }
        }
        finally
        {
            await ctx.WriteCanonicalRulesAsync().ConfigureAwait(false);
        }

        var recovered = ctx.WatchLog();
        await ctx.ReportAsync(ctx.FreshSubject(), SmokeMetrics.Malformed, SmokeRules.BreachingLevel)
            .ConfigureAwait(false);

        var arrived = await recovered
            .PollForAsync(w => w.DeliveredFor(SmokeMetrics.Malformed, LocalDestination).Count >= 1,
                SmokeTimings.AlertWindow)
            .ConfigureAwait(false);

        return arrived
            ? ScenarioOutcome.Pass("survived the truncated file, and a valid one worked again after it")
            : ScenarioOutcome.Fail(
                "the host survived the malformed file but a valid file written after it never took effect "
                + $"within {Describe(SmokeTimings.AlertWindow)} — the parse failure looks cached.");
    }

    /// <summary>
    /// The personal channel gets the masked name, the clan channel the real one. Asserted without ever
    /// learning the real name: the harness knows the MASKED name (the host hands plugins that one, by
    /// design) and checks that the Mine body carries it, the Clan body does not, and the two bodies
    /// differ.
    /// <para>
    /// Needs a subject the host can resolve to an account. <c>App.ResolveAlertNames</c> answers an
    /// unknown id with a pair of empty strings, which would make both bodies identical and this row
    /// vacuous — so without one it SKIPS rather than passing on nothing.
    /// </para>
    /// </summary>
    private static async Task<ScenarioOutcome> StreamerModeMasksAsync(ScenarioContext ctx)
    {
        if (ctx.ResolvableSubject is null || string.IsNullOrEmpty(ctx.MaskedName))
        {
            return ScenarioOutcome.Skip(
                "no account the host could resolve to a name. An unknown subject id resolves to a pair of "
                + "empty names, which makes the personal and clan bodies identical and this row vacuous.");
        }

        await ctx.ClearCatcherAsync().ConfigureAwait(false);
        var watch = ctx.WatchLog();

        await ctx.ReportAsync(ctx.ResolvableSubject, SmokeMetrics.Streamer, SmokeRules.BreachingLevel)
            .ConfigureAwait(false);

        var arrived = await watch
            .PollForAsync(w => w.DeliveredFor(SmokeMetrics.Streamer, ClanDestination).Count >= 1,
                SmokeTimings.AlertWindow)
            .ConfigureAwait(false);
        if (!arrived)
        {
            return ScenarioOutcome.Fail(
                $"no 'Alert → Clan' for {SmokeMetrics.Streamer} within {Describe(SmokeTimings.AlertWindow)}, "
                + "so there are no two bodies to compare.");
        }

        // Filtered by metric id as well as path, not by path alone. Every alert kind routed to Mine posts
        // to the same URL and carries the same masked name, so an unrelated drop-out or memory warning
        // arriving in this window would satisfy all three assertions below with a pair of posts that has
        // nothing to do with this scenario — the row would go green having read someone else's alert.
        var posts = await ctx.Catcher.DrainAsync(SmokeTimings.AlertWindow).ConfigureAwait(false);
        var ours = posts.Where(p => p.Body.Contains(SmokeMetrics.Streamer, StringComparison.Ordinal)).ToList();
        var mine = ours.FirstOrDefault(p => p.Path == WebhookCatcher.MinePath)?.Body;
        var clan = ours.FirstOrDefault(p => p.Path == WebhookCatcher.ClanPath)?.Body;
        if (mine is null || clan is null)
        {
            return ScenarioOutcome.Fail(
                $"expected a post naming {SmokeMetrics.Streamer} on both {WebhookCatcher.MinePath} and "
                + $"{WebhookCatcher.ClanPath}; got {ours.Count} matching of {posts.Count} drained, "
                + $"{ctx.Catcher.RequestFaults.Count} request fault(s).");
        }

        // The masked name goes through the same JSON escaping the app's own payload does, so a name
        // carrying a non-ASCII character still matches.
        var masked = JsonEncoded(ctx.MaskedName);

        if (!mine.Contains(masked, StringComparison.Ordinal))
        {
            return ScenarioOutcome.Fail(
                "the personal channel's body does not carry the masked name the host reports for this "
                + "account. Streamer mode may not be active in the running app — it is read once at "
                + "startup, so the app has to be started AFTER the setting is written.");
        }

        if (clan.Contains(masked, StringComparison.Ordinal))
        {
            return ScenarioOutcome.Fail(
                "the clan channel's body carries the MASKED name. The clan room is the one destination "
                + "meant to get the real one; a board of invented names is useless in the one place the "
                + "information is meant to be shared.");
        }

        return string.Equals(mine, clan, StringComparison.Ordinal)
            ? ScenarioOutcome.Fail("the personal and clan bodies are identical — no name policy was applied.")
            : ScenarioOutcome.Pass("personal body masked, clan body not, and the two differ");
    }

    /// <summary>
    /// A destination that is routed but not configured falls back to the desktop toast rather than
    /// vanishing. Phone is the one destination the harness can leave unconfigured while both webhooks
    /// are live, and the assertion bites precisely because Phone IS in the destination set: if the
    /// router believed it configured, the log would say <c>Alert → Phone</c>, and it must not.
    /// <para>
    /// <b>Half of this row is out of reach in one run, and saying so is better than implying otherwise.</b>
    /// Local is also routed — the desktop row needs the shipped default exercised — so the alert would
    /// land whether or not Phone fell back, and "instead of vanishing" is therefore not what is being
    /// observed here. What IS observed is the router's <c>phoneConfigured</c> branch taking the
    /// fallback arm. Proving the vanishing counterfactual needs a destination set with no Local in it,
    /// which needs another app restart, because <c>DiscordConfigService</c> reads the file once.
    /// </para>
    /// <para>
    /// The phone is unconfigured by the runner during setup — <c>notify.dat</c> is backed up, replaced
    /// with a default record, and read back (see <see cref="ProfileGuard.UnconfigurePhoneAsync"/>). This
    /// row used to skip whenever credentials existed, which on any machine where phone alerts had been
    /// set up meant always: a row that can never run is not a covered row, whatever the table says. It
    /// still skips if that replacement could not be confirmed, because the alternative is paging a real
    /// phone to prove a fallback.
    /// </para>
    /// </summary>
    private static async Task<ScenarioOutcome> UnconfiguredDestinationFallsBackAsync(ScenarioContext ctx)
    {
        if (!ctx.PhoneRouted)
        {
            return ScenarioOutcome.Skip(
                "the phone could not be confirmed unconfigured, so Phone was left out of the metric-breach "
                + "destination set. This row needs a routed destination with no credentials behind it; it "
                + "will not page a real phone to prove a fallback.");
        }

        var watch = ctx.WatchLog();
        await ctx.ReportAsync(ctx.FreshSubject(), SmokeMetrics.Fallback, SmokeRules.BreachingLevel)
            .ConfigureAwait(false);

        // The full window, no early exit, and this row is the reason the rule exists: "Phone was not
        // routed" is the entire point of it, and leaving as soon as the Local line lands would conclude
        // that before the dispatcher could have written a Phone line at all. Local comes LAST in the
        // fan-out the runner writes, so the early exit was not even ordered in this row's favour.
        await watch.SettleAsync(SmokeTimings.AlertWindow).ConfigureAwait(false);

        if (watch.DeliveredFor(SmokeMetrics.Fallback, LocalDestination).Count == 0)
        {
            return ScenarioOutcome.Fail(
                $"nothing reached the desktop for {SmokeMetrics.Fallback} within "
                + $"{Describe(SmokeTimings.AlertWindow)}; an unconfigured destination appears to have "
                + "swallowed the alert.");
        }

        var toPhone = watch.DeliveredFor(SmokeMetrics.Fallback, PhoneDestination).Count;
        return toPhone == 0
            ? ScenarioOutcome.Pass("Phone was routed, unconfigured, and the alert landed on the desktop instead")
            : ScenarioOutcome.Fail(
                $"{toPhone} alert(s) went to Phone, so the destination was treated as configured and this "
                + "row proves nothing about the fallback.");
    }

    /// <summary>
    /// The opt-in off means nothing fires — and not because nothing was configured, which is how this
    /// feature shipped once already.
    /// <para>
    /// Both absences are asserted: no delivered line AND no routed-nowhere line. The second is what
    /// separates "the gate stopped it before a trigger existed" from "a trigger was raised and then
    /// swallowed downstream" — through plan 1 the feature was off only because the destination list was
    /// empty, which looks identical from outside unless you check this.
    /// </para>
    /// <para>
    /// Runs last, and leaves the gate off: the guard restores the user's own value, and putting it back
    /// mid-run would cost another two of the app's ticks for nothing.
    /// </para>
    /// </summary>
    private static async Task<ScenarioOutcome> OptInGatesItAsync(ScenarioContext ctx)
    {
        await ctx.Guard.SetMetricAlertsEnabledAsync(false).ConfigureAwait(false);
        ctx.Log($"  waiting {Describe(SmokeTimings.SettingsPickup)} for the app to re-read the opt-in "
            + $"(its own tick is {Describe(SmokeTimings.AppRoutineTick)})");
        await Task.Delay(SmokeTimings.SettingsPickup).ConfigureAwait(false);

        var watch = ctx.WatchLog();
        try
        {
            await ctx.ReportAsync(ctx.FreshSubject(), SmokeMetrics.Gate, SmokeRules.BreachingLevel)
                .ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            return ScenarioOutcome.Fail(
                $"with the opt-in off the report failed with {ex.StatusCode}. The gate is a host-side "
                + "decision; the RPC must still be accepted.");
        }

        await watch.SettleAsync(SmokeTimings.AlertWindow).ConfigureAwait(false);

        var delivered = watch.DeliveredFor(SmokeMetrics.Gate);
        if (delivered.Count > 0)
        {
            return ScenarioOutcome.Fail(
                $"{delivered.Count} alert(s) fired for {SmokeMetrics.Gate} with the opt-in off.");
        }

        if (watch.RoutedNowhere > 0)
        {
            return ScenarioOutcome.Fail(
                $"no alert was delivered, but {watch.RoutedNowhere} 'routed nowhere' line(s) appeared — a "
                + "trigger was raised and swallowed downstream rather than stopped by the gate. (An "
                + "unrelated alert kind with no destination configured would also land here; rerun to "
                + "tell them apart.)");
        }

        return ScenarioOutcome.Pass(
            $"nothing raised and nothing routed for {Describe(SmokeTimings.AlertWindow)} with the opt-in off");
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>JSON-escapes a string the way <c>DiscordWebhookSender</c>'s serializer does, so a
    /// captured body can be searched for it literally.</summary>
    private static string JsonEncoded(string value) => JsonSerializer.Serialize(value).Trim('"');

    /// <summary>
    /// Reads a number the app rendered, in whatever culture the app is running in. Invariant first
    /// (what an en install writes), then the harness's own culture, then a comma-for-point swap — which
    /// covers the shipped comma-decimal languages even when the harness process and the app disagree
    /// about culture, as they will when the app's UI language was chosen in Settings.
    /// </summary>
    public static bool TryParseObserved(string token, out double value)
    {
        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
        if (double.TryParse(token, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) return true;
        return double.TryParse(
            token.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string Describe(TimeSpan span) =>
        span.TotalSeconds < 90 ? $"{span.TotalSeconds:0}s" : $"{span.TotalMinutes:0.#}m";
}
