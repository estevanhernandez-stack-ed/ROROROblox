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
public sealed class RobloxProcessTracker : IRobloxProcessTracker, IDisposable
{
    private const string PlayerProcessName = "RobloxPlayerBeta";
    private static readonly TimeSpan DefaultAttachTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(750);
    // Process.StartTime can read a hair earlier than the wall clock we captured pre-Process.Start,
    // so allow a small grace window when comparing against launchedAtUtc.
    private static readonly TimeSpan StartTimeGrace = TimeSpan.FromSeconds(3);

    private readonly ILogger<RobloxProcessTracker> _log;
    private readonly TimeSpan _attachTimeout;
    private readonly TimeSpan _pollInterval;
    private readonly ConcurrentDictionary<Guid, AttachedSlot> _attachedByAccount = new();
    private readonly ConcurrentDictionary<int, Guid> _claimedPidToAccount = new();
    private bool _disposed;

    public RobloxProcessTracker() : this(NullLogger<RobloxProcessTracker>.Instance) { }

    public RobloxProcessTracker(ILogger<RobloxProcessTracker> log)
        : this(log, DefaultAttachTimeout, DefaultPollInterval) { }

    internal RobloxProcessTracker(
        ILogger<RobloxProcessTracker> log,
        TimeSpan attachTimeout,
        TimeSpan pollInterval)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _attachTimeout = attachTimeout;
        _pollInterval = pollInterval;
    }

    public IReadOnlyDictionary<Guid, TrackedProcess> Attached =>
        _attachedByAccount.ToDictionary(
            kvp => kvp.Key,
            kvp => new TrackedProcess(kvp.Value.Process.Id, kvp.Value.AttachedAtUtc));

    public bool IsTracking(Guid accountId) => _attachedByAccount.ContainsKey(accountId);

    public event EventHandler<RobloxProcessEventArgs>? ProcessAttached;
    public event EventHandler<RobloxProcessEventArgs>? ProcessAttachFailed;
    public event EventHandler<RobloxProcessEventArgs>? ProcessExited;

    public async Task TrackLaunchAsync(Guid accountId, DateTimeOffset launchedAtUtc, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var deadline = DateTimeOffset.UtcNow + _attachTimeout;
        _log.LogDebug("Tracking launch for account {AccountId}, deadline {Deadline:O}", accountId, deadline);

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

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

        _log.LogWarning(
            "No RobloxPlayerBeta.exe spawned within {Timeout}s for account {AccountId}. " +
            "Likely silent launch failure (Roblox version drift, place removed, AV interference).",
            _attachTimeout.TotalSeconds, accountId);
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

    private Process? TryFindUnclaimedPlayerProcess(DateTimeOffset earliestStartUtc)
    {
        Process[] candidates;
        try
        {
            candidates = Process.GetProcessesByName(PlayerProcessName);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "GetProcessesByName failed; will retry next poll.");
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

    private sealed record AttachedSlot(Process Process, DateTimeOffset AttachedAtUtc);
}
