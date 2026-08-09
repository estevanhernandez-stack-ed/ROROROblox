namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Answers "will the next launch fit?" before it happens (F-082, the preventive half).
/// <para>
/// Everything else in the watchdog is retrospective — it tells you that you are already out of
/// room. This is the only piece that speaks before the memory is spent, which is the difference
/// between a warning and a post-mortem.
/// </para>
/// <para>
/// It ADVISES. It does not block. A launch that is refused on a forecast is worse than a launch
/// that goes ahead on a warning, because the forecast is a constant derived from one machine and
/// the user is looking at their own. Contrast the mutex modal, which does hard-block: there the
/// failure is deterministic and has exactly one recovery. This is a prediction, and predictions
/// should not hold a veto over somebody's own hardware.
/// </para>
/// </summary>
public static class LaunchHeadroomAdvisor
{
    private const long Mb = 1024L * 1024;

    public enum Verdict
    {
        /// <summary>Room for everything asked for. Say nothing.</summary>
        Fits,

        /// <summary>Some will fit, not all. Worth a word before a batch launch.</summary>
        Partial,

        /// <summary>Not even one more fits. The machine is already at its limit.</summary>
        WontFit,

        /// <summary>The memory probe failed. UNKNOWN is not "fine" — but it is not a warning either.</summary>
        Unknown,
    }

    /// <param name="requested">How many clients the user is about to launch (1 for Launch As).</param>
    /// <returns>
    /// The verdict plus <c>Fits</c> = how many actually have room. The caller shows the numbers;
    /// this decides nothing about UI.
    /// </returns>
    public static (Verdict Verdict, int RoomFor) Evaluate(
        bool probeOk, long availableBytes, long reserveBytes, int requested)
    {
        if (!probeOk || requested <= 0)
        {
            // A failed read is UNKNOWN, never "plenty of room". Same rule the watchdog follows for
            // an unreadable pid: silence beats a confident wrong number.
            return (Verdict.Unknown, 0);
        }

        var spare = availableBytes - reserveBytes;
        var roomFor = spare <= 0 ? 0 : (int)(spare / (MemoryDefaults.ExpectedClientMb * Mb));

        if (roomFor >= requested) return (Verdict.Fits, roomFor);
        return (roomFor <= 0 ? Verdict.WontFit : Verdict.Partial, roomFor);
    }
}
