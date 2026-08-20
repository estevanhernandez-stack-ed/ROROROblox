namespace ROROROblox.Core.Diagnostics;

/// <summary>How a stop ended.</summary>
public enum ClientStopOutcome
{
    /// <summary>Already gone before we asked. Nothing to do.</summary>
    AlreadyExited,

    /// <summary>Roblox closed itself — the user answered its confirm. Settings were persisted.</summary>
    ClosedItself,

    /// <summary>The grace ran out and we terminated it. Whatever was unsaved is gone.</summary>
    Forced,
}

/// <summary>
/// Runs a stop the way Roblox actually responds to one (F-111, F-109).
/// <para>
/// WHAT WAS WRONG. <c>Process.CloseMainWindow()</c> posts <c>WM_CLOSE</c> and returns whether the
/// message was POSTED, not whether the window closed. Roblox ignores <c>WM_CLOSE</c> outright —
/// measured against a real in-game client, alive and unchanged 30 seconds later — so the old guard
/// <c>if (!RequestClose(…)) Kill(…)</c> never escalated and the first click of Stop did nothing at
/// all. The second click worked only because <c>CloseMainWindow</c> happened to return false by
/// then.
/// </para>
/// <para>
/// WHAT ROBLOX DOES RESPOND TO is <c>WM_SYSCOMMAND</c>/<c>SC_CLOSE</c> — what clicking the X sends.
/// A client with no session closes on it. An in-game client raises Roblox's OWN confirm ("Close
/// Roblox" / "Back to Home") and waits for a human. That dialog is drawn by the Roblox engine
/// inside the game surface, not as an OS window, so nothing on this side can see it, click it, or
/// even detect that it exists — which is why the grace below is a person-answering-a-dialog budget
/// and not a window-teardown one.
/// </para>
/// <para>
/// We do not click that dialog. Driving a game client's UI is input automation, which is MaCro's
/// job and a wall this product keeps on purpose.
/// </para>
/// </summary>
public static class ClientStopSequence
{
    /// <summary>
    /// How long to let the user answer Roblox's confirm before forcing. Ten seconds: long enough to
    /// notice a dialog and click it, short enough that Stop still behaves like Stop.
    /// </summary>
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(10);

    /// <summary>How often the countdown ticks, and therefore how often exit is re-checked.</summary>
    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    /// <param name="hasExited">Re-read each tick. The ONLY honest answer to "did it close".</param>
    /// <param name="askToClose">Sends SC_CLOSE. Returns false if there is no window to send to.</param>
    /// <param name="forceKill">Last resort, once the grace is spent.</param>
    /// <param name="onSecondsRemaining">Row countdown. Called with the grace first, then each tick;
    /// called with 0 exactly once when the wait ends, so a caller can clear its own state.</param>
    /// <param name="delay">Injected so tests do not sleep.</param>
    public static async Task<ClientStopOutcome> RunAsync(
        Func<bool> hasExited,
        Func<bool> askToClose,
        Action forceKill,
        Action<int> onSecondsRemaining,
        Func<TimeSpan, CancellationToken, Task> delay,
        TimeSpan? grace = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(hasExited);
        ArgumentNullException.ThrowIfNull(askToClose);
        ArgumentNullException.ThrowIfNull(forceKill);
        ArgumentNullException.ThrowIfNull(onSecondsRemaining);
        ArgumentNullException.ThrowIfNull(delay);

        if (hasExited()) return ClientStopOutcome.AlreadyExited;

        var budget = grace ?? DefaultGrace;

        // A zero grace is "force stop" — the user opted out of waiting. Still ASK first: if Roblox
        // takes it immediately (no session, or the confirm suppressed via "Don't Show Again") the
        // clean exit is free and its settings survive.
        askToClose();

        var remaining = (int)Math.Ceiling(budget.TotalSeconds);
        onSecondsRemaining(remaining);

        while (remaining > 0)
        {
            if (ct.IsCancellationRequested) break;

            await delay(Tick, ct).ConfigureAwait(false);

            if (hasExited())
            {
                onSecondsRemaining(0);
                return ClientStopOutcome.ClosedItself;
            }

            remaining--;

            // Zero is emitted once, below, on whichever path ends the wait. Emitting it here too
            // would tell the row "done" twice — caught by TheCountdownWalksFromTenToZeroWithoutSkipping,
            // which is the test that exists because a countdown that stutters is worse than none.
            if (remaining > 0) onSecondsRemaining(remaining);
        }

        // Re-check before killing. The exit can land between the last tick and here, and killing a
        // process that already closed cleanly would be indistinguishable in the log from killing a
        // live one — it would make the outcome a lie rather than a mistake.
        if (hasExited())
        {
            onSecondsRemaining(0);
            return ClientStopOutcome.ClosedItself;
        }

        forceKill();
        onSecondsRemaining(0);
        return ClientStopOutcome.Forced;
    }
}
