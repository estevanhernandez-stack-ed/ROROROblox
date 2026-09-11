using Grpc.Core;
using ROROROblox.App.Plugins;
using ROROROblox.PluginContract;
using ROROROblox.Tests;

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

    // Construction helper modelled on PluginHostServiceTests.cs: the ctor has eleven required
    // parameters that this suite does not exercise, so the stubs below are the same no-op/empty
    // shapes that file already uses for the same purpose, copied here rather than reinvented
    // (they are private to that class and not otherwise reachable from this one).
    private static PluginHostService BuildService(IMetricReportSink? sink) => new(
        new InMemoryRegistry(Array.Empty<InstalledPlugin>()),
        "1.4.0",
        "1.0",
        new FakeHostStateProvider("Off"),
        new FakeRunningAccountsProvider(Array.Empty<RunningAccountSnapshot>()),
        new InProcessPluginEventBus(),
        new FakeLaunchInvoker(),
        new PluginUITranslator(new FakeUIHost()),
        new FakeActivitySnapshotProvider(Array.Empty<AccountActivitySnapshot>()),
        new FakeActivityMarker(),
        new FakeAccountStopper(),
        metricSink: sink);

    private static FakeServerCallContext TestContext() => FakeServerCallContext.Create();

    private sealed class InMemoryRegistry : IInstalledPluginsLookup
    {
        private readonly List<InstalledPlugin> _plugins;
        public InMemoryRegistry(IEnumerable<InstalledPlugin> plugins) { _plugins = plugins.ToList(); }
        public InstalledPlugin? FindById(string id) => _plugins.FirstOrDefault(p => p.Manifest.Id == id);
    }

    private sealed class FakeHostStateProvider : IPluginHostStateProvider
    {
        public FakeHostStateProvider(string state) { MultiInstanceState = state; }
        public string MultiInstanceState { get; }
        public bool MultiInstanceEnabled => MultiInstanceState == "On";
    }

    private sealed class FakeRunningAccountsProvider : IRunningAccountsProvider
    {
        private readonly List<RunningAccountSnapshot> _snapshots;
        public FakeRunningAccountsProvider(IEnumerable<RunningAccountSnapshot> snapshots) { _snapshots = snapshots.ToList(); }
        public IReadOnlyList<RunningAccountSnapshot> Snapshot() => _snapshots;
    }

    private sealed class FakeLaunchInvoker : IPluginLaunchInvoker
    {
        public Task<(bool ok, string? failureReason, int processId)> RequestLaunchAsync(string accountId)
            => Task.FromResult<(bool, string?, int)>((true, null, 0));

        public Task<(bool ok, string? failureReason, int processId)> RequestLaunchTargetAsync(
            string accountId, string? shareUrl, long? followUserId)
            => Task.FromResult<(bool, string?, int)>((true, null, 0));

        public Task<CurrentServerInfo?> GetCurrentServerAsync() => Task.FromResult<CurrentServerInfo?>(null);
    }

    private sealed class FakeActivitySnapshotProvider : IActivitySnapshotProvider
    {
        private readonly List<AccountActivitySnapshot> _snapshots;
        public FakeActivitySnapshotProvider(IEnumerable<AccountActivitySnapshot> snapshots) { _snapshots = snapshots.ToList(); }
        public IReadOnlyList<AccountActivitySnapshot> Snapshot() => _snapshots;
    }

    private sealed class FakeActivityMarker : IAccountActivityMarker
    {
        public void Mark(string accountId) { }
    }

    private sealed class FakeAccountStopper : IPluginAccountStopper
    {
        public IReadOnlyList<string> TrackedAccountIds => Array.Empty<string>();
        public bool StopAccount(string accountId) => false;
    }

    private sealed class FakeUIHost : IPluginUIHost
    {
        public string AddTrayMenuItem(string p, string l, string? t, bool e, Action c) => string.Empty;
        public string AddRowBadge(string p, string t, string? c, string? tt) => string.Empty;
        public string AddStatusPanel(string p, string t, string b) => string.Empty;
        public void Update(string h, string l) { }
        public void Remove(string h) { }
    }
}
