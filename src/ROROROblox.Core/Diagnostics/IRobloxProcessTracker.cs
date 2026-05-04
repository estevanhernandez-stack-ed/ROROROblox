namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Tracks <c>RobloxPlayerBeta.exe</c> client processes spawned by Launch As, so the UI can show
/// a live "Running"/"Closed" badge per account. The launcher PID returned by
/// <see cref="LaunchResult.Started"/> is the short-lived <c>RobloxPlayerLauncher.exe</c>; the
/// tracker is what surfaces the actual long-running game window's lifetime.
/// </summary>
public interface IRobloxProcessTracker
{
    /// <summary>
    /// Begin watching for the <c>RobloxPlayerBeta.exe</c> spawned by a launch initiated at
    /// <paramref name="launchedAtUtc"/>. Polls until a new (unclaimed) player process appears,
    /// then attaches and fires <see cref="ProcessAttached"/>. Times out after 30 seconds, in
    /// which case <see cref="ProcessAttachFailed"/> fires (silent launch failure — wrong
    /// Roblox version, place gone, antivirus, etc.).
    /// </summary>
    Task TrackLaunchAsync(Guid accountId, DateTimeOffset launchedAtUtc, CancellationToken ct = default);

    /// <summary>
    /// Attach to an ALREADY-running <c>RobloxPlayerBeta.exe</c> by pid. Used at app startup
    /// to re-establish tracking for windows the user launched in a prior ROROROblox session
    /// (or manually outside the app), so they don't lose state across an app restart and so
    /// auto-launch knows the account is already playing. Returns true if the attach succeeded
    /// (process exists, isn't already claimed, and is the player binary).
    /// </summary>
    bool AttachExisting(Guid accountId, int pid);

    /// <summary>Snapshot of currently-attached PIDs by account id.</summary>
    IReadOnlyDictionary<Guid, TrackedProcess> Attached { get; }

    /// <summary>True if we're currently watching a player process for this account.</summary>
    bool IsTracking(Guid accountId);

    /// <summary>
    /// Send the tracked window a graceful close (CloseMainWindow). Returns false if no process
    /// is tracked for the account or the close call fails.
    /// </summary>
    bool RequestClose(Guid accountId);

    /// <summary>
    /// Hard-kill the tracked process. Use only as a fallback after <see cref="RequestClose"/>.
    /// </summary>
    bool Kill(Guid accountId);

    /// <summary>Fired when a player process is successfully attached after a launch.</summary>
    event EventHandler<RobloxProcessEventArgs>? ProcessAttached;

    /// <summary>Fired when no player process appears within the attach timeout.</summary>
    event EventHandler<RobloxProcessEventArgs>? ProcessAttachFailed;

    /// <summary>Fired when an attached player process exits (user closed the window, crash, etc.).</summary>
    event EventHandler<RobloxProcessEventArgs>? ProcessExited;
}

/// <summary>
/// Snapshot of one currently-attached Roblox process.
/// </summary>
public sealed record TrackedProcess(int Pid, DateTimeOffset AttachedAtUtc);

/// <summary>
/// Event payload for tracker lifecycle events. <paramref name="Pid"/> is 0 on attach-failed.
/// </summary>
public sealed record RobloxProcessEventArgs(Guid AccountId, int Pid)
{
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
