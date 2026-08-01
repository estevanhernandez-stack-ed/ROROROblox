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

    // Coordinator review (2026-08-01): the aggregate-only summary line cannot answer "which of
    // my N clients is the one ballooning" -- the whole stated point of the task. This test fails
    // if the per-account payload is dropped from the summary line, unlike
    // Summary_EmitsEvery15Minutes_NotEveryTick which passes with or without it.
    [Fact]
    public void Summary_IncludesPerAccountBreakdown_NotJustAggregate()
    {
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var log = new CapturingLogger<MemoryWatchdog>();
        var wd = new MemoryWatchdog(proc, new FakeSystemMemory(), clock, log) { CapBytes = 0 };

        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        proc.Readings[10] = 2 * Gb;
        proc.Readings[20] = 6 * Gb;
        wd.OnAccountLaunched(accountA, 10);
        wd.OnAccountLaunched(accountB, 20);

        wd.Sample(); // first tick: summary fires immediately (see _lastSummaryAt = MinValue).

        var summary = log.Entries.Single(e => e.Level == LogLevel.Information && e.Message.Contains("memory:"));
        var shortA = accountA.ToString("N")[..8];
        var shortB = accountB.ToString("N")[..8];

        // Distinct per-account byte counts must both be present and attributable to their own
        // account id -- an aggregate-only line ("2 client(s), ... MB/hr total") cannot produce
        // either of these substrings.
        Assert.Contains($"{shortA}:2048MB", summary.Message);
        Assert.Contains($"{shortB}:6144MB", summary.Message);
    }

    // Coordinator review, requirement 3: an unreadable account reports a stale last-known-good
    // reading (rec.LastBytes), which must never be rendered as though it were a fresh sample --
    // that would plant a false data point in the forensic artifact this task exists to build.
    [Fact]
    public void Summary_MarksUnreadableAccountStale_NotAsAFreshReading()
    {
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var log = new CapturingLogger<MemoryWatchdog>();
        var wd = new MemoryWatchdog(proc, new FakeSystemMemory(), clock, log) { CapBytes = 0 };

        var accountId = Guid.NewGuid();
        proc.Readings[10] = 3 * Gb;
        wd.OnAccountLaunched(accountId, 10);
        wd.Sample(); // fresh reading; first summary fires and carries a real value.

        proc.Readings.Remove(10); // pid goes unreadable from here on.
        clock.Advance(TimeSpan.FromMinutes(15));
        wd.Sample(); // second summary fires; account is now stale.

        var summaries = log.Entries
            .Where(e => e.Level == LogLevel.Information && e.Message.Contains("memory:"))
            .ToList();
        Assert.True(summaries.Count >= 2, "expected a summary on both the first and second tick");

        var shortId = accountId.ToString("N")[..8];
        Assert.Contains($"{shortId}:3072MB(stale)", summaries[^1].Message);
    }
}
