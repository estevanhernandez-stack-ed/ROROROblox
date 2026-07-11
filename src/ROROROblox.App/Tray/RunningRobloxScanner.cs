using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.ViewModels;
using ROROROblox.Core.Diagnostics;
using ROROROblox.Core.StreamerMode;

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
/// Matching parses the <c>"Roblox - {name}"</c> title <see cref="RobloxWindowDecorator"/> writes
/// and resolves each account's expected title name through the SAME
/// <see cref="RobloxWindowTitle.ResolveName"/> the decorator uses — so a LocalName nickname or an
/// active streamer-mode fake name still re-attaches. The <see cref="IStreamerIdentityProvider"/>
/// must be initialized before this runs (it is: <c>App.OnStartup</c> awaits streamer-mode init
/// before the fire-and-forget startup-checks that call <see cref="Scan"/>). Untagged windows
/// (manually launched outside ROROROblox, or pre-decorator builds) are reported as "unmatched" so
/// the caller can pause auto-launch defensively.
/// </summary>
internal sealed class RunningRobloxScanner
{
    private const string PlayerProcessName = "RobloxPlayerBeta";

    private readonly ILogger<RunningRobloxScanner> _log;
    private readonly IStreamerIdentityProvider? _identity;

    public RunningRobloxScanner(IStreamerIdentityProvider? identity = null, ILogger<RunningRobloxScanner>? log = null)
    {
        _identity = identity;
        _log = log ?? NullLogger<RunningRobloxScanner>.Instance;
    }

    /// <summary>
    /// Find the account whose current window-title name equals <paramref name="parsedTitleName"/>,
    /// resolving each candidate's title name the SAME way <see cref="RobloxWindowDecorator"/> writes
    /// it (streamer-mode fake when active, else <c>LocalName ?? DisplayName</c>) via
    /// <see cref="RobloxWindowTitle.ResolveName"/>. Matching on raw <c>DisplayName</c> here — as the
    /// scanner did before v1.10 — silently drops re-attach for nickname and streamer-mode windows.
    /// Static + internal so the match rule is unit-testable without a live process (Scan itself
    /// enumerates the OS process table and can't be exercised in a unit test).
    /// </summary>
    internal static AccountSummary? MatchAccountByTitleName(
        IReadOnlyList<AccountSummary> accounts, string parsedTitleName, IStreamerIdentityProvider? identity)
        => accounts.FirstOrDefault(a => string.Equals(
            RobloxWindowTitle.ResolveName(identity, a.Id, a.LocalName ?? a.DisplayName, a.AvatarUrl),
            parsedTitleName,
            StringComparison.OrdinalIgnoreCase));

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

                var m = RobloxWindowTitle.Pattern.Match(title);
                if (!m.Success)
                {
                    // Title doesn't match our sentinel — likely launched outside ROROROblox or
                    // by a pre-decorator build. Caller still knows there's a Roblox running.
                    _log.LogDebug("Untagged Roblox process pid {Pid} title='{Title}'.", p.Id, title);
                    unmatched++;
                    p.Dispose();
                    continue;
                }

                var parsedName = m.Groups[1].Value;
                var account = MatchAccountByTitleName(accounts, parsedName, _identity);
                if (account is null)
                {
                    _log.LogDebug("Tagged Roblox window '{ParsedName}' but no matching account (streamer/local-name-aware).", parsedName);
                    unmatched++;
                    p.Dispose();
                    continue;
                }

                // AttachExisting handles HasExited / wrong-process / claim-collision internally.
                // We don't dispose p before the attach — tracker takes ownership of its own
                // Process instance. Dispose ours separately.
                if (tracker.AttachExisting(account.Id, p.Id))
                {
                    _log.LogInformation("Re-attached pid {Pid} to account {AccountId} (title '{ParsedName}').",
                        p.Id, account.Id, parsedName);
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
