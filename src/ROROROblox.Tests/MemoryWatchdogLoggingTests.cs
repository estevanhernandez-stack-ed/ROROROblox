using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

public class MemoryWatchdogLoggingTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public readonly List<(LogLevel Level, string Message)> Entries = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? ex,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, ex)));
    }

    // Fakes duplicated per-file so each test file stands alone — see Task 3.
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
        public bool TryRead(out long total, out long available)
        {
            total = 32L * Gb; available = 20L * Gb;
            return true;
        }
    }

    [Fact]
    public void Summary_EmitsEvery15Minutes_NotEveryTick()
    {
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var log = new CapturingLogger<MemoryWatchdog>();
        var wd = new MemoryWatchdog(proc, new FakeSystemMemory(), clock, log) { CapBytes = 0 };

        proc.Readings[10] = 2 * Gb;
        wd.OnAccountLaunched(Guid.NewGuid(), 10);

        // 30 ticks at 30s = 15 minutes of wall clock.
        for (var i = 0; i < 30; i++)
        {
            wd.Sample();
            clock.Advance(TimeSpan.FromSeconds(30));
        }

        var summaries = log.Entries.Count(e => e.Level == LogLevel.Information && e.Message.Contains("memory"));
        Assert.InRange(summaries, 1, 2); // once or twice, never 30 times
    }

    [Fact]
    public void CapCrossing_LogsWarningOncePerCrossing()
    {
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var log = new CapturingLogger<MemoryWatchdog>();
        var wd = new MemoryWatchdog(proc, new FakeSystemMemory(), clock, log) { CapBytes = 4 * Gb };

        proc.Readings[10] = 5 * Gb;
        wd.OnAccountLaunched(Guid.NewGuid(), 10);
        wd.Sample();
        wd.Sample();
        wd.Sample();

        Assert.Single(log.Entries.Where(e => e.Level == LogLevel.Warning));
    }
}
