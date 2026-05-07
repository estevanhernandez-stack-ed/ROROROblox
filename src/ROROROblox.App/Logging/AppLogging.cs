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
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ROROROblox",
        "logs");

    public static string LogFilePath { get; } = Path.Combine(LogDirectory, "rororoblox-.log");

    private static SerilogLoggerFactory? _factory;

    public static ILoggerFactory Configure()
    {
        Directory.CreateDirectory(LogDirectory);

        var serilogLogger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            // Microsoft.Extensions.Http logs the full request URL at Information for every POST.
            // For the DiscordWebhook named client that URL contains the webhook secret token —
            // anyone with the URL can post to the channel. Suppress those categories so the
            // credential never ends up in our log file. Our own DiscordWebhookService log
            // statements already use RedactHost() for the explicit "host only" line.
            // Both LogicalHandler + ClientHandler categories matter; cover both.
            .MinimumLevel.Override("System.Net.Http.HttpClient.DiscordWebhook.LogicalHandler", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http.HttpClient.DiscordWebhook.ClientHandler", LogEventLevel.Warning)
            .Enrich.WithProperty("App", "ROROROblox")
            .WriteTo.File(
                path: LogFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 25 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}",
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
