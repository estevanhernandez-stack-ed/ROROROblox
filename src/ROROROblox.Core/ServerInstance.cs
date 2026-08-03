namespace ROROROblox.Core;

/// <summary>
/// One specific running Roblox server — a place and the job id of an instance OF that place,
/// taken from a single presence snapshot.
/// <para>
/// The two halves travel together because they only mean anything together. Roblox games teleport
/// players between places inside one universe, so the place an account <em>launched into</em> is
/// routinely not the place it is <em>in</em>: Pet Sim's entry place is <c>8737899170</c>, while
/// presence reports the account in <c>140403681187145</c>. Asking for a job id under the wrong
/// place produces "This experience has ended, or the server became unavailable unexpectedly due to
/// a system error" — the server is fine; the address does not exist. Verified 2026-08-02.
/// </para>
/// <para>
/// So: never build one of these from two sources. <see cref="TryFrom"/> is the only entrance, and
/// it takes both halves of one presence reading at once.
/// </para>
/// </summary>
public sealed record ServerInstance(long PlaceId, string JobId)
{
    /// <summary>
    /// Build a pair from one presence reading, or <see langword="null"/> when either half is
    /// missing — offline, privacy-hidden, or before the first poll lands. A half pair is not an
    /// address; callers fall back to <see cref="LaunchTarget.Place"/>, which is exactly what "this
    /// game, any server" already means.
    /// </summary>
    public static ServerInstance? TryFrom(long? placeId, string? jobId) =>
        placeId is > 0 && !string.IsNullOrWhiteSpace(jobId)
            ? new ServerInstance(placeId.Value, jobId)
            : null;
}
