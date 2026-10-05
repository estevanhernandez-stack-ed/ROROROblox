using System;
using System.Collections.Generic;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// "Watch memory while accounts are running" has to mean this session, not the next one.
/// <para>
/// WHAT BROKE. Reported by Este 2026-10-05, from his other machine: the box was unticked and a
/// memory warning still arrived. <c>MemoryWatchdogEnabled</c> was read exactly once, at startup
/// (<c>App.WireMemoryWatchdogAsync</c>), and all it decided was whether <c>Start()</c> created the
/// 30s timer. <c>Stop()</c> had no production call site at all, and the three
/// <see cref="IMemoryWatchdog.PressureCrossed"/> subscribers — the alert builder, the tray badge
/// and the plugin stream — were wired unconditionally and never re-read the flag. So unticking the
/// box persisted the setting and changed nothing until the next launch, with no UI hint saying so.
/// Ticking it on mid-session was equally inert.
/// </para>
/// <para>
/// WHY A GATE AND NOT AN <c>if</c> AT THE RAISE SITE. Gating the subscribers would leave the timer
/// sampling every 30 seconds to feed chips and a badge the user just said they did not want, and
/// "watch memory" off has to mean the watching stops. Stopping the timer is also the only version
/// that holds for the plugin stream, which is not ours to filter.
/// </para>
/// <para>
/// WHY THE GENERATION COUNTER. Same two-writer race <see cref="MetricAlertsGateTests"/> documents:
/// a periodic re-read of settings.json and the Settings toggle both write this, and the slow one
/// must not overwrite the fast one. A tick that read <c>true</c> before the user unticked the box
/// would otherwise restart the watchdog seconds after an explicit opt-out.
/// </para>
/// </summary>
public class MemoryWatchdogGateTests
{
    private sealed class SpyWatchdog : IMemoryWatchdog
    {
        public int Starts { get; private set; }
        public int Stops { get; private set; }

        public int ExpectedClientMb => 0;
        public long CapBytes { get; set; }
        public long ReserveBytes { get; set; }
        public int ProjectionWarnMinutes { get; set; }
        public event EventHandler<MemoryPressureSnapshot>? PressureCrossed;
        public void OnAccountLaunched(Guid accountId, int pid) { }
        public void OnAccountExited(Guid accountId, int pid) { }
        public void ResetBaseline(Guid accountId, int pid) { }
        public void Start() => Starts++;
        public void Stop() => Stops++;
        public void Sample() => PressureCrossed?.Invoke(this, GetSnapshot());
        public MemoryPressureSnapshot GetSnapshot() => new(0, 0, 0, false, null, []);
    }

    [Fact]
    public void UntickingTheBox_StopsTheWatchdogInThisSession()
    {
        var watchdog = new SpyWatchdog();
        var gate = new MemoryWatchdogGate(watchdog);
        gate.Apply(enabled: true);

        gate.Apply(enabled: false);

        Assert.Equal(1, watchdog.Stops);
        Assert.False(gate.IsOn);
    }

    [Fact]
    public void TickingItBackOn_StartsItAgainWithoutARelaunch()
    {
        var watchdog = new SpyWatchdog();
        var gate = new MemoryWatchdogGate(watchdog);

        gate.Apply(enabled: true);
        gate.Apply(enabled: false);
        gate.Apply(enabled: true);

        Assert.Equal(2, watchdog.Starts);
        Assert.True(gate.IsOn);
    }

    [Fact]
    public void AStaleReadCannotRevive_TheWatchdogTheUserJustTurnedOff()
    {
        var watchdog = new SpyWatchdog();
        var gate = new MemoryWatchdogGate(watchdog);
        gate.Apply(enabled: true);

        // A periodic re-read of settings.json begins. The file still says true at this instant.
        var generation = gate.BeginRead();

        // The user unticks the box: the setting is written and the gate nudged.
        gate.Apply(enabled: false);

        // The read resumes holding the value it picked up a moment ago. It must be dropped.
        Assert.False(gate.TryCommit(generation, enabled: true),
            "a read that began before the nudge is stale by definition.");
        Assert.False(gate.IsOn,
            "the opt-out stands; this is the warning the fix exists to prevent.");
        Assert.Equal(1, watchdog.Starts);
    }

    [Fact]
    public void WithNoNudge_APeriodicReadCommitsNormally()
    {
        var watchdog = new SpyWatchdog();
        var gate = new MemoryWatchdogGate(watchdog);

        var generation = gate.BeginRead();

        Assert.True(gate.TryCommit(generation, enabled: true));
        Assert.True(gate.IsOn);
        Assert.Equal(1, watchdog.Starts);
    }

    [Fact]
    public void ApplyingTheSameValueTwice_DoesNotRestartTheWatchdog()
    {
        var watchdog = new SpyWatchdog();
        var gate = new MemoryWatchdogGate(watchdog);

        gate.Apply(enabled: true);
        gate.Apply(enabled: true);

        Assert.Equal(1, watchdog.Starts);
        Assert.Equal(0, watchdog.Stops);
    }
}
