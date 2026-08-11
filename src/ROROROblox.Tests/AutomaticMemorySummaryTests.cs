using System.IO;
using System.Text.RegularExpressions;
using ROROROblox.App.Preferences;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// The Memory section's blank boxes explain themselves, and the explanation is the truth.
/// <para>
/// WHAT THIS IS FOR. Item 3 made three numeric memory fields accept blank to mean "RoRoRo picks
/// one" and never said what it picked. Item 4a says it. The only way that helps is if the number on
/// screen is the number the watchdog runs with — a display that can drift is worse than a blank
/// box, because a blank box at least does not lie.
/// </para>
/// <para>
/// SO THE ASSERTIONS ARE LITERALS, NOT CALLS. Every expected figure below is computed by hand from
/// <see cref="MemoryDefaults"/>' documented rules and written out. Asserting
/// <c>summary.ReserveMb == MemoryDefaults.ReserveMb(total)</c> would be circular — the class under
/// test calls exactly that, so the comparison would hold no matter what either side did. A literal
/// is the only version that can fail when the derivation moves.
/// </para>
/// </summary>
public class AutomaticMemorySummaryTests
{
    /// <summary>
    /// Substitutable exactly as the shipped probe is: <see cref="ISystemMemoryProbe"/> comes from
    /// DI (<c>App.xaml.cs:717</c>) and reaches the window through its constructor, so nothing in
    /// this file constructs a <c>SystemMemoryProbe</c> or reads real hardware. A test that measured
    /// the machine it runs on would assert a different thing on every box.
    /// </summary>
    private sealed class FakeSystemMemory(bool result, long total, long available = 0)
        : ISystemMemoryProbe
    {
        public bool TryRead(out long totalPhysicalBytes, out long availablePhysicalBytes)
        {
            totalPhysicalBytes = total;
            availablePhysicalBytes = available;
            return result;
        }
    }

    private const long Gib = 1024L * 1024 * 1024;

    private static AutomaticMemorySummary.Resolved Describe(bool result, long total) =>
        new AutomaticMemorySummary(new FakeSystemMemory(result, total)).Describe();

    /// <summary>
    /// Hand-computed, one row per RAM tier the clan actually runs.
    /// <para>
    /// Reserve is 8% of installed bytes, integer-truncated to MB, clamped to [1024, 4096]:
    /// 16 GiB -&gt; 17179869184 * 0.08 / 1048576 = 1310.72 -&gt; 1310.
    /// 32 GiB -&gt; 34359738368 * 0.08 / 1048576 = 2621.44 -&gt; 2621.
    /// 64 GiB -&gt; 68719476736 * 0.08 / 1048576 = 5242.88 -&gt; 5242, over the ceiling -&gt; 4096.
    /// 8 GiB  -&gt; 8589934592 * 0.08 / 1048576 = 655.36 -&gt; 655, under the floor -&gt; 1024.
    /// </para>
    /// <para>
    /// Cap is 4096 clamped DOWNWARD by 35% (F-082 — Min, not Max):
    /// 16 GiB -&gt; 5734 -&gt; 4096. 32 GiB -&gt; 11468 -&gt; 4096. 64 GiB -&gt; 22937 -&gt; 4096.
    /// 8 GiB -&gt; 2867, the one tier where the fraction bites, and the figure
    /// <see cref="MemoryDefaults.CapMb"/>'s own doc quotes.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(8L, 1024, 2867)]
    [InlineData(16L, 1310, 4096)]
    [InlineData(32L, 2621, 4096)]
    [InlineData(64L, 4096, 4096)]
    public void TheStatedFiguresAreTheOnesTheWatchdogWouldRunWith(long gib, int reserveMb, int capMb)
    {
        var resolved = Describe(result: true, total: gib * Gib);

        Assert.True(resolved.RamKnown);
        Assert.Equal(reserveMb, resolved.ReserveMb);
        Assert.Equal(capMb, resolved.CapMb);

        // And the numbers reach the sentence. A record carrying the right figures while the text
        // says something else is the same defect one layer out.
        Assert.Contains($"{reserveMb} MB kept free", resolved.Text, StringComparison.Ordinal);
        Assert.Contains($"passes {capMb} MB", resolved.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The startup path derives from whatever the probe left in its <c>out</c>, bool discarded
    /// (<c>App.xaml.cs:1169</c>). This proves the display does the same, which is the whole
    /// no-second-derivation claim reduced to one comparison an implementation change can break.
    /// </summary>
    [Theory]
    [InlineData(8L)]
    [InlineData(16L)]
    [InlineData(32L)]
    [InlineData(64L)]
    public void TheDisplayDerivesFromTheSameCallTheStartupPathMakes(long gib)
    {
        var total = gib * Gib;
        var resolved = Describe(result: true, total: total);

        // Not circular the way the literals above would be if they were calls: this asserts the
        // EQUIVALENCE of two paths, and it is the one assertion that keeps holding if the rules
        // inside MemoryDefaults are ever revised.
        Assert.Equal(MemoryDefaults.ReserveMb(total), resolved.ReserveMb);
        Assert.Equal(MemoryDefaults.CapMb(total), resolved.CapMb);
    }

    [Theory]
    [InlineData(16L, "16 GB")]
    [InlineData(32L, "32 GB")]
    // A real 32 GB machine reports less than 32 GiB once firmware takes its share. Reported, not
    // rounded up to the number on the box — the derivation never saw the marketing figure.
    [InlineData(0L, "31.9 GB")]
    public void ItStatesTheRamTheMachineReported(long gib, string expected)
    {
        var total = gib > 0 ? gib * Gib : 34266738688L;

        var resolved = Describe(result: true, total: total);

        Assert.Contains($"Your PC reports {expected} of RAM.", resolved.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The builder's fallback case. <see cref="ISystemMemoryProbe.TryRead"/> returns bool because it
    /// can fail, and when it does the app genuinely does not know this machine's RAM.
    /// <see cref="MemoryDefaults"/> falls back — reserve to its 1024 MB floor, cap to the flat
    /// 4096 MB anomaly line — and the section says so rather than printing a plausible number.
    /// </summary>
    [Fact]
    public void AFailedProbeIsStated_AndTheFallbackFiguresAreTheOnesShown()
    {
        var resolved = Describe(result: false, total: 0);

        Assert.False(resolved.RamKnown);
        Assert.Equal(1024, resolved.ReserveMb);
        Assert.Equal(4096, resolved.CapMb);

        Assert.Contains("RoRoRo can't read how much RAM your PC has.", resolved.Text, StringComparison.Ordinal);
        Assert.Contains("1024 MB kept free", resolved.Text, StringComparison.Ordinal);
        Assert.Contains("passes 4096 MB", resolved.Text, StringComparison.Ordinal);

        // Never claims a figure it does not have.
        Assert.DoesNotContain("Your PC reports", resolved.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("GB of RAM", resolved.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A probe that succeeds but reports nothing is the same state to a reader, and
    /// <see cref="MemoryDefaults"/> already treats it as the same state to the arithmetic — it
    /// tests the total, not the bool. Two rules about one value is how a display starts disagreeing
    /// with the thing it describes.
    /// </summary>
    [Fact]
    public void ASuccessfulReadOfZeroIsAlsoStatedAsUnknown()
    {
        var resolved = Describe(result: true, total: 0);

        Assert.False(resolved.RamKnown);
        Assert.Equal(1024, resolved.ReserveMb);
        Assert.Equal(4096, resolved.CapMb);
        Assert.Contains("RoRoRo can't read how much RAM your PC has.", resolved.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Asking someone to type a number the machine already reported is the same defect wearing a
    /// question mark. The invitation appears only where there is nothing else to go on.
    /// </summary>
    [Fact]
    public void ItAsksForNumbersOnlyWhenTheProbeFailed()
    {
        const string invitation = "Put your own numbers in";

        Assert.DoesNotContain(invitation, Describe(result: true, total: 32 * Gib).Text, StringComparison.Ordinal);
        Assert.Contains(invitation, Describe(result: false, total: 0).Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Pins the one figure with no <see cref="MemoryDefaults"/> derivation behind it.
    /// <c>SettingsBlob.ProjectionWarnMinutes</c> is a plain <c>int</c> declared with its default at
    /// <c>Core/AppSettings.cs:486</c>, on a <c>private sealed record</c> the App cannot name — so
    /// the App holds a copy, and an unpinned copy of a default is the drift this item is about.
    /// Reading it back off a real <c>AppSettings</c> over a path that does not exist is the record's
    /// own answer, not a restatement of the copy.
    /// </summary>
    [Fact]
    public async Task TheProjectionDefaultIsTheOneAppSettingsActuallyShips()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"ROROROblox-automatic-memory-{Guid.NewGuid():N}",
            "settings.json");

        using var settings = new AppSettings(path);

        Assert.Equal(
            AutomaticMemorySummary.ProjectionDefaultMinutes,
            await settings.GetProjectionWarnMinutesAsync());

        Assert.Contains(
            $"starts at {AutomaticMemorySummary.ProjectionDefaultMinutes} minutes.",
            Describe(result: true, total: 32 * Gib).Text,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The markup says the projection default twice in prose that no constant reaches — a HelpText
    /// and a hint, both written as "The default is N." Those are the copies this class's constant
    /// cannot pin by reference, so they are pinned by reading the file. A hint quoting a number the
    /// app no longer uses is the identical defect to a blank box that will not say what it picked.
    /// </summary>
    [Fact]
    public void TheMarkupsStatedDefaultAgreesWithTheRealOne()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.NotNull(root);

        var xaml = Path.Combine(root!, "src", "ROROROblox.App", "Preferences", "PreferencesWindow.xaml");
        Assert.True(File.Exists(xaml), $"{xaml} is missing; this test scanned nothing.");

        var stated = Regex.Matches(File.ReadAllText(xaml), @"The default is (\d+)\.")
            .Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        // Vacuity floor. Two sites today (the projection box's AutomationProperties.HelpText and
        // its hint). A regex that stopped matching would pass an empty list forever, which is the
        // `--filter "Foo*"` lesson in another costume.
        Assert.True(stated.Count >= 2,
            $"Found {stated.Count} 'The default is N.' statements in PreferencesWindow.xaml; there "
            + "are two. The scan is broken, not the markup.");

        Assert.All(stated, n => Assert.Equal(AutomaticMemorySummary.ProjectionDefaultMinutes, n));
    }
}
