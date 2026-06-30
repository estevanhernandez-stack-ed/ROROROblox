using ROROROblox.App.ViewModels;

namespace ROROROblox.Tests;

/// <summary>
/// Tests for the pure <see cref="LaunchEligibility"/> computation extracted out of
/// <see cref="MainViewModel"/> so the busy/eligible logic + breakdown counts + banner strings can
/// be exercised without constructing the whole heavy view model. The v1.5.0 augment rule: an
/// account is "already running" (and so skipped) if <c>InGame || IsRunning</c> — this covers
/// in-game clients AND at-home/open clients whose pid is alive. Spec: §"Components > 3. Launch
/// multiple hardening" (with the task-level "already running" umbrella superseding the spec's
/// older "in a game" wording, since an at-home account isn't in a game).
/// </summary>
public class LaunchEligibilityTests
{
    private static LaunchCandidate Candidate(
        bool isSelected = true,
        bool sessionExpired = false,
        bool inGame = false,
        bool isRunning = false,
        bool isLaunching = false,
        string name = "Alt")
        => new(isSelected, sessionExpired, false, inGame, isRunning, isLaunching, name);

    // === Augment rule: InGame but pid lost is still "running" (skipped) ===

    [Fact]
    public void InGame_PidLost_IsExcluded_CountedAsRunning()
    {
        var c = Candidate(inGame: true, isRunning: false);

        var result = LaunchEligibility.Compute(new[] { c });

        Assert.Empty(result.Eligible);
        Assert.Equal(1, result.Breakdown.Running);
        Assert.Equal(0, result.Breakdown.Expired);
        Assert.Equal(0, result.Breakdown.Deselected);
    }

    // === Truly closed (both false) is included ===

    [Fact]
    public void NotInGame_NotRunning_IsIncluded()
    {
        var c = Candidate(inGame: false, isRunning: false);

        var result = LaunchEligibility.Compute(new[] { c });

        Assert.Single(result.Eligible);
        Assert.Equal(0, result.Breakdown.Running);
    }

    // === Running pid alive (at home, not in game) is excluded as "running" ===

    [Fact]
    public void IsRunning_NotInGame_IsExcluded_CountedAsRunning()
    {
        var c = Candidate(inGame: false, isRunning: true);

        var result = LaunchEligibility.Compute(new[] { c });

        Assert.Empty(result.Eligible);
        Assert.Equal(1, result.Breakdown.Running);
    }

    // === IsLaunching is excluded (in-flight) but not counted in any skip bucket ===

    [Fact]
    public void IsLaunching_IsExcluded()
    {
        var c = Candidate(isLaunching: true);

        var result = LaunchEligibility.Compute(new[] { c });

        Assert.Empty(result.Eligible);
        // In-flight launches aren't a "skip reason" the user needs to act on.
        Assert.Equal(0, result.Breakdown.Running);
        Assert.Equal(0, result.Breakdown.Expired);
        Assert.Equal(0, result.Breakdown.Deselected);
    }

    // === All running/in-game → empty eligible, zero-eligible banner names the reason ===

    [Fact]
    public void AllRunningOrInGame_EmptyEligible_ZeroBannerNamesReason()
    {
        var candidates = new[]
        {
            Candidate(isRunning: true),
            Candidate(inGame: true, isRunning: false),
            Candidate(isRunning: true),
        };

        var result = LaunchEligibility.Compute(candidates);

        Assert.Empty(result.Eligible);
        Assert.Equal(3, result.Breakdown.Running);
        Assert.Contains("Nothing to launch", result.ZeroEligibleBanner);
        Assert.Contains("3 already running", result.ZeroEligibleBanner);
    }

    // === Mixed: 7 selected, 1 running → 6 eligible, partial banner has skip-reason tail ===

    [Fact]
    public void SevenSelected_OneRunning_SixEligible_PartialBannerHasSkipTail()
    {
        var candidates = new List<LaunchCandidate>();
        for (var i = 0; i < 6; i++) candidates.Add(Candidate());
        candidates.Add(Candidate(isRunning: true));

        var result = LaunchEligibility.Compute(candidates);

        Assert.Equal(6, result.Eligible.Count);
        Assert.Equal(1, result.Breakdown.Running);

        var banner = result.PartialBanner(dispatched: 6, verb: "Launch multiple finished");
        Assert.Contains("6 client", banner);
        Assert.Contains("1 already running", banner);
    }

    // === Deselected + expired land in their own buckets, not "running" ===

    [Fact]
    public void DeselectedAndExpired_CountedInOwnBuckets_NotRunning()
    {
        var candidates = new[]
        {
            Candidate(isSelected: false),                 // deselected
            Candidate(sessionExpired: true),              // expired
            Candidate(),                                  // eligible
        };

        var result = LaunchEligibility.Compute(candidates);

        Assert.Single(result.Eligible);
        Assert.Equal(0, result.Breakdown.Running);
        Assert.Equal(1, result.Breakdown.Expired);
        Assert.Equal(1, result.Breakdown.Deselected);
    }

    // === Zero-eligible banner omits zero-count clauses but always states at least one reason ===

    [Fact]
    public void ZeroEligibleBanner_OmitsZeroClauses_StatesAtLeastOneReason()
    {
        // Everything deselected: only "deselected" should appear, no "0 running / 0 expired" noise.
        var candidates = new[]
        {
            Candidate(isSelected: false),
            Candidate(isSelected: false),
        };

        var result = LaunchEligibility.Compute(candidates);

        Assert.Empty(result.Eligible);
        Assert.Contains("2 deselected", result.ZeroEligibleBanner);
        Assert.DoesNotContain("0 ", result.ZeroEligibleBanner);
        Assert.DoesNotContain("running", result.ZeroEligibleBanner);
        Assert.DoesNotContain("expired", result.ZeroEligibleBanner);
    }

    [Fact]
    public void ZeroEligibleBanner_NoCandidatesAtAll_StillStatesAReason()
    {
        var result = LaunchEligibility.Compute(Array.Empty<LaunchCandidate>());

        Assert.Empty(result.Eligible);
        // No accounts to launch — banner must not be empty / silent.
        Assert.False(string.IsNullOrWhiteSpace(result.ZeroEligibleBanner));
        Assert.Contains("Nothing to launch", result.ZeroEligibleBanner);
    }

    // === Partial banner: no skip reasons → clean banner with no trailing parenthetical ===

    [Fact]
    public void PartialBanner_NoSkips_HasNoParenthetical()
    {
        var candidates = new[] { Candidate(), Candidate() };

        var result = LaunchEligibility.Compute(candidates);

        Assert.Equal(2, result.Eligible.Count);
        var banner = result.PartialBanner(dispatched: 2, verb: "Launch multiple finished");
        Assert.DoesNotContain("(", banner);
        Assert.Contains("2 clients dispatched", banner);
    }

    [Fact]
    public void PartialBanner_OnlyNonZeroClausesIncluded()
    {
        // 1 running, 2 deselected, 0 expired → tail names running + deselected, omits expired.
        var candidates = new[]
        {
            Candidate(),                       // eligible
            Candidate(isRunning: true),        // running
            Candidate(isSelected: false),      // deselected
            Candidate(isSelected: false),      // deselected
        };

        var result = LaunchEligibility.Compute(candidates);

        var banner = result.PartialBanner(dispatched: 1, verb: "Launch multiple finished");
        Assert.Contains("1 already running", banner);
        Assert.Contains("2 deselected", banner);
        Assert.DoesNotContain("expired", banner);
    }

    // === Singular vs plural client wording ===

    [Fact]
    public void PartialBanner_SingleClient_UsesSingular()
    {
        var candidates = new[] { Candidate() };

        var result = LaunchEligibility.Compute(candidates);

        var banner = result.PartialBanner(dispatched: 1, verb: "Launch multiple finished");
        Assert.Contains("1 client dispatched", banner);
        Assert.DoesNotContain("clients", banner);
    }
}

// ============================================================
// Task 6: Limited skip-bucket tests
// ============================================================

public class LaunchEligibilityLimitedTests
{
    private static LaunchCandidate Cand(bool selected = true, bool expired = false,
        bool limited = false, bool inGame = false, bool running = false, bool launching = false)
        => new(selected, expired, limited, inGame, running, launching, "Alt");

    [Fact]
    public void Compute_LimitedAccount_GoesToLimitedBucket_NotEligible()
    {
        var result = LaunchEligibility.Compute(new[] { Cand(limited: true) });

        Assert.Empty(result.Eligible);
        Assert.Equal(1, result.Breakdown.Limited);
        Assert.Equal(0, result.Breakdown.Expired);
        Assert.Contains("1 limited", result.ZeroEligibleBanner);
    }

    [Fact]
    public void Compute_ExpiredBeatsLimited_CountsAsExpired()
    {
        var result = LaunchEligibility.Compute(new[] { Cand(expired: true, limited: true) });

        Assert.Equal(1, result.Breakdown.Expired);
        Assert.Equal(0, result.Breakdown.Limited);
    }
}
