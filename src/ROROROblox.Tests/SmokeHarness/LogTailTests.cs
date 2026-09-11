using ROROROblox.Core.Discord;
using ROROROblox.MetricSmoke;

namespace ROROROblox.Tests.SmokeHarness;

/// <summary>
/// <see cref="LogTail"/> turns the app's own Serilog file into pass/fail for most of the smoke
/// harness's 16 rows, so a loose matcher here is worse than no harness at all — it would turn
/// ordinary log noise into a green run. Every rendered line below is copied from the actual
/// LogInformation call sites in <c>AlertDispatcher.cs</c> and <c>MetricReportSinkAdapter.cs</c>,
/// wrapped in the prefix <c>AppLogging.Configure</c>'s outputTemplate actually produces
/// (timestamp, level, version, SourceContext), not simplified strings a looser test would still
/// pass against.
/// </summary>
public sealed class LogTailTests : IDisposable
{
    private const string DeliveredLine =
        "2026-09-11 14:22:03.512 -07:00 [INF] v1.25.0.0 ROROROblox.App.Discord.AlertDispatcher "
        + "Alert → Mine: BaronBloxwell dropped out (1 account(s)).";

    private const string DeliveredLineWithPunctuationInTitle =
        "2026-09-11 14:22:04.001 -07:00 [INF] v1.25.0.0 ROROROblox.App.Discord.AlertDispatcher "
        + "Alert → Clan: 3 accounts — Pet Simulator 99! (3 account(s)).";

    // Derived from AlertRouter.Cooldown rather than restated as a literal number: this line has to
    // stay true to what AlertDispatcher.DispatchAsync actually renders, and a hand-typed number
    // here would go stale silently the moment that constant changes (it already had — this used to
    // hardcode "15-minute" against a 5-minute constant).
    private static readonly string RoutedNowhereLine =
        "2026-09-11 14:22:05.777 -07:00 [INF] v1.25.0.0 ROROROblox.App.Discord.AlertDispatcher "
        + "Alert raised for 2 account(s) but routed nowhere — check the destination, the "
        + $"per-account mute, and the {AlertRouter.Cooldown.TotalMinutes}-minute cooldown.";

    private const string SkewDropLine =
        "2026-09-11 14:22:06.114 -07:00 [INF] v1.25.0.0 ROROROblox.App.Plugins.Adapters.MetricReportSinkAdapter "
        + "Dropped a metric report for cpu.load stamped 00:00:45.1230000 in the future — check the "
        + "reporter's clock; it is sending local time, not UTC.";

    // MetricReportSinkAdapter.Report never validates metricId beyond
    // string.IsNullOrWhiteSpace — nothing stops a reporter using one with a space in it, even
    // though the shipped convention is dotted identifiers.
    private const string SkewDropLineWithSpaceInMetricId =
        "2026-09-11 14:22:06.500 -07:00 [INF] v1.25.0.0 ROROROblox.App.Plugins.Adapters.MetricReportSinkAdapter "
        + "Dropped a metric report for fps p1 stamped 00:00:12.0000000 in the future — check the "
        + "reporter's clock; it is sending local time, not UTC.";

    // The failure mode the plan calls out by name: a line that merely mentions "alert" in ordinary
    // prose, nowhere near either template's shape.
    private const string NoiseLineMentioningAlert =
        "2026-09-11 14:22:07.000 -07:00 [INF] v1.25.0.0 ROROROblox.App.Settings.SettingsViewModel "
        + "User clicked the Alert button in Settings.";

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rororo-logtail-{Guid.NewGuid():N}.log");

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void WriteLines(params string[] lines)
        => File.WriteAllText(_path, string.Join("", lines.Select(l => l + "\r\n")));

    private void AppendLines(params string[] lines)
        => File.AppendAllText(_path, string.Join("", lines.Select(l => l + "\r\n")));

    [Fact]
    public async Task NewLinesAsync_ReturnsOnlyLinesAppendedAfterTheRecordedOffset()
    {
        WriteLines("line already there before the scenario started");

        var tail = LogTail.OpenAt(_path);

        // Written before OpenAt captured the length — must never come back.
        AppendLines(DeliveredLine);

        var first = await tail.NewLinesAsync();
        Assert.Equal([DeliveredLine], first);

        // A second call only sees what landed after the first call's read.
        AppendLines(RoutedNowhereLine);
        var second = await tail.NewLinesAsync();
        Assert.Equal([RoutedNowhereLine], second);
    }

    [Fact]
    public async Task NewLinesAsync_WithNothingAppended_ReturnsEmpty()
    {
        WriteLines("whatever was already logged");
        var tail = LogTail.OpenAt(_path);

        Assert.Empty(await tail.NewLinesAsync());
    }

    [Fact]
    public void Delivered_ParsesDestinationTitleAndAccountCount()
    {
        var tail = LogTail.OpenAt(_path);

        var delivered = tail.Delivered([DeliveredLine]);

        var alert = Assert.Single(delivered);
        Assert.Equal("Mine", alert.Destination);
        Assert.Equal("BaronBloxwell dropped out", alert.Title);
        Assert.Equal(1, alert.AccountCount);
    }

    [Fact]
    public void Delivered_ParsesATitleThatItselfContainsAnEmDashAndPunctuation()
    {
        // WebhookPayload.ForAlert composes titles with "—" and can carry a game name after it
        // (e.g. the MetricBreach kind); the greedy match up to the final "(N account(s))." must
        // still land on the real suffix, not stop at the first punctuation it sees.
        var tail = LogTail.OpenAt(_path);

        var delivered = tail.Delivered([DeliveredLineWithPunctuationInTitle]);

        var alert = Assert.Single(delivered);
        Assert.Equal("Clan", alert.Destination);
        Assert.Equal("3 accounts — Pet Simulator 99!", alert.Title);
        Assert.Equal(3, alert.AccountCount);
    }

    [Fact]
    public void RoutedNowhere_IsCountedButNeverAlsoReadAsDelivered()
    {
        var tail = LogTail.OpenAt(_path);
        var lines = new[] { DeliveredLine, RoutedNowhereLine };

        // The two verdicts are opposites. Conflating them would invert a smoke row's result, so
        // each recogniser must see only its own line out of the pair.
        Assert.Equal(1, tail.RoutedNowhereCount(lines));
        var delivered = Assert.Single(tail.Delivered(lines));
        Assert.Equal("Mine", delivered.Destination);
    }

    [Fact]
    public void RoutedNowhereCount_DoesNotCountADeliveredLine()
    {
        var tail = LogTail.OpenAt(_path);

        Assert.Equal(0, tail.RoutedNowhereCount([DeliveredLine]));
    }

    [Fact]
    public void Delivered_DoesNotMatchARoutedNowhereLine()
    {
        var tail = LogTail.OpenAt(_path);

        Assert.Empty(tail.Delivered([RoutedNowhereLine]));
    }

    [Fact]
    public void SkewDrops_NamesTheMetricIdOnAFutureDatedReport()
    {
        var tail = LogTail.OpenAt(_path);

        var drops = tail.SkewDrops([SkewDropLine]);

        Assert.Equal(["cpu.load"], drops);
    }

    [Fact]
    public void SkewDrops_DoesNotTruncateAMetricIdThatContainsASpace()
    {
        // The old `\S+` capture stopped at the first whitespace, which would have silently
        // truncated "fps p1" down to "fps". metricId is opaque and unvalidated past a
        // whitespace-only check, so a space in it is unlikely by convention but not impossible.
        var tail = LogTail.OpenAt(_path);

        var drops = tail.SkewDrops([SkewDropLineWithSpaceInMetricId]);

        Assert.Equal(["fps p1"], drops);
    }

    [Fact]
    public void ALineThatMerelyMentionsTheWordAlert_IsNotMatchedByAnyRecogniser()
    {
        // The failure mode the plan is explicit about: a matcher firing on the word "alert"
        // anywhere in the file would turn ordinary log noise into a green run.
        var tail = LogTail.OpenAt(_path);
        var lines = new[] { NoiseLineMentioningAlert };

        Assert.Empty(tail.Delivered(lines));
        Assert.Equal(0, tail.RoutedNowhereCount(lines));
        Assert.Empty(tail.SkewDrops(lines));
    }

    [Fact]
    public async Task NewLinesAsync_AfterTheFileRollsAndShrinks_DoesNotThrowAndReadsTheNewContentOnly()
    {
        // Deliberately much longer, in bytes, than SkewDropLine below — the point being tested is
        // that the recorded offset ends up PAST the end of the replacement file, not merely close.
        WriteLines(new string('x', 4096));

        var tail = LogTail.OpenAt(_path);

        // Simulate a day roll / truncation: the file at this path is now shorter than the offset
        // OpenAt captured.
        WriteLines(SkewDropLine);

        var lines = await tail.NewLinesAsync();

        Assert.Equal([SkewDropLine], lines);
        Assert.Equal(["cpu.load"], tail.SkewDrops(lines));

        // And it keeps working normally afterwards — the reset did not leave the offset broken.
        AppendLines(DeliveredLine);
        Assert.Equal([DeliveredLine], await tail.NewLinesAsync());
    }

    [Fact]
    public async Task OpenAt_OnAFileThatDoesNotExistYet_StartsAtZero_AndSeesEverythingOnceItIsCreated()
    {
        Assert.False(File.Exists(_path));
        var tail = LogTail.OpenAt(_path);

        Assert.Empty(await tail.NewLinesAsync());

        WriteLines(DeliveredLine);
        Assert.Equal([DeliveredLine], await tail.NewLinesAsync());
    }

    [Fact]
    public async Task NewLinesAsync_WhileAnotherHandleHoldsTheFileOpenSharedReadWrite_DoesNotThrow_AndDoesNotBlockThatWriter()
    {
        // AppLogging.Configure opens the log with `shared: true`, so the app's own handle stays
        // open, ReadWrite-shared, for the life of the process. This is the concurrency case: LogTail
        // must open without demanding exclusive access, and must not prevent the "app" (this held
        // handle) from continuing to append.
        WriteLines("seed line before anyone opens a second handle");
        var tail = LogTail.OpenAt(_path);

        using var appHandle = new FileStream(
            _path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        appHandle.Seek(0, SeekOrigin.End);
        var bytes = System.Text.Encoding.UTF8.GetBytes(DeliveredLine + "\r\n");
        await appHandle.WriteAsync(bytes);
        await appHandle.FlushAsync();

        // LogTail's open must not throw a sharing-violation IOException against that live handle.
        var lines = await tail.NewLinesAsync();
        Assert.Equal([DeliveredLine], lines);

        // And the "app" can still write more afterward — LogTail did not leave a lock behind.
        var moreBytes = System.Text.Encoding.UTF8.GetBytes(RoutedNowhereLine + "\r\n");
        await appHandle.WriteAsync(moreBytes);
        await appHandle.FlushAsync();

        Assert.Equal([RoutedNowhereLine], await tail.NewLinesAsync());
    }

    [Fact]
    public async Task NewLinesAsync_WhileAWriterKeepsAppendingConcurrently_NeverReplaysOrLosesALine()
    {
        // Regression for a real bug: NewLinesAsync used to stamp its offset from the file length
        // observed BEFORE ReadToEndAsync ran, not from where the read actually stopped. The app
        // appends to this exact file for the whole life of a scenario — that is the entire premise
        // of tailing it live — so when bytes land while a read is still in flight, ReadToEndAsync
        // (which loops until it truly hits end-of-file) sweeps them up too, and the OLD code then
        // recorded an offset that undercounted how far the read had actually gone. The next call
        // re-read and re-returned the tail it had already handed back. A delivered line getting
        // double-counted is not cosmetic — it is exactly what would turn the "repeated breaches do
        // not become repeated toasts" scenario's "exactly one" assertion into a false failure.
        // The prior concurrency test only writes strictly before and after a read and cannot catch
        // this; this one keeps writing throughout a tight read loop so some write lands mid-read.
        WriteLines("seed line before the race starts");
        var tail = LogTail.OpenAt(_path);

        const int totalWrites = 300;
        var writer = Task.Run(async () =>
        {
            for (var i = 0; i < totalWrites; i++)
            {
                AppendLines(DeliveredLine);
                await Task.Yield();
            }
        });

        var totalSeen = 0;
        while (!writer.IsCompleted)
        {
            totalSeen += tail.Delivered(await tail.NewLinesAsync()).Count;
        }
        await writer;
        // Drain whatever landed between the writer's last append and this method's last read.
        totalSeen += tail.Delivered(await tail.NewLinesAsync()).Count;

        Assert.Equal(totalWrites, totalSeen);
    }
}
