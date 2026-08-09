using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// F-082 — the arithmetic, encoded so it cannot quietly come undone.
/// <para>
/// RoRoRo watched memory and said nothing to the one user who needed it: the person running ten
/// accounts on sixteen gigabytes. Both existing triggers were structurally unable to fire there.
/// The per-client cap was 35% of installed RAM, so it scaled UP with memory and never crossed at
/// any tier from 16 GB; the projection needed growth that plateaued clients do not produce.
/// </para>
/// <para>
/// Numbers come from live measurement on Roblox 733, 2026-08-07, 8 concurrent clients in Pet Sim
/// 99: median 2650 MB per client, peak 3280 MB.
/// </para>
/// </summary>
public class MemoryHeadroomTests
{
    private const long Gb = 1024L * 1024 * 1024;
    private const long Mb = 1024L * 1024;

    /// <summary>Windows plus the browser/Discord/etc a real user has open.</summary>
    private const long OsOverhead = 3500 * Mb;

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(64)]
    public void ThePerClientCapNeverScalesUpWithInstalledRam(int totalGb)
    {
        // THE BUG, stated as a property. `Math.Max(35%, 4 GB)` gave 5734 MB at 16 GB and 16886 MB
        // at 47 GB — a cap that got MORE permissive the more memory you owned, against a client
        // that peaks near 3280 MB. Owning 64 GB does not make one client's 3 GB acceptable.
        var cap = MemoryDefaults.CapMb((long)totalGb * Gb);

        Assert.True(cap <= 4096,
            $"{totalGb} GB derived a {cap} MB per-client cap. This axis is anomaly detection and "
            + "must not grow with installed RAM — that is exactly the F-082 defect.");
    }

    [Fact]
    public void ASmallMachineGetsAStricterCapNotALooserOne()
    {
        // Min, not Max: the fraction may only ever tighten the cap. 8 GB lands under the flat
        // anomaly line, which is the whole point of clamping downward.
        Assert.True(MemoryDefaults.CapMb(8 * Gb) < MemoryDefaults.CapMb(16 * Gb));
        Assert.Equal(MemoryDefaults.CapMb(32 * Gb), MemoryDefaults.CapMb(64 * Gb));
    }

    [Fact]
    public void AHealthyClientNeverTripsTheAnomalyCap()
    {
        // 3280 MB was the highest single client we measured. If the cap sat below that, every
        // normal session would warn and the feature would be muted within a day.
        Assert.True(MemoryDefaults.CapMb(16 * Gb) > 3280, "a measured-healthy client must not warn");
    }

    [Theory]
    // The case that reported this, and the case that is silent today.
    [InlineData(16, 10, true)]
    [InlineData(16, 8, true)]
    // Four fits on 16 GB at the measured footprint; that user is fine.
    [InlineData(16, 4, false)]
    // This machine on 2026-08-07 — 8 clients on 47 GB, measured, healthy, must stay quiet.
    [InlineData(48, 8, false)]
    [InlineData(32, 10, false)]
    [InlineData(8, 2, true)]
    public void HeadroomWarnsExactlyWhenTheClientsDoNotFit(int totalGb, int clients, bool shouldWarn)
    {
        var total = (long)totalGb * Gb;
        var reserve = (long)MemoryDefaults.ReserveMb(total) * Mb;
        var held = clients * (long)MemoryDefaults.ExpectedClientMb * Mb;
        var available = total - OsOverhead - held;

        // Physically impossible to be more than exhausted; Windows reports 0, not negative.
        if (available < 0) available = 0;

        var belowReserve = available < reserve;

        Assert.Equal(shouldWarn, belowReserve);
    }

    [Fact]
    public void TheOldFormulaWouldHaveFailedTheSixteenGigCase()
    {
        // Guards the regression directly rather than trusting the new formula's shape. If anyone
        // re-derives the cap from a percentage of RAM, this is the test that explains why not.
        var total = 16 * Gb;
        const long clientPeak = 3280 * Mb;

        var oldCap = Math.Max((long)(total * 0.35 / Mb), 4096) * Mb;
        var newCap = (long)MemoryDefaults.CapMb(total) * Mb;

        Assert.True(clientPeak < oldCap, "premise: the old cap could not fire on 16 GB");
        Assert.True(newCap < oldCap, "the new cap must be strictly tighter on 16 GB");
    }

    [Fact]
    public void PreLaunchRefusesToPromiseRoomThatIsNotThere()
    {
        var reserve = (long)MemoryDefaults.ReserveMb(16 * Gb) * Mb;

        // Room for one more.
        Assert.True(MemoryDefaults.AnotherClientFits(reserve + 4000 * Mb, reserve));
        // Not enough for a client's expected footprint on top of the reserve.
        Assert.False(MemoryDefaults.AnotherClientFits(reserve + 500 * Mb, reserve));
        // Already under the reserve — nothing fits, and the arithmetic must not go negative-happy.
        Assert.False(MemoryDefaults.AnotherClientFits(reserve / 2, reserve));
    }

    [Fact]
    public void BelowReserveIsNotClear_EvenWithNoGrowthAndNoFatClient()
    {
        // The exact silent case: every client normal, nothing growing, machine out of room.
        // Before F-082 this returned "clear" and the user got no warning at all.
        var snapshot = new MemoryPressureSnapshot(
            AvailableBytes: 400 * Mb,
            AggregateGrowthBytesPerHour: 0,
            MinutesToCeiling: 0,
            HasProjection: false,
            TargetAccountId: null,
            Accounts: [],
            AggregateClientBytes: 26500 * Mb,
            BelowReserve: true);

        Assert.False(MemoryPressureEvaluator.IsClear(snapshot, projectionWarnMinutes: 120));
    }

    [Fact]
    public void AHealthyMachineStillReadsClear()
    {
        // The other half of the guarantee: adding an axis must not make a fine machine warn.
        var snapshot = new MemoryPressureSnapshot(
            AvailableBytes: 20 * Gb,
            AggregateGrowthBytesPerHour: 0,
            MinutesToCeiling: 0,
            HasProjection: false,
            TargetAccountId: null,
            Accounts: [],
            AggregateClientBytes: 21 * Gb,
            BelowReserve: false);

        Assert.True(MemoryPressureEvaluator.IsClear(snapshot, projectionWarnMinutes: 120));
    }

    [Fact]
    public void AnExistingSnapshotCallSiteStillCompilesAndReadsClear()
    {
        // The two new fields are optional on purpose — MemoryPressureSnapshot is constructed in
        // several places and a required parameter would have been a breaking change for a value
        // most callers cannot compute.
        var snapshot = new MemoryPressureSnapshot(8 * Gb, 0, 0, false, null, []);

        Assert.False(snapshot.BelowReserve);
        Assert.Equal(0, snapshot.AggregateClientBytes);
        Assert.True(MemoryPressureEvaluator.IsClear(snapshot, projectionWarnMinutes: 120));
    }

    [Fact]
    public void TheFooterShowsWhatTheClientsCostTogether()
    {
        // F-080. The number that needed a PowerShell probe on 2026-08-07 while the watchdog had
        // been sampling it every 15 seconds.
        var text = ROROROblox.App.ViewModels.MemoryChipFormatter.FormatFooter(
            clientCount: 6, aggregateBytes: 16 * Gb + 200 * Mb, belowReserve: false);

        Assert.Equal("6 Roblox clients running · 16.2 GB", text);
    }

    [Fact]
    public void TheFooterMarksAnOutOfRoomMachine()
    {
        var text = ROROROblox.App.ViewModels.MemoryChipFormatter.FormatFooter(
            clientCount: 10, aggregateBytes: 26 * Gb, belowReserve: true);

        Assert.StartsWith("10 Roblox clients running · ▲", text);
    }

    [Fact]
    public void TheFooterSaysNothingAboutMemoryItDoesNotHave()
    {
        // "0.0 GB" reads as a measurement when it is really the absence of one. Before the first
        // sample lands, and with nothing running, the line stays as it always was.
        Assert.Equal("No Roblox clients running",
            ROROROblox.App.ViewModels.MemoryChipFormatter.FormatFooter(0, 0, false));
        Assert.Equal("2 Roblox clients running",
            ROROROblox.App.ViewModels.MemoryChipFormatter.FormatFooter(2, 0, false));
        Assert.Equal("1 Roblox client running",
            ROROROblox.App.ViewModels.MemoryChipFormatter.FormatFooter(1, 0, false));
    }

    [Theory]
    // 16 GB with ten clients already up: nothing fits, and the batch must be warned about.
    [InlineData(400, 10, LaunchHeadroomAdvisor.Verdict.WontFit, 0)]
    // Room for two more, asked for six — partial, still worth saying before starting six.
    [InlineData(7900, 6, LaunchHeadroomAdvisor.Verdict.Partial, 2)]
    // Plenty: say nothing at all.
    [InlineData(30000, 4, LaunchHeadroomAdvisor.Verdict.Fits, 10)]
    public void ThePreLaunchAdvisorSeesTheBatchBeforeItStarts(
        int availableMb, int requested, LaunchHeadroomAdvisor.Verdict expected, int expectedRoom)
    {
        var reserve = (long)MemoryDefaults.ReserveMb(16 * Gb) * Mb;
        var (verdict, roomFor) = LaunchHeadroomAdvisor.Evaluate(
            probeOk: true, availableBytes: availableMb * Mb, reserveBytes: reserve, requested: requested);

        Assert.Equal(expected, verdict);
        Assert.Equal(expectedRoom, roomFor);
    }

    [Fact]
    public void AFailedProbeIsUnknownAndNeverAWarning()
    {
        // The watchdog writes AvailableBytes: 0 when the read fails, so a zero is ambiguous.
        // Treating it as "no memory" would fire this dialog on every batch launch for anyone whose
        // probe is broken — the fastest possible route to the warning being ignored.
        var (verdict, room) = LaunchHeadroomAdvisor.Evaluate(
            probeOk: false, availableBytes: 0, reserveBytes: 1 * Gb, requested: 4);

        Assert.Equal(LaunchHeadroomAdvisor.Verdict.Unknown, verdict);
        Assert.Equal(0, room);
    }

    [Fact]
    public void AZeroAvailableReadingIsTreatedAsUnknownNotAsEmpty()
    {
        // The watchdog collapses "probe failed" and "genuinely zero" into AvailableBytes: 0, so the
        // caller maps a zero to probeOk:false. Pinning that mapping here, because the alternative —
        // reading zero as "no memory left" — fires the dialog on every batch launch for anyone
        // whose probe is broken, and a warning that always fires is one nobody reads.
        var available = 0L;
        var (verdict, _) = LaunchHeadroomAdvisor.Evaluate(
            probeOk: available > 0, availableBytes: available, reserveBytes: 1 * Gb, requested: 1);

        Assert.Equal(LaunchHeadroomAdvisor.Verdict.Unknown, verdict);
    }
}
