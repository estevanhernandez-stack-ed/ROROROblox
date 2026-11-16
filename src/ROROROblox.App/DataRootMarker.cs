using Microsoft.Extensions.Logging;
using ROROROblox.Core;

namespace ROROROblox.App;

/// <summary>One startup log line: its level and its finished text.</summary>
internal sealed record DataRootLogLine(LogLevel Level, string Message);

/// <summary>
/// What a run on another data folder says about itself. Pure: the wording lives here so a test
/// pins it without a running app, and <c>App.OnStartup</c> and <c>MainWindow</c> only apply it.
/// <para>
/// These strings are a developer's label on a dev build (<c>RORORO_DATA_DIR</c> is ignored by
/// shipped builds), so they are deliberately not localized and not in the string catalogs. They
/// are built in C#, not XAML, and reach the window through a non-literal assignment, which is
/// why the prose and brand fences do not see them.
/// </para>
/// </summary>
internal static class DataRootMarker
{
    /// <summary>Appended to both window titles under the override.</summary>
    internal const string TitleTag = " [test data]";

    /// <summary>
    /// The line that follows "ROROROblox starting": a Warning naming the folder when the override
    /// is in use, an Information line saying why when the variable was set and ignored, null when
    /// it was never set. The rejected raw value is not echoed: it is whatever the caller typed.
    /// </summary>
    internal static DataRootLogLine? LogLine(DataRootDecision decision) => decision.Reason switch
    {
        DataRootDecisionReason.Used => new DataRootLogLine(
            LogLevel.Warning,
            $"{AppDataRoot.EnvironmentVariableName} is in use: this run keeps its data in {decision.Folder}, " +
            "and leaves the machine-wide registrations (join links, run on login) alone."),
        DataRootDecisionReason.NotADevBuild => new DataRootLogLine(
            LogLevel.Information,
            $"{AppDataRoot.EnvironmentVariableName} is set but ignored: only a dev build honors it."),
        DataRootDecisionReason.NotAnAbsolutePath => new DataRootLogLine(
            LogLevel.Information,
            $"{AppDataRoot.EnvironmentVariableName} is set but ignored: it is not a full absolute path."),
        _ => null,
    };

    /// <summary>The window-title tag, or null when the override is not in use.</summary>
    internal static string? TitleSuffix(DataRootDecision decision) =>
        decision.Reason == DataRootDecisionReason.Used ? TitleTag : null;

    /// <summary>The title with the tag applied when the override is in use; otherwise unchanged.</summary>
    internal static string? Tagged(string? title, DataRootDecision decision) =>
        TitleSuffix(decision) is { } tag ? title + tag : title;

    /// <summary>Writes <see cref="LogLine"/> if there is one. Message text is passed as an argument, never as a template.</summary>
    internal static void Log(ILogger log, DataRootDecision decision)
    {
        if (LogLine(decision) is { } line)
        {
            log.Log(line.Level, "{DataRootNotice}", line.Message);
        }
    }
}
