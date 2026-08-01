using System;
using System.Collections.Generic;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// Pins <see cref="MemoryPressureEvaluator.IsClear"/> -- the pure predicate MainViewModel's 30s
/// ticker uses to clear the tray's memory-warning badge once PressureCrossed's edge-triggered
/// "something crossed" signal has gone quiet.
/// </summary>
public class MemoryPressureEvaluatorTests
{
    private static AccountMemory Account(bool overCap, bool readOk = true) =>
        new(Guid.NewGuid(), PrivateBytes: 0, GrowthBytesPerHour: 0, MinutesToCeiling: 0, OverCap: overCap, IsTarget: false, ReadOk: readOk);

    [Fact]
    public void IsClear_NoAccountsNoProjection_ReturnsTrue()
    {
        var snapshot = new MemoryPressureSnapshot(0, 0, 0, HasProjection: false, null, Array.Empty<AccountMemory>());

        Assert.True(MemoryPressureEvaluator.IsClear(snapshot, projectionWarnMinutes: 120));
    }

    [Fact]
    public void IsClear_AccountOverCap_ReturnsFalse_EvenWithNoProjection()
    {
        // The exact gotcha this predicate exists to avoid: HasProjection == false must never be
        // read as "cleared" when an account is still over cap -- the two triggers are independent.
        // Would fail if the production code short-circuited on `!HasProjection` before checking cap.
        var snapshot = new MemoryPressureSnapshot(
            0, 0, 0, HasProjection: false, null, new List<AccountMemory> { Account(overCap: true) });

        Assert.False(MemoryPressureEvaluator.IsClear(snapshot, projectionWarnMinutes: 120));
    }

    [Fact]
    public void IsClear_ProjectionBelowThreshold_ReturnsFalse()
    {
        // Would fail if the comparison were dropped or inverted.
        var snapshot = new MemoryPressureSnapshot(
            0, 0, MinutesToCeiling: 5, HasProjection: true, null, new List<AccountMemory> { Account(overCap: false) });

        Assert.False(MemoryPressureEvaluator.IsClear(snapshot, projectionWarnMinutes: 120));
    }

    [Fact]
    public void IsClear_ProjectionAtThreshold_ReturnsTrue()
    {
        // Boundary case: MemoryWatchdog's own latch fires on `minutes < ProjectionWarnMinutes`, so
        // the clear condition's negation must be `>=`, not `>`. Would fail if the operator were `>`.
        var snapshot = new MemoryPressureSnapshot(
            0, 0, MinutesToCeiling: 120, HasProjection: true, null, new List<AccountMemory> { Account(overCap: false) });

        Assert.True(MemoryPressureEvaluator.IsClear(snapshot, projectionWarnMinutes: 120));
    }

    [Fact]
    public void IsClear_ProjectionAboveThreshold_ReturnsTrue()
    {
        var snapshot = new MemoryPressureSnapshot(
            0, 0, MinutesToCeiling: 500, HasProjection: true, null, new List<AccountMemory> { Account(overCap: false) });

        Assert.True(MemoryPressureEvaluator.IsClear(snapshot, projectionWarnMinutes: 120));
    }

    [Fact]
    public void IsClear_CapClearButProjectionStillLow_ReturnsFalse()
    {
        // Would fail if the two conditions were OR'd instead of AND'd.
        var snapshot = new MemoryPressureSnapshot(
            0, 0, MinutesToCeiling: 10, HasProjection: true, null, new List<AccountMemory> { Account(overCap: false) });

        Assert.False(MemoryPressureEvaluator.IsClear(snapshot, projectionWarnMinutes: 120));
    }

    [Fact]
    public void IsClear_ProjectionFineButOneAccountOverCap_ReturnsFalse()
    {
        // Would fail if the loop stopped at the first account instead of scanning all of them,
        // or if cap-checking were skipped whenever a projection happens to look fine.
        var accounts = new List<AccountMemory> { Account(overCap: false), Account(overCap: true) };
        var snapshot = new MemoryPressureSnapshot(0, 0, MinutesToCeiling: 500, HasProjection: true, null, accounts);

        Assert.False(MemoryPressureEvaluator.IsClear(snapshot, projectionWarnMinutes: 120));
    }
}
