namespace ROROROblox.Core.Metrics;

/// <summary>
/// One number, reported for one account, at one instant.
///
/// <para>
/// <paramref name="AccountId"/> is a RoRoRo account, NOT an external subject id. Mapping an
/// external identity onto an account is the caller's job, so that mute and per-(account, kind)
/// cooldown key exactly as they do for every other alert kind. An observation that belongs to no
/// account uses <see cref="Guid.Empty"/> — the same global-carrier convention
/// <c>AlertKind.UptimeMark</c> uses, and for the same reason: one account's mute must not
/// silence something that is not about that account.
/// </para>
/// <para>
/// <paramref name="Value"/> is whatever the source reported, raw. It is frequently CUMULATIVE
/// (points so far, total earned) rather than a rate; deriving a rate is
/// <see cref="MetricHistory"/>'s job, from two observations.
/// </para>
/// </summary>
public sealed record MetricObservation(
    Guid AccountId,
    string MetricId,
    double Value,
    DateTimeOffset ObservedAtUtc);
