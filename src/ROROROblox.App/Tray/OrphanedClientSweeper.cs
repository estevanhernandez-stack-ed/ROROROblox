using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.App.Tray;

/// <summary>
/// Finds Roblox clients nobody owns and gives them back to the account they belong to (F-103).
/// <para>
/// <see cref="RunningRobloxScanner.Scan"/> already enumerates every <c>RobloxPlayerBeta</c> and
/// already identifies the untagged ones — but it runs exactly once, at startup
/// (<c>App.xaml.cs</c>). Nothing looks again for the rest of the session, so a client Roblox
/// restarts for itself after an update is never found: it counts toward the running total and the
/// memory watchdog while no account row owns it, and the row's Stop cannot reach it.
/// </para>
/// <para>
/// This is the driver that was missing. It does no attribution of its own — that is
/// <see cref="ClientSuccession"/>, which is pure and refuses when the answer would be a guess.
/// </para>
/// </summary>
internal sealed class OrphanedClientSweeper : IDisposable
{
    private const string PlayerProcessName = "RobloxPlayerBeta";

    /// <summary>
    /// Slower than the decorator's 1.5s title tick on purpose. Enumerating the process table is not
    /// free, and a self-restart is not a sub-second event — the succession window is 90s, so
    /// checking every few seconds finds it with room to spare.
    /// </summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

    private readonly IClock _clock;
    private readonly ILogger<OrphanedClientSweeper> _log;
    private readonly Func<IReadOnlyList<ClientSnapshot>> _enumerate;
    private readonly Func<int, bool> _isTracked;
    private readonly Action<Guid, int> _adopt;

    private readonly List<ClientSuccession.Exit> _recentExits = [];
    private readonly object _exitsLock = new();
    private Timer? _timer;
    private int _sweeping;
    private bool _disposed;

    /// <summary>One live client as the sweeper needs to see it. Title decides whether it is ours.</summary>
    internal readonly record struct ClientSnapshot(int Pid, DateTimeOffset StartedAt, string? Title);

    /// <param name="isTracked">Whether the tracker already owns this pid — a tracked client is not an orphan.</param>
    /// <param name="adopt">Attach the pid to the account and re-decorate. Called only for unambiguous attributions.</param>
    /// <param name="enumerate">Live clients. Injected so the sweep is testable without a process table.</param>
    public OrphanedClientSweeper(
        IClock clock,
        Func<int, bool> isTracked,
        Action<Guid, int> adopt,
        Func<IReadOnlyList<ClientSnapshot>>? enumerate = null,
        ILogger<OrphanedClientSweeper>? log = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _isTracked = isTracked ?? throw new ArgumentNullException(nameof(isTracked));
        _adopt = adopt ?? throw new ArgumentNullException(nameof(adopt));
        _enumerate = enumerate ?? DefaultEnumerate;
        _log = log ?? NullLogger<OrphanedClientSweeper>.Instance;
    }

    /// <summary>
    /// Record a client leaving. The tracker raises this with the account that owned it, which is the
    /// only reason a successor can be attributed at all — the replacement process carries nothing
    /// linking it back to an account.
    /// </summary>
    public void OnClientExited(Guid accountId, int pid)
    {
        lock (_exitsLock)
        {
            _recentExits.Add(new ClientSuccession.Exit(accountId, pid, _clock.UtcNow));
            PruneStale();
        }
    }

    public void Start()
    {
        if (_disposed) return;
        _timer ??= new Timer(_ => SafeSweep(), null, SweepInterval, SweepInterval);
    }

    private void SafeSweep()
    {
        if (Interlocked.Exchange(ref _sweeping, 1) == 1) return;
        try { Sweep(); }
        catch (Exception ex) { _log.LogDebug(ex, "Orphan sweep threw; will retry next tick."); }
        finally { Interlocked.Exchange(ref _sweeping, 0); }
    }

    /// <summary>Internal seam so the sweep can be driven deterministically in tests.</summary>
    internal void Sweep()
    {
        var now = _clock.UtcNow;

        List<ClientSuccession.Exit> exits;
        lock (_exitsLock)
        {
            PruneStale();
            exits = [.. _recentExits];
        }

        var orphans = _enumerate()
            .Where(c => !_isTracked(c.Pid))
            // An untagged title is the signal. A client we decorated reads "Roblox - {name}", and
            // that title is what RunningRobloxScanner re-attaches by; a bare "Roblox" is the
            // replacement Roblox started for itself.
            .Where(c => !string.IsNullOrWhiteSpace(c.Title) && !RobloxWindowTitle.Pattern.IsMatch(c.Title!))
            .Select(c => new ClientSuccession.Orphan(c.Pid, c.StartedAt))
            .ToList();

        if (orphans.Count == 0) return;

        var attributions = ClientSuccession.Attribute(exits, orphans, now);

        foreach (var (pid, accountId) in attributions)
        {
            _log.LogInformation(
                "Adopted orphaned client pid {Pid} for account {AccountId} — Roblox restarted it and the window had lost its title.",
                pid, accountId);
            try { _adopt(accountId, pid); }
            catch (Exception ex) { _log.LogWarning(ex, "Adopting pid {Pid} failed; it stays unowned.", pid); }

            lock (_exitsLock)
            {
                // One exit yields one successor. Leaving it in would let a second orphan claim the
                // same predecessor on a later tick.
                var used = _recentExits.FindIndex(e => e.AccountId == accountId);
                if (used >= 0) _recentExits.RemoveAt(used);
            }
        }

        // THE WATCH. F-095, F-099, F-101 and F-103 were four lifetime defects in a row, every one
        // found by a human and none by an instrument — and the app had been printing this exact
        // symptom at Debug the whole time, where nobody reads it. Anything still unowned after a
        // sweep gets said out loud.
        var unowned = orphans.Count - attributions.Count;
        if (unowned > 0)
        {
            _log.LogWarning(
                "{Count} Roblox client(s) running that no account owns. They count toward the running total "
                + "and the memory watchdog, but no row can Stop them. Pids: {Pids}",
                unowned,
                string.Join(", ", orphans.Select(o => o.Pid).Except(attributions.Select(a => a.Pid))));
        }
    }

    private void PruneStale()
    {
        var cutoff = _clock.UtcNow - ClientSuccession.Window;
        _recentExits.RemoveAll(e => e.At < cutoff);
    }

    private static IReadOnlyList<ClientSnapshot> DefaultEnumerate()
    {
        var result = new List<ClientSnapshot>();
        Process[] procs;
        try { procs = Process.GetProcessesByName(PlayerProcessName); }
        catch { return result; }

        foreach (var p in procs)
        {
            try
            {
                if (p.HasExited) continue;
                p.Refresh();
                result.Add(new ClientSnapshot(p.Id, new DateTimeOffset(p.StartTime.ToUniversalTime(), TimeSpan.Zero), p.MainWindowTitle));
            }
            catch { /* exited mid-read, or access denied — not an orphan we can act on */ }
            finally { p.Dispose(); }
        }
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Dispose();
        _timer = null;
    }
}
