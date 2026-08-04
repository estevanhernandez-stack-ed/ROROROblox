namespace ROROROblox.Core.Discord;

/// <summary>The two things worth waking someone up for. Deliberately not extensible without a
/// design decision — session-expired and landed-elsewhere were considered and cut (spec §11).</summary>
public enum AlertKind
{
    AccountDroppedOut,
    MemoryWarning,
}

/// <summary>
/// One alert-worthy event, carrying BOTH names.
/// <para>
/// <paramref name="DisplayName"/> is already rendered through streamer mode by the caller — it is
/// what every destination gets by default. <paramref name="RealName"/> is the actual account name,
/// and exactly one destination is allowed to use it: the clan channel.
/// </para>
/// <para>
/// Streamer mode exists to stop alt names reaching an audience the user did not choose — a stream,
/// a screenshot, a friends list. A clan channel is the opposite: a room the user deliberately
/// joined, full of people who already know which accounts are theirs. Masking there produces a
/// board of invented names nobody can act on, which is worse than useless in the one place the
/// information is meant to be shared.
/// </para>
/// <para>
/// The residual risk, stated once so it is not rediscovered: a clan channel visible on a second
/// monitor while streaming leaks real names anyway. That is a "don't show your clan channel on
/// stream" problem, not something this type can solve — but it IS the reason no other destination
/// gets <paramref name="RealName"/>.
/// </para>
/// </summary>
public sealed record AlertTrigger(
    AlertKind Kind,
    Guid AccountId,
    string DisplayName,
    string RealName,
    string? GameName,
    long? PrivateBytes,
    DateTimeOffset OccurredAtUtc);
