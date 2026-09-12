using ROROROblox.App.Plugins;
using ROROROblox.MetricSmoke;

namespace ROROROblox.Tests.SmokeHarness;

/// <summary>
/// <see cref="MetricReporter"/> is a gRPC client over a named pipe, built on the same connection
/// shape as <c>EndToEndContractTests.ConnectChannel</c> (the maintained harness in
/// ROROROblox.PluginTestHarness) -- so there is nothing here proving the pipe transport itself; that
/// is what the harness already covers, run after run, against a real Kestrel server.
/// <para>
/// What IS ours to prove without a live host running: the constructor stores what it was given, an
/// unreachable pipe answers <c>false</c> rather than throwing (design §4.3's open question --
/// <see cref="IsHostReachableAsync"/> is meant to be pollable from an automated run with no human
/// watching, so it cannot hang or throw), and the unix-millisecond conversion
/// <see cref="ReportAsync"/> sends over the wire is exact. A real granted-consent report reaching a
/// real running host is Task 6's job, not this one's -- this class cannot fake that host without
/// manufacturing coverage.
/// </para>
/// </summary>
public sealed class MetricReporterTests
{
    [Fact]
    public void Constructor_StoresThePipeNameAndPluginId()
    {
        using var reporter = new MetricReporter("rororo-plugin-host", "rororo.smoke");

        Assert.Equal("rororo-plugin-host", reporter.PipeName);
        Assert.Equal("rororo.smoke", reporter.PluginId);
    }

    [Fact]
    public void Constructor_WithNoPipeNameGiven_DefaultsToTheProductionPipeName()
    {
        // The fix this guards: a hand-typed literal here (or in the one-argument constructor
        // itself) would silently stop matching if PluginHostStartupService.DefaultPipeName were
        // ever renamed. Asserting equality against the production constant -- not against the
        // literal string it currently holds -- is what actually fails if only one of the two gets
        // renamed.
        using var reporter = new MetricReporter("rororo.smoke");

        Assert.Equal(PluginHostStartupService.DefaultPipeName, reporter.PipeName);
    }

    [Fact]
    public async Task IsHostReachableAsync_NoHostListeningOnThatPipe_ReturnsFalse_RatherThanThrowing()
    {
        // A pipe name nothing has ever bound. No app, no fake server -- the point of this test
        // is that the probe fails closed with a bool instead of propagating whatever exception
        // shape the connect attempt happens to produce (an IOException today; the exact type is
        // an implementation detail of NamedPipeClientStream/Grpc.Net.Client this class should not
        // have to keep pace with).
        var pipeName = $"rororo-smoke-unreachable-{Guid.NewGuid():N}";
        using var reporter = new MetricReporter(pipeName, "rororo.smoke");

        var reachable = await reporter.IsHostReachableAsync();

        Assert.False(reachable);
    }

    [Fact]
    public async Task SavedAccountsAsync_NoHostListeningOnThatPipe_ThrowsRatherThanAnsweringEmpty()
    {
        // The opposite contract from IsHostReachableAsync, and deliberately so. This one feeds the
        // masked-naming row, which SKIPS when it cannot get an account — so an unreachable host quietly
        // answering "no accounts" would look exactly like a profile with none, and the row would report
        // itself as not applicable instead of as unable to ask. It throws; the runner catches it and says
        // which of the two happened.
        var pipeName = $"rororo-smoke-unreachable-{Guid.NewGuid():N}";
        using var reporter = new MetricReporter(pipeName, "rororo.smoke");

        await Assert.ThrowsAnyAsync<Exception>(() => reporter.SavedAccountsAsync());
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1_700_000_000_000L)]
    [InlineData(1L)]
    public void ConvertToUnixMilliseconds_OnAWholeMillisecondBoundary_RoundTripsExactly(long unixMs)
    {
        var observedAt = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);

        Assert.Equal(unixMs, MetricReporter.ConvertToUnixMilliseconds(observedAt));
    }

    [Fact]
    public void ConvertToUnixMilliseconds_ExactlyOneWholeMillisecondLater_AdvancesByExactlyOne()
    {
        // 1ms == 10,000 ticks (DateTimeOffset ticks are 100ns units). This is the control case
        // for the sub-millisecond test below: a clean, exact millisecond step must land on the
        // next integer, not two ticks either side of it.
        var wholeMs = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        var oneMsLater = wholeMs.AddTicks(10_000);

        Assert.Equal(1_700_000_000_001L, MetricReporter.ConvertToUnixMilliseconds(oneMsLater));
    }

    [Fact]
    public void ConvertToUnixMilliseconds_SubMillisecondRemainder_TruncatesDown_NeverRoundsIntoTheFuture()
    {
        // 9,999 ticks is 0.9999ms past a whole millisecond -- one tick short of rolling over to
        // the next one. Rounding to nearest (or up) would land on the NEXT millisecond, and the
        // proto doc on observed_at_unix_ms says the host drops anything stamped in its own
        // future. A conversion that rounds the wrong way here could turn a perfectly valid,
        // just-observed report into a silently dropped one.
        var wholeMs = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        var almostOneMsLater = wholeMs.AddTicks(9_999);

        Assert.Equal(1_700_000_000_000L, MetricReporter.ConvertToUnixMilliseconds(almostOneMsLater));
    }

    [Fact]
    public void ConvertToUnixMilliseconds_OneTickPastMidnight_StillTruncatesDown()
    {
        // A second boundary case away from the round number above, so the truncation isn't
        // proven only at one convenient value.
        var wholeMs = DateTimeOffset.FromUnixTimeMilliseconds(1_726_000_000_001);
        var withRemainder = wholeMs.AddTicks(1);

        Assert.Equal(1_726_000_000_001L, MetricReporter.ConvertToUnixMilliseconds(withRemainder));
    }
}
