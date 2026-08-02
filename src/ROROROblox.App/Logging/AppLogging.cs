using System.IO;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace ROROROblox.App.Logging;

/// <summary>
/// Boots Serilog with a daily-rolling file sink under <c>%LOCALAPPDATA%\ROROROblox\logs\</c>
/// and hands out an <see cref="ILoggerFactory"/> that bridges to <c>ILogger&lt;T&gt;</c> for DI.
/// Local-only — log files never leave the user's machine. Cookies and ROBLOSECURITY values
/// are NEVER logged; logging code that touches secrets must redact at the call site.
/// </summary>
internal static class AppLogging
{
    private static string _logDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ROROROblox",
        "logs");

    public static string LogDirectory => _logDirectory;

    public static string LogFilePath => Path.Combine(_logDirectory, "rororoblox-.log");

    private static SerilogLoggerFactory? _factory;

    public static ILoggerFactory Configure(string version, string? logDirectoryOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(logDirectoryOverride))
        {
            _logDirectory = logDirectoryOverride;
        }
        Directory.CreateDirectory(_logDirectory);

        var serilogLogger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            // Framework namespaces flood the file at DBG — HttpClientFactory alone writes a
            // handler-cleanup pair every 10s (~90% of a 15 MB day). Warning+ keeps real
            // framework failures visible; app namespaces (ROROROblox.*) stay at Debug.
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.WithProperty("App", "ROROROblox")
            .Enrich.WithProperty("Version", version)
            .WriteTo.File(
                path: LogFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                fileSizeLimitBytes: 25 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true,
                // {Version} MUST be in the template. Enrichment alone never reaches the file sink.
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] v{Version} {SourceContext} {Message:lj}{NewLine}{Exception}",
                restrictedToMinimumLevel: LogEventLevel.Debug)
            .CreateLogger();

        Log.Logger = serilogLogger;
        _factory = new SerilogLoggerFactory(serilogLogger, dispose: true);
        return _factory;
    }

    public static void Shutdown()
    {
        _factory?.Dispose();
        Log.CloseAndFlush();
    }
}
