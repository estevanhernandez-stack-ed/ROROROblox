namespace ROROROblox.Core;

/// <summary>
/// Owns the OS handle for a named Windows kernel object. The product binds this to
/// <c>Local\ROBLOX_singletonEvent</c> — winning that name defeats Roblox's single-instance
/// check and enables multiple clients side by side. See spec §5.1, and
/// <see cref="MutexAcquireOutcome"/> for why "winning the name" is the accurate framing.
/// </summary>
public interface IMutexHolder
{
    /// <summary>
    /// The resolved name of the singleton mutex this holder owns (config-driven, falling back to
    /// <c>Local\ROBLOX_singletonEvent</c>). Exposed so the plugin host can hand the same name to a
    /// future add-to-already-running plugin — it must close the exact handle the app holds.
    /// </summary>
    string MutexName { get; }
    bool IsHeld { get; }
    bool Acquire();
    void Release();

    /// <summary>
    /// Non-acquiring probe: true iff the singleton name is currently owned by someone other than
    /// this holder — Roblox (as an Event) or a compatible tool (as a Mutex). Returns false when we
    /// hold it or when nobody does. Does not acquire, wait, or mutate any handle.
    /// </summary>
    bool IsHeldElsewhere();

    /// <summary>
    /// Attempt the name once and report WHY, instead of collapsing "Roblox has it" and "a
    /// compatible tool has it" into a bare false — they call for opposite user-facing behavior.
    /// <para>The default implementation preserves the boolean contract so existing fakes keep
    /// working; <see cref="MutexHolder"/> overrides it with the real error-code mapping.</para>
    /// </summary>
    MutexAcquireOutcome TryAcquire()
        => Acquire() ? MutexAcquireOutcome.Acquired : MutexAcquireOutcome.HeldByRoblox;

    /// <summary>
    /// Poll <see cref="TryAcquire"/> until it succeeds or <paramref name="window"/> elapses.
    ///
    /// <para>A single instantaneous attempt is why "quit Roblox, then hit Retry" so often needed a
    /// second press: the kernel object dies only when its LAST handle closes, and a
    /// just-terminated RobloxPlayerBeta takes a beat to tear down. Polling absorbs that lag.</para>
    ///
    /// <para>Returns early on <see cref="MutexAcquireOutcome.HeldByCompatibleTool"/> — a peer tool
    /// holding the name is a stable state, not a race, so waiting it out accomplishes nothing.</para>
    /// </summary>
    async Task<MutexAcquireOutcome> TryAcquireWithRetryAsync(
        TimeSpan window,
        TimeSpan interval,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + window;
        while (true)
        {
            var outcome = TryAcquire();
            if (outcome is MutexAcquireOutcome.Acquired or MutexAcquireOutcome.HeldByCompatibleTool)
            {
                return outcome;
            }
            if (DateTime.UtcNow >= deadline || cancellationToken.IsCancellationRequested)
            {
                return outcome;
            }
            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        }
    }

    event EventHandler? MutexLost;
}
