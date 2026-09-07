using System.Collections.Generic;

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

// Carries DATA, not prose (the Core string boundary, localization Phase D 2026-09-07): the
// version fields are null when the probe found nothing — the App renders that absence as "not
// detected" (localized in the UI panel, English in the support bundle). MultiInstanceHeld is the
// held/not-held fact; the App turns it into "ON"/"OFF" the same two ways. Core emits no sentence.
public sealed record DiagnosticsSnapshot(
    string AppVersion,
    string DotNetVersion,
    string OsVersion,
    string? RobloxInstalledVersion,
    bool RobloxInstalled,
    string? WebView2Version,
    bool WebView2Installed,
    int AccountCount,
    int LiveProcessCount,
    bool MultiInstanceHeld,
    string LogDirectory,
    string DataDirectory,
    DateTimeOffset CapturedAtUtc,
    long TotalPhysicalMemoryBytes,
    long AvailablePhysicalMemoryBytes,
    IReadOnlyList<AccountMemory> AccountMemory);
