using Microsoft.Extensions.Logging;

namespace ROROROblox.Core;

/// <summary>How a per-account FPS cap ended up being applied (or not).</summary>
public enum FpsCapSettleOutcome
{
    /// <summary>The file already held this cap. Nothing written, nothing waited for.</summary>
    AlreadySet,

    /// <summary>Written, and confirmed still present after a full quiet window post-write.</summary>
    Settled,

    /// <summary>
    /// Gave up — either every attempt was overwritten, or the overall settle budget
    /// (<see cref="FpsCapSettler.SettleTimeout"/>) ran out first. Launching anyway, with a cap
    /// that may be wrong.
    /// </summary>
    Exhausted,

    /// <summary>The writer failed. Degraded, non-blocking — the launch proceeds.</summary>
    WriteFailed,
}

/// <summary>
/// Makes a per-account FPS cap survive close-together launches.
/// <para>
/// Roblox keeps ONE settings file per install and a starting client re-persists its own
/// FramerateCap to it repeatedly for ~9 seconds (measured 2026-08-02). So the party that
/// overwrites our value is the PREVIOUS CLIENT, not the next launch — which is why the earlier
/// pid-based launch gate could be correct in every detail and still not fix the bug. In the
/// decisive run our write survived 170 milliseconds.
/// </para>
/// <para>
/// Correctness comes from <em>re-confirming</em> the write, not from any single constant being
/// right. After writing, we wait for the file to go quiet a SECOND time — a fresh
/// <see cref="QuietDebounce"/> window seeded from the moment of our own write — before
/// re-reading. That means a clobber landing anywhere across that whole window gets caught and
/// retried, not just one landing inside a short fixed pause immediately after the write.
/// <see cref="MaxWriteAttempts"/> and <see cref="SettleTimeout"/> both bound how long we keep
/// trying; once either is exhausted we launch anyway with whatever is currently on disk
/// (<see cref="FpsCapSettleOutcome.Exhausted"/>) rather than block the launch.
/// </para>
/// </summary>
public static class FpsCapSettler
{
    /// <summary>
    /// How long the file must be unmodified before we call it quiet. Must exceed the largest gap
    /// observed BETWEEN a client's own writes (3.25 s on 2026-08-02) with margin. Not
    /// correctness-critical on its own — a too-short debounce costs a retry, not a wrong cap,
    /// because every write is re-confirmed against a fresh quiet-wait (see class remarks).
    /// </summary>
    internal static readonly TimeSpan QuietDebounce = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Ceiling on a SINGLE quiet-wait call. A quiet-wait started early in a settle attempt can
    /// still run up to this long; in practice <see cref="SettleTimeout"/> is the tighter bound
    /// for calls started later, since it caps the whole attempt, not just one wait.
    /// </summary>
    internal static readonly TimeSpan QuietWaitTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Ceiling on the ENTIRE settle call — every quiet-wait and write across every retry,
    /// combined. Without this, a permanently busy file could run
    /// <see cref="MaxWriteAttempts"/> attempts of two <see cref="QuietWaitTimeout"/>-bounded
    /// waits each — as much as 3 x (30 s + 30 s) = 180 s — before giving up. That reads to a
    /// user clicking Launch as a hang, not a slow launch, so this budget dominates in practice:
    /// the real worst case is ~<see cref="SettleTimeout"/>, plus negligible read/write overhead.
    /// </summary>
    internal static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(20);

    /// <summary>How often to re-check the file's last-write time.</summary>
    internal static readonly TimeSpan QuietPollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Backstop cap on retries. <see cref="SettleTimeout"/> is what actually governs worst-case
    /// wall time in practice — this exists so a file that keeps going quiet and being
    /// immediately re-clobbered can't loop forever within that budget.
    /// </summary>
    internal const int MaxWriteAttempts = 3;

    public static async Task<FpsCapSettleOutcome> SettleAsync(
        IGlobalBasicSettingsProbe probe,
        IGlobalBasicSettingsWriter writer,
        int desiredCap,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken ct)
    {
        // Fast path. The race only exists when consecutive launches want DIFFERENT caps; if the
        // file already says what we need, there is nothing to protect and nothing to wait for.
        // This is what keeps the feature shippable -- most users set one cap across every account
        // and must not pay a settle window per launch for a case they are not in.
        if (probe.ReadFramerateCap() == desiredCap)
        {
            logger.LogDebug("FPS cap {Cap} already on disk; no write, no wait.", desiredCap);
            return FpsCapSettleOutcome.AlreadySet;
        }

        var overallDeadline = timeProvider.GetUtcNow() + SettleTimeout;
        var attemptsMade = 0;

        for (var attempt = 1; attempt <= MaxWriteAttempts; attempt++)
        {
            if (timeProvider.GetUtcNow() >= overallDeadline)
            {
                break;
            }

            attemptsMade = attempt;

            await WaitForQuietAsync(probe, timeProvider, overallDeadline, "pre-write", logger, ct)
                .ConfigureAwait(false);

            try
            {
                await writer.WriteFramerateCapAsync(desiredCap, ct).ConfigureAwait(false);
            }
            catch (GlobalBasicSettingsWriteException ex)
            {
                // Same posture as the pre-existing call site: degraded, non-blocking. Roblox falls
                // back to whatever cap is already in the file.
                logger.LogWarning(ex, "Could not write FPS cap {Cap}; launching with the existing value.", desiredCap);
                return FpsCapSettleOutcome.WriteFailed;
            }

            // Re-confirm across a FULL fresh quiet window, not a short fixed pause. A clobber
            // landing anywhere in this window gets caught here, not just one landing in the first
            // second — see class remarks for why a short fixed pause is a false floor.
            //
            // The confirmation only counts if the wait actually OBSERVED quiet. If it instead ran
            // out the overall budget, the read below happens moments after our own write with no
            // window for a still-settling previous client to have clobbered it — that is not a
            // confirmation, it's an unproven read that happens to match. Trusting it here is the
            // exact shape of the original wrong-cap bug, just with a green `Settled` on top of it.
            var postWriteQuiet = await WaitForQuietAsync(probe, timeProvider, overallDeadline, "post-write", logger, ct)
                .ConfigureAwait(false);

            if (postWriteQuiet)
            {
                if (probe.ReadFramerateCap() == desiredCap)
                {
                    return FpsCapSettleOutcome.Settled;
                }

                // A GENUINE clobber: the file went quiet after our write, and the value it went
                // quiet holding is not ours. Something else wrote in between.
                logger.LogWarning(
                    "FPS cap {Cap} was overwritten after the write (attempt {Attempt} of {Max}) — a client is still settling.",
                    desiredCap, attempt, MaxWriteAttempts);
            }
            else
            {
                // NOT a confirmed clobber: the post-write wait timed out before ever observing
                // quiet, so nothing is known about whether our write survived. Log this distinctly
                // from the genuine-overwrite case above, or support-bundle triage chases a clobber
                // that may not exist.
                logger.LogWarning(
                    "FPS cap {Cap} could not be confirmed (attempt {Attempt} of {Max}) — the post-write quiet " +
                    "wait timed out before the file ever went quiet, so whether the write survived is unknown.",
                    desiredCap, attempt, MaxWriteAttempts);
            }
        }

        // Out of attempts, or out of budget: launch anyway. A contended settings file must never
        // abort a launch. This is the ONLY path where the original wrong-cap bug can still reach
        // a user, so it is logged at Error to make it impossible to miss in a support bundle.
        logger.LogError(
            "Gave up applying FPS cap {Cap} after {Attempts} attempt(s) within the {Budget} settle budget; this client may run the wrong cap.",
            desiredCap, attemptsMade, SettleTimeout);
        return FpsCapSettleOutcome.Exhausted;
    }

    /// <summary>
    /// Block until the settings file has been unmodified for <see cref="QuietDebounce"/>, bounded
    /// by whichever comes sooner: <see cref="QuietWaitTimeout"/> after this call started, or the
    /// caller's <paramref name="overallDeadline"/>. Logs the outcome on BOTH branches — settled or
    /// timed out — with the actually-measured elapsed time, never the constant.
    /// <para>
    /// Seeds its "quiet since" baseline from the file's last-observed write time rather than from
    /// "now". A file that has genuinely been untouched for longer than <see cref="QuietDebounce"/>
    /// already — the common case, since Roblox writes this file on session exit and the first
    /// launch of a session often finds no Roblox process running at all — is credited as already
    /// quiet and this returns immediately instead of paying a flat debounce it did not need.
    /// </para>
    /// <para>
    /// Returns <see langword="true"/> only when quiet was actually observed, and
    /// <see langword="false"/> when the call gave up at <paramref name="overallDeadline"/> (or its
    /// own <see cref="QuietWaitTimeout"/>) without ever seeing it. Callers that treat a
    /// post-write call of this as a confirmation MUST check the return value — a timeout is not a
    /// confirmation, it's the absence of one.
    /// </para>
    /// </summary>
    private static async Task<bool> WaitForQuietAsync(
        IGlobalBasicSettingsProbe probe,
        TimeProvider timeProvider,
        DateTimeOffset overallDeadline,
        string phase,
        ILogger logger,
        CancellationToken ct)
    {
        var start = timeProvider.GetUtcNow();
        var perCallDeadline = start + QuietWaitTimeout;
        var deadline = perCallDeadline < overallDeadline ? perCallDeadline : overallDeadline;

        var lastSeen = probe.GetLastWriteTimeUtc();
        var quietSince = lastSeen ?? start;

        while (timeProvider.GetUtcNow() < deadline)
        {
            if (timeProvider.GetUtcNow() - quietSince >= QuietDebounce)
            {
                logger.LogInformation(
                    "Quiet wait ({Phase}) settled after {Elapsed}.", phase, timeProvider.GetUtcNow() - start);
                return true;
            }

            await Task.Delay(QuietPollInterval, timeProvider, ct).ConfigureAwait(false);

            var now = probe.GetLastWriteTimeUtc();
            if (now != lastSeen)
            {
                lastSeen = now;
                quietSince = timeProvider.GetUtcNow();
            }
        }

        logger.LogInformation(
            "Quiet wait ({Phase}) timed out after {Elapsed} without settling.", phase, timeProvider.GetUtcNow() - start);
        return false;
    }
}
