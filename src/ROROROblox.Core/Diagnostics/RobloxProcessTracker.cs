using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Polls <see cref="Process.GetProcessesByName"/> for new <c>RobloxPlayerBeta.exe</c> processes
/// after a launch and assigns the first unclaimed one (with a <c>StartTime</c> at-or-after
/// <c>launchedAtUtc</c>) to the requesting account. FIFO across concurrent launches via a
/// per-pid claim dictionary.
/// </summary>
public sealed class RobloxProcessTracker : IRobloxProcessTracker, IForegroundAccountResolver, IDisposable
{
    private const string PlayerProcessName = "RobloxPlayerBeta";
    private const string InstallerProcessName = "RobloxPlayerInstaller";
    private static readonly TimeSpan DefaultAttachTimeout = TimeSpan.FromSeconds(30);
    // Install-aware extended deadline (v1.7.0 install-deferral rider 6). When RobloxPlayerInstaller.exe
    // is running during the attach wait, an update can delay the real RobloxPlayerBeta well past the
    // 30s base deadline. This cap is held in lockstep with the v1.6.0 AppStorageDefender's ~120s
    // identity hold (AppStorageDefender max-cap) so the two stop disagreeing — the tracker keeps
    // waiting for as long as the defender keeps defending the launching account's identity.
    private static readonly TimeSpan DefaultInstallerExtendedTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(750);
    // Process.StartTime can read a hair earlier than the wall clock we captured pre-Process.Start,
    // so allow a small grace window when comparing against launchedAtUtc.
    private static readonly TimeSpan StartTimeGrace = TimeSpan.FromSeconds(3);

    private readonly ILogger<RobloxProcessTracker> _log;
    private readonly TimeSpan _attachTimeout;
    private readonly TimeSpan _installerExtendedTimeout;
    private readonly TimeSpan _pollInterval;
    private readonly Func<bool> _isInstallerRunning;
    private readonly Func<IReadOnlyList<Process>> _candidateProcesses;
    private readonly ConcurrentDictionary<Guid, AttachedSlot> _attachedByAccount = new();
    private readonly ConcurrentDictionary<int, Guid> _claimedPidToAccount = new();
    private bool _disposed;

    public RobloxProcessTracker() : this(NullLogger<RobloxProcessTracker>.Instance) { }

    public RobloxProcessTracker(ILogger<RobloxProcessTracker> log)
        : this(
            log,
            DefaultAttachTimeout,
            DefaultInstallerExtendedTimeout,
            DefaultPollInterval,
            DefaultInstallerScan,
            DefaultPlayerScan)
    { }

    /// <summary>
    /// Seam ctor — inject the timeouts/poll plus the two install-deferral seams:
    /// <paramref name="isInstallerRunning"/> (the <c>RobloxPlayerInstaller.exe</c> signal, same shape
    /// as <c>RobloxUpdateProbe.IsInstallerRunning</c>) and <paramref name="candidateProcesses"/> (the
    /// raw <c>RobloxPlayerBeta</c> candidate source). The tracker still applies its own claim / FIFO /
    /// start-time filtering on top of the candidates, so that logic stays under test. Used by tests
    /// and any DI override; the public ctors wire the real live scans.
    /// </summary>
    internal RobloxProcessTracker(
        ILogger<RobloxProcessTracker> log,
        TimeSpan attachTimeout,
        TimeSpan installerExtendedTimeout,
        TimeSpan pollInterval,
        Func<bool> isInstallerRunning,
        Func<IReadOnlyList<Process>> candidateProcesses)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _attachTimeout = attachTimeout;
        _installerExtendedTimeout = installerExtendedTimeout;
        _pollInterval = pollInterval;
        _isInstallerRunning = isInstallerRunning ?? throw new ArgumentNullException(nameof(isInstallerRunning));
        _candidateProcesses = candidateProcesses ?? throw new ArgumentNullException(nameof(candidateProcesses));
    }

    public IReadOnlyDictionary<Guid, TrackedProcess> Attached =>
        _attachedByAccount.ToDictionary(
            kvp => kvp.Key,
            kvp => new TrackedProcess(kvp.Value.Process.Id, kvp.Value.AttachedAtUtc));

    public bool IsTracking(Guid accountId) => _attachedByAccount.ContainsKey(accountId);

    /// <inheritdoc cref="IForegroundAccountResolver.TryResolveAccountByPid"/>
    public bool TryResolveAccountByPid(int pid, out Guid accountId)
        => _claimedPidToAccount.TryGetValue(pid, out accountId);

    public event EventHandler<RobloxProcessEventArgs>? ProcessAttached;
    public event EventHandler<RobloxProcessEventArgs>? ProcessAttachFailed;
    public event EventHandler<RobloxProcessEventArgs>? ProcessExited;

    public bool AttachExisting(Guid accountId, int pid)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (pid <= 0) return false;

        Process? process = null;
        try
        {
            process = Process.GetProcessById(pid);
            if (process.HasExited) return false;
            // Defensive: double-check we're actually attaching to a player process. If we're
            // wrong here we'd end up tagging an unrelated process as a Roblox client.
            if (!string.Equals(process.ProcessName, PlayerProcessName, StringComparison.OrdinalIgnoreCase))
            {
                process.Dispose();
                return false;
            }
            if (!_claimedPidToAccount.TryAdd(pid, accountId))
            {
                // Already claimed (rare — same scanner re-runs). Bail without disturbing.
                process.Dispose();
                return false;
            }
            // Reuse the same attach path as the polled-launch flow so all consumers (UI badge,
            // session history, window decorator) get the same ProcessAttached event shape.
            AttachToProcess(accountId, process);
            return true;
        }
        catch (ArgumentException)
        {
            // pid no longer exists.
            process?.Dispose();
            return false;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "AttachExisting failed for pid {Pid} on account {AccountId}", pid, accountId);
            process?.Dispose();
            return false;
        }
    }

    public async Task TrackLaunchAsync(Guid accountId, DateTimeOffset launchedAtUtc, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var startUtc = DateTimeOffset.UtcNow;
        // Base deadline = today's 30s behavior. The extended ceiling is only ever USED when the
        // Roblox installer is found running as the base deadline elapses — an install can delay the
        // real RobloxPlayerBeta past 30s, and the v1.6.0 defender holds identity to ~120s, so the
        // tracker matches that hold instead of firing a false attach-failure mid-update. With no
        // installer running, the loop never extends and behavior is unchanged.
        var baseDeadline = startUtc + _attachTimeout;
        // The extended cap can't be shorter than the base deadline (guards a misconfigured ctor).
        var extendedDeadline = startUtc + (_installerExtendedTimeout > _attachTimeout
            ? _installerExtendedTimeout
            : _attachTimeout);
        var extended = false;
        _log.LogDebug("Tracking launch for account {AccountId}, base deadline {Deadline:O}", accountId, baseDeadline);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var now = DateTimeOffset.UtcNow;
            // Past the base deadline: extend ONLY while the installer is running, and only up to the
            // bounded extended cap. Re-check the installer signal each pass so a finished install
            // doesn't keep us waiting needlessly past the base window.
            if (now >= baseDeadline)
            {
                if (now >= extendedDeadline || !InstallerRunningSafe())
                {
                    break;
                }
                if (!extended)
                {
                    extended = true;
                    _log.LogInformation(
                        "RobloxPlayerInstaller.exe is running at the {Base}s mark for account {AccountId}; " +
                        "extending attach wait up to {Cap}s (lockstep with the appStorage defender's hold).",
                        _attachTimeout.TotalSeconds, accountId, _installerExtendedTimeout.TotalSeconds);
                }
            }

            var match = TryFindUnclaimedPlayerProcess(launchedAtUtc);
            if (match is not null)
            {
                if (_claimedPidToAccount.TryAdd(match.Id, accountId))
                {
                    AttachToProcess(accountId, match);
                    return;
                }
                // Lost the claim race — release the handle and keep polling.
                match.Dispose();
            }

            try
            {
                await Task.Delay(_pollInterval, ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                throw;
            }
        }

        var waited = extended ? _installerExtendedTimeout : _attachTimeout;
        _log.LogWarning(
            "No RobloxPlayerBeta.exe spawned within {Timeout}s for account {AccountId}{Extended}. " +
            "Likely silent launch failure (Roblox version drift, place removed, AV interference).",
            waited.TotalSeconds, accountId, extended ? " (installer-extended)" : string.Empty);
        ProcessAttachFailed?.Invoke(this, new RobloxProcessEventArgs(accountId, 0));
    }

    public bool RequestClose(Guid accountId)
    {
        if (!_attachedByAccount.TryGetValue(accountId, out var slot))
        {
            return false;
        }

        try
        {
            if (slot.Process.HasExited)
            {
                return false;
            }
            return slot.Process.CloseMainWindow();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "RequestClose failed for account {AccountId} pid {Pid}", accountId, slot.Process.Id);
            return false;
        }
    }

    public bool Kill(Guid accountId)
    {
        if (!_attachedByAccount.TryGetValue(accountId, out var slot))
        {
            return false;
        }

        try
        {
            if (slot.Process.HasExited)
            {
                return false;
            }
            slot.Process.Kill(entireProcessTree: false);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Kill failed for account {AccountId} pid {Pid}", accountId, slot.Process.Id);
            return false;
        }
    }

    // Degrade-safe wrapper for the installer signal: an injected scan that throws must never extend
    // the wait forever — treat a failed read as "not installing" (same bias as RobloxUpdateProbe).
    private bool InstallerRunningSafe()
    {
        try
        {
            return _isInstallerRunning();
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Installer-running signal threw; treating as not installing.");
            return false;
        }
    }

    private Process? TryFindUnclaimedPlayerProcess(DateTimeOffset earliestStartUtc)
    {
        IReadOnlyList<Process> candidates;
        try
        {
            candidates = _candidateProcesses();
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Candidate-process scan failed; will retry next poll.");
            return null;
        }

        Process? winner = null;
        DateTime winnerStart = DateTime.MaxValue;

        foreach (var p in candidates)
        {
            try
            {
                if (_claimedPidToAccount.ContainsKey(p.Id))
                {
                    p.Dispose();
                    continue;
                }
                if (p.HasExited)
                {
                    p.Dispose();
                    continue;
                }
                // Process.StartTime is local; compare in UTC with grace.
                var startUtc = p.StartTime.ToUniversalTime();
                if (startUtc + StartTimeGrace < earliestStartUtc.UtcDateTime)
                {
                    p.Dispose();
                    continue;
                }
                // Pick the EARLIEST eligible — when two launches race, we attach to the older one
                // first (FIFO), so the next iteration's caller gets the next one.
                if (p.StartTime < winnerStart)
                {
                    winner?.Dispose();
                    winner = p;
                    winnerStart = p.StartTime;
                }
                else
                {
                    p.Dispose();
                }
            }
            catch (InvalidOperationException)
            {
                // Process exited between the snapshot and the inspection.
                p.Dispose();
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Skipping process {Pid} during scan", p.Id);
                p.Dispose();
            }
        }

        return winner;
    }

    private void AttachToProcess(Guid accountId, Process process)
    {
        try
        {
            process.EnableRaisingEvents = true;
            var slot = new AttachedSlot(process, DateTimeOffset.UtcNow);
            process.Exited += (_, _) => HandleExited(accountId, process);
            _attachedByAccount[accountId] = slot;
            _log.LogInformation("Attached to RobloxPlayerBeta pid {Pid} for account {AccountId}", process.Id, accountId);
            ProcessAttached?.Invoke(this, new RobloxProcessEventArgs(accountId, process.Id));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to enable raising events on pid {Pid} for account {AccountId}", process.Id, accountId);
            _claimedPidToAccount.TryRemove(process.Id, out _);
            process.Dispose();
        }
    }

    private void HandleExited(Guid accountId, Process process)
    {
        try
        {
            _attachedByAccount.TryRemove(accountId, out _);
            _claimedPidToAccount.TryRemove(process.Id, out _);
            _log.LogInformation("RobloxPlayerBeta pid {Pid} exited for account {AccountId}", process.Id, accountId);
            ProcessExited?.Invoke(this, new RobloxProcessEventArgs(accountId, process.Id));
        }
        finally
        {
            try { process.Dispose(); } catch { }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        foreach (var slot in _attachedByAccount.Values)
        {
            try { slot.Process.Dispose(); } catch { }
        }
        _attachedByAccount.Clear();
        _claimedPidToAccount.Clear();
    }

    // Default live scans wired by the public ctors. The candidate scan hands the FULL set of
    // RobloxPlayerBeta processes to TryFindUnclaimedPlayerProcess, which owns the claim / FIFO /
    // start-time filtering. The installer scan mirrors RobloxUpdateProbe.IsInstallerRunning's
    // process-name family (kept local so the tracker doesn't take a hard dependency on the probe).
    private static IReadOnlyList<Process> DefaultPlayerScan() =>
        Process.GetProcessesByName(PlayerProcessName);

    private static bool DefaultInstallerScan()
    {
        var processes = Process.GetProcessesByName(InstallerProcessName);
        try
        {
            return processes.Length > 0;
        }
        catch
        {
            // Degrade-safe: a scan failure must never extend the wait forever. Treat as "not installing".
            return false;
        }
        finally
        {
            // Dispose every handle — same anti-leak discipline as the probe / running scanner.
            foreach (var p in processes)
            {
                p.Dispose();
            }
        }
    }

    private sealed record AttachedSlot(Process Process, DateTimeOffset AttachedAtUtc);
}
