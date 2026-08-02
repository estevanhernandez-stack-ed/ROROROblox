using Microsoft.Extensions.Logging;

namespace ROROROblox.Core;

/// <summary>How a per-account FPS cap ended up being applied (or not).</summary>
public enum FpsCapSettleOutcome
{
    /// <summary>The file already held this cap. Nothing written, nothing waited for.</summary>
    AlreadySet,

    /// <summary>Written, and confirmed still present after the confirm window.</summary>
    Settled,

    /// <summary>Every attempt was overwritten. Launching anyway, with a cap that may be wrong.</summary>
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
/// Correctness here comes from <em>confirming</em> the write, not from <see cref="QuietDebounce"/>
/// being the right length. If the debounce is too short we notice the clobber and retry; the cost
/// is latency, not a wrong cap. Guessing exactly this class of constant is what produced the
/// previous design's 1-second settle grace.
/// </para>
/// </summary>
public static class FpsCapSettler
{
    /// <summary>
    /// How long the file must be unmodified before we call it quiet. Must exceed the largest gap
    /// observed BETWEEN a client's own writes (3.25 s on 2026-08-02) with margin.
    /// </summary>
    internal static readonly TimeSpan QuietDebounce = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long to wait before re-reading to confirm our write survived. The observed clobber
    /// arrived 170 ms after our write; 1 s covers that with headroom.
    /// </summary>
    internal static readonly TimeSpan WriteConfirmWindow = TimeSpan.FromSeconds(1);

    /// <summary>Ceiling on waiting for quiet. A contended file must never block a launch forever.</summary>
    internal static readonly TimeSpan QuietWaitTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How often to re-check the file's last-write time.</summary>
    internal static readonly TimeSpan QuietPollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>Bounds the worst case at roughly 3 x (QuietDebounce + WriteConfirmWindow).</summary>
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

        for (var attempt = 1; attempt <= MaxWriteAttempts; attempt++)
        {
            var wentQuiet = await WaitForQuietAsync(probe, timeProvider, ct).ConfigureAwait(false);
            if (!wentQuiet)
            {
                logger.LogInformation(
                    "Settings file never went quiet within {Timeout}; writing FPS cap {Cap} anyway (attempt {Attempt}).",
                    QuietWaitTimeout, desiredCap, attempt);
            }

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

            await Task.Delay(WriteConfirmWindow, timeProvider, ct).ConfigureAwait(false);

            if (probe.ReadFramerateCap() == desiredCap)
            {
                return FpsCapSettleOutcome.Settled;
            }

            logger.LogWarning(
                "FPS cap {Cap} was overwritten within {Window} (attempt {Attempt} of {Max}) — a client is still settling.",
                desiredCap, WriteConfirmWindow, attempt, MaxWriteAttempts);
        }

        // Out of attempts. Launch anyway: a contended settings file must never abort a launch.
        // This is the ONLY path where the original wrong-cap bug can still reach a user, so it is
        // logged at Error to make it impossible to miss in a support bundle.
        logger.LogError(
            "Gave up applying FPS cap {Cap} after {Max} attempts; this client may run the wrong cap.",
            desiredCap, MaxWriteAttempts);
        return FpsCapSettleOutcome.Exhausted;
    }

    /// <summary>
    /// Block until the settings file has been unmodified for <see cref="QuietDebounce"/>.
    /// Returns false if <see cref="QuietWaitTimeout"/> elapses first — the caller proceeds anyway.
    /// </summary>
    private static async Task<bool> WaitForQuietAsync(
        IGlobalBasicSettingsProbe probe,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var deadline = timeProvider.GetUtcNow() + QuietWaitTimeout;
        var lastSeen = probe.GetLastWriteTimeUtc();
        var quietSince = timeProvider.GetUtcNow();

        while (timeProvider.GetUtcNow() < deadline)
        {
            if (timeProvider.GetUtcNow() - quietSince >= QuietDebounce)
            {
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

        return false;
    }
}
