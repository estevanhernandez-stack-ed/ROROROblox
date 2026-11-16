using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// v1.39 item 7. The free-memory axis can only cross, latch and warn while the watchdog is counting
/// at least one client. 30 days of one PC's logs showed 547 headroom crossings, 219 of them with
/// "0 client(s) holding 0 MB": a tray badge for a squeeze RoRoRo did not cause and cannot relieve.
/// </summary>
public class MemoryWatchdogHeadroomClientGateTests
{
    private const long Gb = 1024L * 1024 * 1024;
    private const long Mb = 1024L * 1024;

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public readonly List<(LogLevel Level, string Message)> Entries = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? ex,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, ex)));
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        public void Advance(TimeSpan d) => UtcNow += d;
    }

    private sealed class FakeProcessMemory : IProcessMemoryProbe
    {
        public readonly Dictionary<int, long?> Readings = new();
        public bool TryReadPrivateBytes(int pid, out long privateBytes)
        {
            privateBytes = 0;
            if (!Readings.TryGetValue(pid, out var v) || v is null) return false;
            privateBytes = v.Value;
            return true;
        }
    }

    private sealed class FakeSystemMemory : ISystemMemoryProbe
    {
        public long Available = 20L * Gb;
        public bool TryRead(out long total, out long available)
        {
            total = 32L * Gb; available = Available;
            return true;
        }
    }

    private static (MemoryWatchdog wd, FakeProcessMemory proc, FakeSystemMemory sys, FakeClock clock,
        CapturingLogger<MemoryWatchdog> log, Func<int> fires) Build(long available)
    {
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var sys = new FakeSystemMemory { Available = available };
        var log = new CapturingLogger<MemoryWatchdog>();
        var wd = new MemoryWatchdog(proc, sys, clock, log) { CapBytes = 0, ReserveBytes = 2 * Gb };
        var fires = 0;
        wd.PressureCrossed += (_, _) => fires++;
        return (wd, proc, sys, clock, log, () => fires);
    }

    private static bool Warned(CapturingLogger<MemoryWatchdog> log)
        => log.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("memory headroom crossed"));

    [Fact]
    public void ZeroClients_UnderReserve_NoCrossing_NoWarningLog_AndIsClear()
    {
        var (wd, _, _, _, log, fires) = Build(available: 1 * Gb);

        wd.Sample();
        wd.Sample();

        Assert.Equal(0, fires());
        Assert.False(Warned(log));
        Assert.True(MemoryPressureEvaluator.IsClear(wd.GetSnapshot(), wd.ProjectionWarnMinutes));
    }

    [Fact]
    public void OneClient_UnderReserve_CrossesOnceAndStaysLatched()
    {
        var (wd, proc, _, _, log, fires) = Build(available: 1 * Gb);
        proc.Readings[10] = 2 * Gb;
        wd.OnAccountLaunched(Guid.NewGuid(), 10);

        wd.Sample();
        Assert.Equal(1, fires());
        Assert.True(Warned(log));
        Assert.False(MemoryPressureEvaluator.IsClear(wd.GetSnapshot(), wd.ProjectionWarnMinutes));

        wd.Sample();
        Assert.Equal(1, fires());
    }

    [Fact]
    public void LastClientExits_MemoryStillLow_NextSampleIsClear_ThenALaterClientCrossesAgain()
    {
        var (wd, proc, _, _, _, fires) = Build(available: 1 * Gb);
        var first = Guid.NewGuid();
        proc.Readings[10] = 2 * Gb;
        wd.OnAccountLaunched(first, 10);
        wd.Sample();
        Assert.Equal(1, fires());
        Assert.False(MemoryPressureEvaluator.IsClear(wd.GetSnapshot(), wd.ProjectionWarnMinutes));

        wd.OnAccountExited(first, 10);
        wd.Sample();
        Assert.True(MemoryPressureEvaluator.IsClear(wd.GetSnapshot(), wd.ProjectionWarnMinutes));
        Assert.Equal(1, fires());

        // A client launched into the still-low machine crosses on its first sample: the zero-client
        // sample released the latch.
        var second = Guid.NewGuid();
        proc.Readings[11] = 2 * Gb;
        wd.OnAccountLaunched(second, 11);
        wd.Sample();
        Assert.Equal(2, fires());
        Assert.False(MemoryPressureEvaluator.IsClear(wd.GetSnapshot(), wd.ProjectionWarnMinutes));
    }

    [Fact]
    public void OneClient_ReArmDeadbandStillApplies()
    {
        var (wd, proc, sys, _, _, fires) = Build(available: 1 * Gb);
        proc.Readings[10] = 2 * Gb;
        wd.OnAccountLaunched(Guid.NewGuid(), 10);
        wd.Sample();
        Assert.Equal(1, fires());

        sys.Available = 2 * Gb + 100 * Mb;   // over the reserve, inside the 15% deadband
        wd.Sample();
        sys.Available = 1 * Gb;
        wd.Sample();
        Assert.Equal(1, fires());
    }

    [Fact]
    public void FootprintLearner_StillDiscardsUnderReserveSamples_AndLearnsWhenHealthy()
    {
        // The learner is fed the RAW below-reserve reading. With zero clients there is no settled
        // sample to feed at all, so the only observable seam is: a client under the reserve teaches
        // nothing, the same client on a healthy machine does.
        long Learned(long available)
        {
            var (wd, proc, _, clock, _, _) = Build(available);
            proc.Readings[10] = 2000 * Mb;
            wd.OnAccountLaunched(Guid.NewGuid(), 10);
            wd.Sample();
            clock.Advance(MemoryWatchdog.MinimumObservation);
            for (var i = 0; i < ClientFootprintLearner.MinimumSamples + 2; i++)
            {
                wd.Sample();
                clock.Advance(TimeSpan.FromSeconds(30));
            }
            return wd.ExpectedClientMb;
        }

        Assert.Equal(ClientFootprintLearner.SeedMb, Learned(available: 1 * Gb));
        Assert.Equal(2000, Learned(available: 20 * Gb));
    }
}
