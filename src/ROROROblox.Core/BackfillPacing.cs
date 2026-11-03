namespace ROROROblox.Core;

/// <summary>
/// The pacing the per-account background lanes share: <see cref="AccountUserIdBackfillService"/>
/// and <see cref="AccountFactsService"/> read one number, so the "two accounts are never requested
/// in the same second" rule cannot drift between them. The wait between consecutive accounts is
/// <c>InterAccountDelayMs + Random.Shared.Next(-JitterMs, JitterMs)</c>, i.e. 2.0 to 3.0 s.
/// </summary>
public static class BackfillPacing
{
    /// <summary>Base wait between consecutive accounts, in milliseconds.</summary>
    public const int InterAccountDelayMs = 2500;

    /// <summary>Jitter half-width in milliseconds, applied as <c>Random.Shared.Next(-JitterMs, JitterMs)</c>.</summary>
    public const int JitterMs = 500;
}
