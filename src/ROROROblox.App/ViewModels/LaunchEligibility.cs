namespace ROROROblox.App.ViewModels;

/// <summary>
/// The per-account flags the launch-eligibility computation needs, lifted out of
/// <see cref="AccountSummary"/> so the logic is pure and testable without constructing the heavy
/// view model. The v1.5.0 augment rule: an account is "already running" (and so skipped) when
/// <see cref="InGame"/> OR <see cref="IsRunning"/> — covering both in-game clients AND at-home/open
/// clients whose pid is alive. Spec §"Components > 3. Launch multiple hardening".
/// </summary>
internal readonly record struct LaunchCandidate(
    bool IsSelected,
    bool SessionExpired,
    bool SessionLimited,
    bool InGame,
    bool IsRunning,
    bool IsLaunching,
    string Name);

/// <summary>
/// Skip-reason tallies for a launch pass. Each bucket is mutually distinct: an account counts in
/// exactly one (selected-and-busy → Running; selected-and-expired → Expired;
/// selected-and-limited → Limited; not-selected → Deselected). In-flight launches
/// (<see cref="LaunchCandidate.IsLaunching"/>) are excluded from eligibility but NOT counted as a
/// skip reason — they aren't something the user needs to act on.
/// </summary>
internal readonly record struct LaunchBreakdown(int Running, int Expired, int Limited, int Deselected);

/// <summary>
/// Result of <see cref="LaunchEligibility.Compute"/>: the eligible candidates, the skip-reason
/// breakdown, and the banner strings. Pure data — no UI, no view-model dependency.
/// </summary>
internal sealed class LaunchEligibilityResult
{
    public required IReadOnlyList<LaunchCandidate> Eligible { get; init; }
    public required LaunchBreakdown Breakdown { get; init; }

    /// <summary>
    /// Banner shown when nothing is eligible — names the reason(s) with accurate counts. Zero-count
    /// clauses are omitted for readability, but at least one reason is always stated so the launch
    /// never silently no-ops.
    /// </summary>
    public string ZeroEligibleBanner
    {
        get
        {
            var clauses = NonZeroClauses();
            // No reasons at all (e.g. no accounts) — still say something, never go silent.
            if (clauses.Count == 0)
            {
                return "Nothing to launch — no accounts available.";
            }
            return $"Nothing to launch — {string.Join(", ", clauses)}.";
        }
    }

    /// <summary>
    /// Success banner for a launch that dispatched <paramref name="dispatched"/> client(s). When any
    /// accounts were skipped, appends a parenthetical skip-reason tail with only the non-zero
    /// clauses; with no skips, no parenthetical is added. The headline fix: when 1 of 7 is skipped
    /// for being running, the banner says so — no more silent 6-of-7.
    /// </summary>
    public string PartialBanner(int dispatched, string verb)
    {
        var clientWord = dispatched == 1 ? "client" : "clients";
        var head = $"{verb}. {dispatched} {clientWord} dispatched.";
        var clauses = NonZeroClauses();
        if (clauses.Count == 0)
        {
            return head;
        }
        return $"{head} ({string.Join(", ", clauses)}.)";
    }

    /// <summary>
    /// The non-zero skip-reason clauses, in a fixed order: running ▸ expired ▸ limited ▸ deselected.
    /// "running" uses the "already running" umbrella term (covers in-game AND at-home/open clients).
    /// </summary>
    private List<string> NonZeroClauses()
    {
        var clauses = new List<string>();
        if (Breakdown.Running > 0) clauses.Add($"{Breakdown.Running} already running");
        if (Breakdown.Expired > 0) clauses.Add($"{Breakdown.Expired} expired");
        if (Breakdown.Limited > 0) clauses.Add($"{Breakdown.Limited} limited");
        if (Breakdown.Deselected > 0) clauses.Add($"{Breakdown.Deselected} deselected");
        return clauses;
    }
}

/// <summary>
/// Pure launch-eligibility computation for the Launch-multiple / Private-server flows. Extracted
/// from <see cref="MainViewModel"/> so the busy/eligible test + breakdown counts + banner strings
/// can be unit-tested without the view model. Spec §"Components > 3".
/// </summary>
internal static class LaunchEligibility
{
    /// <summary>
    /// An account is "busy/active" (and so skipped) when <c>InGame || IsRunning</c> — the v1.5.0
    /// augment rule. This is the single source of truth used by both eligibility and CanExecute.
    /// </summary>
    public static bool IsBusy(LaunchCandidate c) => c.InGame || c.IsRunning;

    /// <summary>
    /// Split the candidates into the eligible set + a skip-reason breakdown. Eligible =
    /// <c>IsSelected &amp;&amp; !SessionExpired &amp;&amp; !(InGame || IsRunning) &amp;&amp; !IsLaunching</c>.
    /// Skip buckets are assigned in priority order over selected accounts: busy → Running,
    /// else expired → Expired. Not-selected accounts → Deselected. In-flight launches are excluded
    /// but uncounted.
    /// </summary>
    public static LaunchEligibilityResult Compute(IEnumerable<LaunchCandidate> candidates)
    {
        var eligible = new List<LaunchCandidate>();
        var running = 0;
        var expired = 0;
        var limited = 0;
        var deselected = 0;

        foreach (var c in candidates)
        {
            if (!c.IsSelected)
            {
                deselected++;
                continue;
            }
            // Selected from here down.
            if (c.SessionExpired)
            {
                expired++;
                continue;
            }
            if (c.SessionLimited) { limited++; continue; }   // after expired, before busy
            if (IsBusy(c))
            {
                running++;
                continue;
            }
            if (c.IsLaunching)
            {
                // In-flight — excluded from eligibility, not a user-actionable skip reason.
                continue;
            }
            eligible.Add(c);
        }

        return new LaunchEligibilityResult
        {
            Eligible = eligible,
            Breakdown = new LaunchBreakdown(running, expired, limited, deselected),
        };
    }
}
