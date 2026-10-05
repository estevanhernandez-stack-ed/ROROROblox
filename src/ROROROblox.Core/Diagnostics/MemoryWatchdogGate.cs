namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Owns whether the memory watchdog is sampling, so "Watch memory while accounts are running"
/// means this session and not the next one.
/// <para>
/// Before this existed the setting was read once at startup and decided only whether
/// <see cref="IMemoryWatchdog.Start"/> ran; <see cref="IMemoryWatchdog.Stop"/> had no production
/// caller at all. Unticking the box mid-session persisted the value and changed nothing, which is
/// how a memory warning reached Este on 2026-10-05 with the box already clear.
/// </para>
/// <para>
/// <b>Why stop the timer rather than filter the alerts.</b> Three subscribers hang off
/// <see cref="IMemoryWatchdog.PressureCrossed"/> — the alert builder, the tray badge and the plugin
/// gRPC stream — and the last of those is not ours to filter on a user's behalf. Stopping the
/// sampler is the one version of "off" that holds for all three, and it is also what the label
/// promises: the watching stops.
/// </para>
/// <para>
/// <b>Why the generation counter.</b> Two writers reach this: the Settings toggle, which nudges
/// immediately after persisting, and a periodic re-read of settings.json that exists so a
/// hand-edited file is still honoured. The slow one must not overwrite the fast one. A tick that
/// read <c>true</c> a moment before the user unticked the box would otherwise restart the watchdog
/// seconds after an explicit opt-out — the same interleaving <c>MetricAlertsGateTests</c> records
/// for the metric opt-in, and the same fix: capture the generation before the read starts, and
/// compare-then-assign under one lock so a nudge cannot slip between the two steps.
/// </para>
/// <para>
/// The lock wraps <see cref="IMemoryWatchdog.Start"/>/<see cref="IMemoryWatchdog.Stop"/>, which are
/// both cheap and idempotent (a <see cref="System.Threading.Timer"/> created with <c>??=</c> and
/// disposed without waiting), so it is never held across anything that blocks. The settings read
/// itself happens outside it — that is precisely why the generation is needed.
/// </para>
/// </summary>
public sealed class MemoryWatchdogGate(IMemoryWatchdog watchdog)
{
    private readonly object _lock = new();
    private int _generation;
    private bool _on;

    /// <summary>Whether the watchdog is sampling right now.</summary>
    public bool IsOn
    {
        get { lock (_lock) return _on; }
    }

    /// <summary>
    /// Applies a value the caller has already persisted, and outranks any read in flight. The
    /// generation bumps whatever the value: a nudge that writes what the gate already held still
    /// makes an in-flight read stale.
    /// </summary>
    public void Apply(bool enabled)
    {
        lock (_lock)
        {
            _generation++;
            ApplyCore(enabled);
        }
    }

    /// <summary>
    /// The generation as of now, handed back to <see cref="TryCommit"/>. Captured immediately
    /// before a read of the setting begins; everything between here and the commit is the window a
    /// nudge is allowed to win.
    /// </summary>
    public int BeginRead()
    {
        lock (_lock) return _generation;
    }

    /// <summary>
    /// Commits a value read from disk, unless a nudge landed while the read was in flight.
    /// Returns whether it landed, so a caller can log the drop.
    /// </summary>
    public bool TryCommit(int generation, bool enabled)
    {
        lock (_lock)
        {
            if (generation != _generation) return false;
            ApplyCore(enabled);
            return true;
        }
    }

    private void ApplyCore(bool enabled)
    {
        // Idempotent by state, not just by Start/Stop being idempotent themselves: re-applying the
        // value the gate already holds must not re-arm a sampler mid-interval.
        if (enabled == _on) return;

        if (enabled)
        {
            watchdog.Start();
        }
        else
        {
            watchdog.Stop();
        }

        _on = enabled;
    }
}
