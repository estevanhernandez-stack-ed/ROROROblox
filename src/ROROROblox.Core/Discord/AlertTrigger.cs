namespace ROROROblox.Core.Discord;

/// <summary>The two things worth waking someone up for. Deliberately not extensible without a
/// design decision — session-expired and landed-elsewhere were considered and cut (spec §11).</summary>
public enum AlertKind
{
    AccountDroppedOut,
    MemoryWarning,
}

/// <summary>One alert-worthy event. <paramref name="DisplayName"/> is already rendered through
/// streamer mode by the caller — nothing downstream un-masks it.</summary>
public sealed record AlertTrigger(
    AlertKind Kind,
    Guid AccountId,
    string DisplayName,
    string? GameName,
    long? PrivateBytes,
    DateTimeOffset OccurredAtUtc);
