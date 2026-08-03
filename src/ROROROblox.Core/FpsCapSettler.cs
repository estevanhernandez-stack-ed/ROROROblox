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
/// <para>
/// <b>2026-08-02 correction:</b> the two paragraphs above describe a mechanism that, on its own,
/// does NOT fix the bug — measured directly, three accounts at three different caps each came up
/// running the NEXT account's value. The re-confirm loop protects OUR write from the previous
/// client; nothing protected the newly launched client's READ from the write we make for the
/// account after it. Immediately after a launch, "the file hasn't changed" is ambiguous between
/// "the new client already read it and it's genuinely calm" and "the new client hasn't started
/// writing yet" — and the pre-write quiet-wait was crediting the second case as quiet. The fix is
/// <c>SettleAsync</c>'s <c>launchBaselineUtc</c> parameter (threaded from
/// <see cref="RobloxLauncher"/>, which remembers the file's mtime at the moment of each launch's
/// <c>Process.Start</c>): on the slow path, the pre-write quiet-wait additionally refuses to
/// declare quiet until the file's mtime has moved away from that baseline at least once — a
/// client's first write-back to this file IS its proof-of-read. See
/// <see cref="WaitForQuietAsync"/>'s <c>requireWriteSince</c> parameter.
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
    /// <para>
    /// Sized at 45 s (raised from 20 s, 2026-08-02) now that the slow path also pays a
    /// proof-of-read wait up front: measured first write-back landed anywhere from +2.88 s to
    /// +7.07 s after launch, and the launched client keeps re-persisting for a further ~9-12 s
    /// after that before it actually goes quiet. A realistic contended attempt is therefore
    /// roughly 3-7 s (proof-of-read) + 6-9 s (settling to quiet) + 5 s (post-write debounce) ≈
    /// 15-20 s typical — 20 s left no margin at all.
    /// </para>
    /// </summary>
    internal static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(45);

    /// <summary>How often to re-check the file's last-write time.</summary>
    internal static readonly TimeSpan QuietPollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Backstop cap on retries. <see cref="SettleTimeout"/> is what actually governs worst-case
    /// wall time in practice — this exists so a file that keeps going quiet and being
    /// immediately re-clobbered can't loop forever within that budget.
    /// </summary>
    internal const int MaxWriteAttempts = 3;

    /// <param name="probe">Read side of the shared settings file.</param>
    /// <param name="writer">Write side of the shared settings file.</param>
    /// <param name="desiredCap">The FramerateCap this account wants applied.</param>
    /// <param name="timeProvider">Clock — real in production, fake in tests.</param>
    /// <param name="logger">Sink for the settle narrative (Info on both quiet-wait branches, Warning
    /// on a clobber or an unconfirmed write, Error only when the whole call gives up).</param>
    /// <param name="ct">Cancellation for the underlying delays.</param>
    /// <param name="launchBaselineUtc">
    /// The settings file's mtime at the moment the PREVIOUSLY launched client's <c>Process.Start</c>
    /// returned, or <see langword="null"/> if there was no previous launch this session (or no
    /// probe is wired). This is the proof-of-read gate: a client's first write-back to this file
    /// after it starts is the only observable signal that it has already read the cap we set for
    /// it, so on the slow path we refuse to declare the file "quiet" — and therefore refuse to
    /// write the NEXT account's cap — until the file's mtime has moved away from this baseline at
    /// least once. Passing <see langword="null"/> reproduces the pre-2026-08-02 behaviour exactly
    /// (no gate at all), which is also what happens on every retry after the first: by the time a
    /// second attempt starts, the file has already diverged from this baseline, so the gate is a
    /// no-op there. Never applied to the fast path — see the early return below.
    /// </param>
    public static async Task<FpsCapSettleOutcome> SettleAsync(
        IGlobalBasicSettingsProbe probe,
        IGlobalBasicSettingsWriter writer,
        int desiredCap,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken ct,
        DateTimeOffset? launchBaselineUtc = null)
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

            // requireWriteSince is the proof-of-read gate (see the parameter doc on this method).
            // Only the pre-write wait needs it: this is the wait that stands between "the previous
            // client might not have read yet" and "we're about to overwrite the file with the next
            // account's cap". The post-write wait below is a different question entirely (did OUR
            // write survive), so it never takes this argument.
            await WaitForQuietAsync(
                probe, timeProvider, overallDeadline, "pre-write", logger, ct, requireWriteSince: launchBaselineUtc)
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
    /// When <paramref name="requireWriteSince"/> is supplied, "quiet" is no longer sufficient on
    /// its own — this additionally refuses to declare quiet until the file's mtime has been
    /// observed to differ from that baseline at least once. That is the proof-of-read gate: right
    /// after a launch, "the file hasn't changed" is ambiguous between "the new client already read
    /// it and it's genuinely calm" and "the new client hasn't started writing yet" — and crediting
    /// the second case as quiet is exactly the bug this parameter exists to close (see
    /// <see cref="FpsCapSettler"/> class remarks). One merged loop is used for both conditions —
    /// rather than a separate wait-for-write-then-wait-for-quiet pair — so a write that lands
    /// mid-poll immediately starts counting toward the SAME debounce window this loop is already
    /// running, instead of restarting a second one from scratch.
    /// </para>
    /// <para>
    /// Returns <see langword="true"/> only when quiet was actually observed (and, when
    /// <paramref name="requireWriteSince"/> is set, only once a write past that baseline was also
    /// observed), and <see langword="false"/> when the call gave up at
    /// <paramref name="overallDeadline"/> (or its own <see cref="QuietWaitTimeout"/>) without ever
    /// seeing it. Callers that treat a post-write call of this as a confirmation MUST check the
    /// return value — a timeout is not a confirmation, it's the absence of one. A pre-write caller
    /// with <paramref name="requireWriteSince"/> set is expected to proceed regardless of the
    /// return value (bounded, not blocking) — a launched client that crashed, or one Roblox folded
    /// into an already-running instance, will never produce the write this is watching for.
    /// </para>
    /// </summary>
    private static async Task<bool> WaitForQuietAsync(
        IGlobalBasicSettingsProbe probe,
        TimeProvider timeProvider,
        DateTimeOffset overallDeadline,
        string phase,
        ILogger logger,
        CancellationToken ct,
        DateTimeOffset? requireWriteSince = null)
    {
        var start = timeProvider.GetUtcNow();
        var perCallDeadline = start + QuietWaitTimeout;
        var deadline = perCallDeadline < overallDeadline ? perCallDeadline : overallDeadline;

        var lastSeen = probe.GetLastWriteTimeUtc();
        var quietSince = lastSeen ?? start;

        // No baseline to prove a write against -> the gate is trivially satisfied (matches
        // pre-2026-08-02 behaviour exactly). A baseline that already differs from the current
        // mtime means the write already happened before this call even started (e.g. the operator
        // paused between launches) -- also trivially satisfied, and quietSince above is already
        // seeded from that same lastSeen, so the debounce clock is correctly counting from the
        // most recent known write, not from "now".
        var writeObserved = requireWriteSince is null
            || (lastSeen.HasValue && lastSeen.Value != requireWriteSince.Value);

        while (timeProvider.GetUtcNow() < deadline)
        {
            if (writeObserved && timeProvider.GetUtcNow() - quietSince >= QuietDebounce)
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
                if (!writeObserved && requireWriteSince is not null
                    && lastSeen.HasValue && lastSeen.Value != requireWriteSince.Value)
                {
                    writeObserved = true;
                }
            }
        }

        if (requireWriteSince is not null && !writeObserved)
        {
            // Bounded, not blocking: the launched client may have crashed, or Roblox may have
            // folded the launch into an already-running instance, and in either case it will never
            // produce the write this loop is watching for. Distinct message from the plain
            // quiet-timeout below so a support bundle doesn't read this as "the file stayed busy"
            // when what actually happened is "nobody ever proved they read it".
            logger.LogWarning(
                "Proof-of-read wait ({Phase}) timed out after {Elapsed} without observing a write from the " +
                "launched client; proceeding without confirmation that it read the file first.",
                phase, timeProvider.GetUtcNow() - start);
            return false;
        }

        logger.LogInformation(
            "Quiet wait ({Phase}) timed out after {Elapsed} without settling.", phase, timeProvider.GetUtcNow() - start);
        return false;
    }
}
