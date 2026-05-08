using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// Unit tests for <see cref="StartupGate.ShouldProceed"/> — the probe-driven shutdown
/// decision behind the cycle-4 hard-block startup modal. 5 cases per spec §6. All fakes
/// hand-rolled (zero new dependencies — same pattern as <see cref="JoinByLinkSaveTests"/>).
/// </summary>
public class StartupGateTests
{
    [Fact]
    public void ShouldProceed_ProbeReturnsEmpty_ReturnsTrueWithNoWarning()
    {
        var probe = new FakeRobloxRunningProbe { NextResult = Array.Empty<int>() };
        var logger = new ListLogger<StartupGate>();
        var gate = new StartupGate(probe, logger);

        var result = gate.ShouldProceed();

        Assert.True(result);
        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void ShouldProceed_ProbeReturnsOnePid_ReturnsFalseAndLogsInfoWithPid()
    {
        var probe = new FakeRobloxRunningProbe { NextResult = new[] { 4242 } };
        var logger = new ListLogger<StartupGate>();
        var gate = new StartupGate(probe, logger);

        var result = gate.ShouldProceed();

        Assert.False(result);
        Assert.Empty(logger.Warnings);
        var info = Assert.Single(logger.Information);
        Assert.Contains("4242", info.Message);
    }

    [Fact]
    public void ShouldProceed_ProbeReturnsMultiplePids_ReturnsFalseAndLogsInfoWithAllPids()
    {
        var probe = new FakeRobloxRunningProbe { NextResult = new[] { 1111, 2222, 3333 } };
        var logger = new ListLogger<StartupGate>();
        var gate = new StartupGate(probe, logger);

        var result = gate.ShouldProceed();

        Assert.False(result);
        Assert.Empty(logger.Warnings);
        var info = Assert.Single(logger.Information);
        Assert.Contains("1111", info.Message);
        Assert.Contains("2222", info.Message);
        Assert.Contains("3333", info.Message);
    }

    [Fact]
    public void ShouldProceed_ProbeThrowsInvalidOperation_FailsOpenAndLogsWarning()
    {
        var probe = new FakeRobloxRunningProbe { NextThrow = new InvalidOperationException("snapshot mid-enum") };
        var logger = new ListLogger<StartupGate>();
        var gate = new StartupGate(probe, logger);

        var result = gate.ShouldProceed();

        Assert.True(result); // fail-open
        var warning = Assert.Single(logger.Warnings);
        Assert.NotNull(warning.Exception);
        Assert.IsType<InvalidOperationException>(warning.Exception);
    }

    [Fact]
    public void ShouldProceed_ProbeThrowsUnexpectedType_FailsOpenAndLogsWarning()
    {
        // Defensive — Process.GetProcessesByName surfaces Win32 errors as a variety of types
        // depending on OS state. Any non-IOE throw must still result in fail-open.
        var probe = new FakeRobloxRunningProbe { NextThrow = new ApplicationException("unexpected shape") };
        var logger = new ListLogger<StartupGate>();
        var gate = new StartupGate(probe, logger);

        var result = gate.ShouldProceed();

        Assert.True(result); // fail-open
        var warning = Assert.Single(logger.Warnings);
        Assert.NotNull(warning.Exception);
        Assert.IsType<ApplicationException>(warning.Exception);
    }

    // ---- fakes ----

    private sealed class FakeRobloxRunningProbe : IRobloxRunningProbe
    {
        public IReadOnlyList<int> NextResult { get; set; } = Array.Empty<int>();
        public Exception? NextThrow { get; set; }

        public IReadOnlyList<int> GetRunningPlayerPids()
        {
            if (NextThrow is not null) throw NextThrow;
            return NextResult;
        }
    }

    /// <summary>
    /// Minimal logger that captures entries by level so tests can assert on count, message,
    /// and attached exception. Same shape as the capture-fakes used in similar test suites.
    /// </summary>
    private sealed class ListLogger<T> : ILogger<T>
    {
        public record Entry(LogLevel Level, string Message, Exception? Exception);

        public List<Entry> Entries { get; } = new();
        public IEnumerable<Entry> Information => Entries.Where(e => e.Level == LogLevel.Information);
        public IEnumerable<Entry> Warnings => Entries.Where(e => e.Level == LogLevel.Warning);

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new Entry(logLevel, formatter(state, exception), exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
