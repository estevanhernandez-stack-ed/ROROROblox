using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using ROROROblox.App.Updates;
using Xunit;

namespace ROROROblox.Tests.Updates;

/// <summary>
/// The service behind the header's update button. No test reaches the WinRT call: a fake
/// <see cref="IStoreUpdateProbe"/> stands in, and a <see cref="FakeTimeProvider"/> stands in for the
/// six-hour clock, so nothing here sleeps.
/// </summary>
public sealed class UpdateAvailabilityTests
{
    private sealed class FakeProbe : IStoreUpdateProbe
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public Func<Task<bool>> Answer { get; set; } = () => Task.FromResult(false);
        public SemaphoreSlim Called { get; } = new(0);

        public async Task<bool> IsUpdateWaitingAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Called.Release();
            return await Answer().ConfigureAwait(false);
        }
    }

    private sealed class CapturingLog : ILogger<UpdateAvailability>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries) Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private static readonly TimeSpan Six = TimeSpan.FromHours(6);

    private static async Task WaitForCallAsync(FakeProbe probe) =>
        Assert.True(await probe.Called.WaitAsync(TimeSpan.FromSeconds(10)), "the probe was never asked");

    [Fact]
    public void BeforeAnythingHappens_NoneIsWaiting()
    {
        using var service = new UpdateAvailability(true, new FakeProbe(), new FakeTimeProvider());

        Assert.Equal(UpdateKind.None, service.Current.Kind);
    }

    [Fact]
    public async Task APackagedInstall_WithTheStoreSayingYes_ReadsStore_AndRaisesChangedOnce()
    {
        var probe = new FakeProbe { Answer = () => Task.FromResult(true) };
        using var service = new UpdateAvailability(true, probe, new FakeTimeProvider());
        var raised = 0;
        service.Changed += (_, _) => raised++;

        await service.CheckNowAsync();
        await service.CheckNowAsync(); // same answer again: nothing changed, nothing raised

        Assert.Equal(UpdateKind.Store, service.Current.Kind);
        Assert.Null(service.Current.Version);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task APackagedInstall_WithTheStoreSayingNo_ReadsNone()
    {
        using var service = new UpdateAvailability(true, new FakeProbe(), new FakeTimeProvider());
        var raised = 0;
        service.Changed += (_, _) => raised++;

        await service.CheckNowAsync();

        Assert.Equal(UpdateKind.None, service.Current.Kind);
        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task ADirectDownloadInstall_AStagedNotification_ReadsStaged_WithTheVersion()
    {
        var probe = new FakeProbe { Answer = () => Task.FromResult(true) };
        using var service = new UpdateAvailability(false, probe, new FakeTimeProvider());
        var raised = 0;
        service.Changed += (_, _) => raised++;

        service.ReportStaged("1.40.0");

        Assert.Equal(new UpdateStatus(UpdateKind.Staged, "1.40.0"), service.Current);
        Assert.Equal(1, raised);
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public void APackagedInstall_IgnoresAStagedNotification()
    {
        using var service = new UpdateAvailability(true, new FakeProbe(), new FakeTimeProvider());

        service.ReportStaged("1.40.0");

        Assert.Equal(UpdateKind.None, service.Current.Kind);
    }

    [Fact]
    public async Task AThrowingProbe_ReadsNone_AndLogsOnceAtDebug()
    {
        var probe = new FakeProbe { Answer = () => throw new InvalidOperationException("no package identity") };
        var log = new CapturingLog();
        using var service = new UpdateAvailability(true, probe, new FakeTimeProvider(), log);

        await service.CheckNowAsync();
        await service.CheckNowAsync();
        await service.CheckNowAsync();

        Assert.Equal(UpdateKind.None, service.Current.Kind);
        Assert.Equal(3, probe.Calls);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Debug, entry.Level);
    }

    [Fact]
    public async Task AFailedRecheck_DoesNotTakeDownAnUpdateAlreadySeen()
    {
        var probe = new FakeProbe { Answer = () => Task.FromResult(true) };
        using var service = new UpdateAvailability(true, probe, new FakeTimeProvider(), new CapturingLog());
        await service.CheckNowAsync();

        probe.Answer = () => throw new System.Runtime.InteropServices.COMException("Store service off");
        await service.CheckNowAsync();

        Assert.Equal(UpdateKind.Store, service.Current.Kind);
    }

    [Fact]
    public async Task AProbeThatNeverAnswers_TimesOut_AndReadsNone()
    {
        var time = new FakeTimeProvider();
        var never = new TaskCompletionSource<bool>();
        var probe = new FakeProbe { Answer = () => never.Task };
        var log = new CapturingLog();
        using var service = new UpdateAvailability(true, probe, time, log, probeTimeout: TimeSpan.FromSeconds(30));

        var check = service.CheckNowAsync();
        await WaitForCallAsync(probe);
        time.Advance(TimeSpan.FromSeconds(31));
        await check.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(UpdateKind.None, service.Current.Kind);
        Assert.Single(log.Entries);
        never.SetResult(true); // let the abandoned call finish; it must not flip the state
        await Task.Delay(50);
        Assert.Equal(UpdateKind.None, service.Current.Kind);
    }

    [Fact]
    public async Task Start_AsksOnce_ThenAgainEverySixHours_ThroughTheInjectedClock()
    {
        var time = new FakeTimeProvider();
        var probe = new FakeProbe { Answer = () => Task.FromResult(false) };
        using var service = new UpdateAvailability(true, probe, time);

        service.Start();
        await WaitForCallAsync(probe);
        Assert.Equal(1, probe.Calls);
        await Task.Delay(100); // let that first check release its gate; an overlapping tick is skipped by design

        time.Advance(Six - TimeSpan.FromMinutes(1));
        await Task.Delay(100);
        Assert.Equal(1, probe.Calls); // not yet

        probe.Answer = () => Task.FromResult(true);
        time.Advance(TimeSpan.FromMinutes(1));
        await WaitForCallAsync(probe);
        Assert.Equal(2, probe.Calls);

        // Poll for the status: the second call has returned to the fake, the service is a beat behind.
        await UntilAsync(() => service.Current.Kind == UpdateKind.Store);
        await Task.Delay(100);

        time.Advance(Six);
        await WaitForCallAsync(probe);
        Assert.Equal(3, probe.Calls);
    }

    [Fact]
    public async Task Dispose_StopsTheClock_NoFurtherChecks()
    {
        var time = new FakeTimeProvider();
        var probe = new FakeProbe();
        var service = new UpdateAvailability(true, probe, time);
        service.Start();
        await WaitForCallAsync(probe);

        service.Dispose();
        time.Advance(Six * 3);
        await Task.Delay(100);
        await service.CheckNowAsync(); // a direct call after shutdown is also a no-op

        Assert.Equal(1, probe.Calls);
        service.Dispose(); // idempotent
    }

    [Fact]
    public async Task AnUnpackagedBuild_NeverCallsTheProbe_NotOnStart_NotOnTheClock_NotOnADirectCheck()
    {
        var time = new FakeTimeProvider();
        var probe = new FakeProbe { Answer = () => Task.FromResult(true) };
        using var service = new UpdateAvailability(false, probe, time);

        service.Start();
        time.Advance(Six * 4);
        await service.CheckNowAsync();
        await Task.Delay(100);

        Assert.Equal(0, probe.Calls);
        Assert.Equal(UpdateKind.None, service.Current.Kind);
    }

    [Fact]
    public async Task ASubscriberThatThrows_DoesNotBreakTheService()
    {
        var probe = new FakeProbe { Answer = () => Task.FromResult(true) };
        using var service = new UpdateAvailability(true, probe, new FakeTimeProvider());
        service.Changed += (_, _) => throw new InvalidOperationException("subscriber bug");

        await service.CheckNowAsync();

        Assert.Equal(UpdateKind.Store, service.Current.Kind);
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "the condition never became true");
            await Task.Delay(10);
        }
    }
}

/// <summary>
/// The real probe's contract: every failure is caught, logged once at Debug, and reads as no update.
/// The WinRT call is a constructor seam; only that call is faked.
/// </summary>
public sealed class StoreContextUpdateProbeTests
{
    private sealed class CapturingLog : ILogger
    {
        public List<LogLevel> Levels { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Levels.Add(logLevel);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    public async Task OneOrMoreUpdates_MeansWaiting(int count, bool expected)
    {
        var probe = new StoreContextUpdateProbe(_ => Task.FromResult(count));

        Assert.Equal(expected, await probe.IsUpdateWaitingAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(System.Runtime.InteropServices.COMException))]
    [InlineData(typeof(OperationCanceledException))]
    public async Task EveryFailure_ReadsAsNone_AndNeverEscapes(Type exceptionType)
    {
        var probe = new StoreContextUpdateProbe(_ => throw (Exception)Activator.CreateInstance(exceptionType)!);

        Assert.False(await probe.IsUpdateWaitingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AFailureThatKeepsHappening_IsLoggedOnce_AtDebug()
    {
        var log = new CapturingLog();
        var probe = new StoreContextUpdateProbe(_ => throw new InvalidOperationException("no identity"), log);

        for (var i = 0; i < 4; i++)
        {
            Assert.False(await probe.IsUpdateWaitingAsync(CancellationToken.None));
        }

        Assert.Equal([LogLevel.Debug], log.Levels);
    }

    [Fact]
    public async Task AFailureThatThrowsAfterAnAwait_IsAlsoCaught()
    {
        var probe = new StoreContextUpdateProbe(async _ =>
        {
            await Task.Yield();
            throw new System.Runtime.InteropServices.COMException("Store service off");
        });

        Assert.False(await probe.IsUpdateWaitingAsync(CancellationToken.None));
    }
}
