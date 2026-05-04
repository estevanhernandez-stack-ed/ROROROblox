namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Snapshots the app's environment for the Diagnostics window + support bundle. Read-only;
/// every value here is something a tester or support helper would ask for first when triaging
/// a "it doesn't work" report.
/// </summary>
public interface IDiagnosticsCollector
{
    Task<DiagnosticsSnapshot> CollectAsync(CancellationToken ct = default);
}

public sealed record DiagnosticsSnapshot(
    string AppVersion,
    string DotNetVersion,
    string OsVersion,
    string RobloxInstalledVersion,
    bool RobloxInstalled,
    string WebView2Version,
    bool WebView2Installed,
    int AccountCount,
    int LiveProcessCount,
    string MultiInstanceState,
    string LogDirectory,
    string DataDirectory,
    DateTimeOffset CapturedAtUtc);
