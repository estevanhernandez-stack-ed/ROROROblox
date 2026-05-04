using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.ViewModels;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.App.Tray;

/// <summary>
/// At app startup, scans for already-running <c>RobloxPlayerBeta.exe</c> processes and tries
/// to re-establish tracking. Two payoffs:
/// <list type="bullet">
///   <item><b>State survives ROROROblox restarts.</b> If alts are mid-game when the app
///   relaunches, their rows light up "Running" instead of falsely showing as idle.</item>
///   <item><b>Auto-launch-on-startup respects existing sessions.</b> When the main account is
///   already playing, the scanner attaches the existing window so the auto-launch logic sees
///   <see cref="AccountSummary.IsRunning"/> = true and skips the launch — preventing the
///   "starts a duplicate session and kicks me out" footgun.</item>
/// </list>
/// Matching uses the <c>"Roblox - {DisplayName}"</c> title pattern <see cref="RobloxWindowDecorator"/>
/// applies. Untagged windows (manually launched outside ROROROblox, or pre-decorator builds)
/// are reported as "unmatched" so the caller can pause auto-launch defensively.
/// </summary>
internal sealed class RunningRobloxScanner
{
    private const string PlayerProcessName = "RobloxPlayerBeta";
    // Group 1 captures the display name. Anchored to ^ so a stray "Roblox - X" appearing
    // mid-title doesn't false-match.
    private static readonly Regex TitlePattern = new(@"^Roblox\s*-\s*(.+?)\s*$", RegexOptions.Compiled);

    private readonly ILogger<RunningRobloxScanner> _log;

    public RunningRobloxScanner(ILogger<RunningRobloxScanner>? log = null)
    {
        _log = log ?? NullLogger<RunningRobloxScanner>.Instance;
    }

    public ScanResult Scan(IReadOnlyList<AccountSummary> accounts, IRobloxProcessTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(tracker);

        var matched = 0;
        var unmatched = 0;
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(PlayerProcessName);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "GetProcessesByName failed; reporting empty scan.");
            return new ScanResult(0, 0);
        }

        foreach (var p in processes)
        {
            try
            {
                if (p.HasExited)
                {
                    p.Dispose();
                    continue;
                }
                p.Refresh();
                var title = p.MainWindowTitle;
                if (string.IsNullOrWhiteSpace(title))
                {
                    // Window not up yet, or no main window. Count as unmatched for the
                    // "anything Roblox running?" gate but don't try to attach.
                    unmatched++;
                    p.Dispose();
                    continue;
                }

                var m = TitlePattern.Match(title);
                if (!m.Success)
                {
                    // Title doesn't match our sentinel — likely launched outside ROROROblox or
                    // by a pre-decorator build. Caller still knows there's a Roblox running.
                    _log.LogDebug("Untagged Roblox process pid {Pid} title='{Title}'.", p.Id, title);
                    unmatched++;
                    p.Dispose();
                    continue;
                }

                var displayName = m.Groups[1].Value;
                var account = accounts.FirstOrDefault(a =>
                    string.Equals(a.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));
                if (account is null)
                {
                    _log.LogDebug("Tagged Roblox window for '{DisplayName}' but no matching account.", displayName);
                    unmatched++;
                    p.Dispose();
                    continue;
                }

                // AttachExisting handles HasExited / wrong-process / claim-collision internally.
                // We don't dispose p before the attach — tracker takes ownership of its own
                // Process instance. Dispose ours separately.
                if (tracker.AttachExisting(account.Id, p.Id))
                {
                    _log.LogInformation("Re-attached pid {Pid} to account {AccountId} ({DisplayName}).",
                        p.Id, account.Id, displayName);
                    matched++;
                }
                p.Dispose();
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Skipping pid {Pid} during scan", p.Id);
                p.Dispose();
            }
        }

        return new ScanResult(matched, unmatched);
    }
}

/// <param name="MatchedAndAttached">Pids whose title matched a saved account and are now tracked.</param>
/// <param name="Unmatched">Pids running but with titles we don't recognize (manual launches,
/// pre-decorator sessions, pre-window-up). Caller uses this to decide whether to pause auto-launch.</param>
internal sealed record ScanResult(int MatchedAndAttached, int Unmatched)
{
    public bool AnyRobloxRunning => MatchedAndAttached + Unmatched > 0;
}
