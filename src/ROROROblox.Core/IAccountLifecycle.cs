namespace ROROROblox.Core;

/// <summary>
/// Account-shaped lifecycle events. Closes the spec gap where v1.2's
/// <c>2026-05-06-discord-clan-coordination-design.md §5.8</c> assumed an <c>IAccountLifecycle</c>
/// existed in canonical §5 — it didn't. This abstraction adapts the existing
/// <see cref="ROROROblox.Core.Diagnostics.IRobloxProcessTracker"/> low-level pid events into
/// <see cref="Account"/>-enriched events with current active count, which is the shape
/// DiscordPresenceLifecycle (item 7) needs to drive presence + webhooks.
///
/// MainViewModel is intentionally NOT a producer of these events — it's already calling
/// <c>IRobloxProcessTracker.TrackLaunchAsync</c> per launch, and the adapter listens to the
/// tracker. Spec drift from item 6's "NotifyStarted" method documented in the commit message;
/// will banner-correct in item 11.
/// </summary>
public interface IAccountLifecycle
{
    event EventHandler<AccountStartedEventArgs>? AccountStarted;
    event EventHandler<AccountStoppedEventArgs>? AccountStopped;
}

public sealed class AccountStartedEventArgs(Account account, int processId, int currentActiveCount) : EventArgs
{
    public Account Account { get; } = account;
    public int ProcessId { get; } = processId;

    /// <summary>Active account count AFTER this start (includes the newly-started account).</summary>
    public int CurrentActiveCount { get; } = currentActiveCount;
}

public sealed class AccountStoppedEventArgs(Account account, int processId, int currentActiveCount) : EventArgs
{
    public Account Account { get; } = account;
    public int ProcessId { get; } = processId;

    /// <summary>Active account count AFTER this stop (excludes the just-stopped account).</summary>
    public int CurrentActiveCount { get; } = currentActiveCount;
}
