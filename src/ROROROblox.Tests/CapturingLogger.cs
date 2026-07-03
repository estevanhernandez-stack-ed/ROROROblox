using Microsoft.Extensions.Logging;

namespace ROROROblox.Tests;

/// <summary>
/// Test double that records every formatted log line. For asserting the
/// plugin-lifecycle evidence lines (issue #36) without touching Serilog.
/// Thread-safe: process-exit callbacks log from OS threadpool threads.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<string> _lines = new();

    public IReadOnlyList<string> Snapshot()
    {
        lock (_lines) { return _lines.ToArray(); }
    }

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_lines) { _lines.Add($"[{logLevel}] {formatter(state, exception)}"); }
    }
}
