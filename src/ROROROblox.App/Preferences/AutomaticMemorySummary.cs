using System.Globalization;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.App.Preferences;

/// <summary>
/// Says what "leave the box empty and RoRoRo will pick one" actually picks, on this machine.
/// <para>
/// WHY THIS EXISTS. Item 3 made the three numeric memory fields accept blank, and blank means the
/// composition root derives a value from installed RAM. The section then never said what it
/// derived, so a blank box was a promise with no visible outcome — the app knowing something and
/// not saying it, which is the defect the whole v1.18 cycle is about, one more time.
/// </para>
/// <para>
/// <b>NO SECOND DERIVATION, and that is the constraint that matters most here.</b> Nothing in this
/// file knows about 8%, 35%, a floor or a ceiling. It calls
/// <see cref="MemoryDefaults.ReserveMb"/> and <see cref="MemoryDefaults.CapMb"/> — the same two
/// calls <c>App.WireMemoryWatchdogAsync</c> makes at <c>App.xaml.cs:1173-1174</c> — with the same
/// input, so the figure on screen and the figure the watchdog runs with cannot drift. A displayed
/// number that can disagree with the running one would be worse than saying nothing.
/// </para>
/// <para>
/// THE PROBE'S <c>out</c> VALUE IS PASSED THROUGH UNJUDGED, on purpose. <c>App.xaml.cs:1169</c>
/// discards <see cref="ISystemMemoryProbe.TryRead"/>'s bool and hands whatever landed in the
/// <c>out</c> straight to <see cref="MemoryDefaults"/>, which treats a non-positive total as
/// "probe failed" and falls back on its own. Re-deciding that here would be a second rule about
/// the same value. The bool is used for one thing only: what the sentence SAYS. When the read
/// fails the app genuinely does not know the machine's RAM, and a plausible-looking number in that
/// state is the same defect wearing a friendlier face.
/// </para>
/// <para>
/// It does not ask the user to type their RAM size. The machine already reported it whenever it
/// can; asking for what is already known is the defect wearing a question mark. The invitation to
/// enter explicit numbers appears only on the fallback path, where there is nothing else to go on.
/// </para>
/// </summary>
internal sealed class AutomaticMemorySummary
{
    /// <summary>
    /// What the projection setting is when nobody has changed it.
    /// <para>
    /// UNLIKE THE OTHER TWO, this one has no <see cref="MemoryDefaults"/> derivation to call:
    /// <c>SettingsBlob.ProjectionWarnMinutes</c> is a plain <c>int</c> with a real default, declared
    /// at <c>Core/AppSettings.cs:486</c>, and that record is <c>private sealed</c> — no App code can
    /// name it. So this constant is a copy, and a copy of a default is exactly the drift this class
    /// exists to prevent. It is pinned instead:
    /// <c>AutomaticMemorySummaryTests.TheProjectionDefaultIsTheOneAppSettingsActuallyShips</c>
    /// reads it back off a real <c>AppSettings</c> over a file that does not exist, so changing the
    /// record's default without changing this reddens the build.
    /// </para>
    /// </summary>
    internal const int ProjectionDefaultMinutes = 120;

    private const double BytesPerGb = 1024d * 1024d * 1024d;

    private readonly ISystemMemoryProbe _probe;

    internal AutomaticMemorySummary(ISystemMemoryProbe probe) => _probe = probe;

    /// <summary>
    /// What the three automatic values resolve to right now, and the sentence that says it.
    /// <paramref name="RamKnown"/> false means the probe could not read the machine's memory and
    /// the two figures are <see cref="MemoryDefaults"/>' own fallbacks.
    /// </summary>
    internal readonly record struct Resolved(
        bool RamKnown,
        long TotalPhysicalBytes,
        int ReserveMb,
        int CapMb,
        int ProjectionMinutes,
        string Text);

    internal Resolved Describe()
    {
        var read = _probe.TryRead(out var totalPhysicalBytes, out _);

        // The same input the startup path derives from, unaltered — see the class note.
        var reserveMb = MemoryDefaults.ReserveMb(totalPhysicalBytes);
        var capMb = MemoryDefaults.CapMb(totalPhysicalBytes);

        // A read that failed and a read that returned nothing are the same state to a reader, and
        // MemoryDefaults already treats them as the same state to the arithmetic.
        var ramKnown = read && totalPhysicalBytes > 0;

        return new Resolved(
            ramKnown,
            totalPhysicalBytes,
            reserveMb,
            capMb,
            ProjectionDefaultMinutes,
            Sentence(ramKnown, totalPhysicalBytes, reserveMb, capMb));
    }

    /// <summary>
    /// Clan-facing register per <c>CLAUDE.md</c>: second person, terminal periods, no jargon. The
    /// mechanism is deliberately absent — "8% of installed RAM clamped to 4 GB" is true and tells a
    /// Pet Sim 99 player nothing. What it MEANS on their PC is the number.
    /// </summary>
    private static string Sentence(bool ramKnown, long totalPhysicalBytes, int reserveMb, int capMb) =>
        ramKnown
            ? $"Your PC reports {FormatGb(totalPhysicalBytes)} of RAM. Leave a box below blank and "
              + $"RoRoRo picks for you: {Number(reserveMb)} MB kept free, and a warning when one "
              + $"account passes {Number(capMb)} MB. Warn this far ahead starts at "
              + $"{Number(ProjectionDefaultMinutes)} minutes."
            : "RoRoRo can't read how much RAM your PC has. Leave a box below blank and it falls "
              + $"back to {Number(reserveMb)} MB kept free, and a warning when one account passes "
              + $"{Number(capMb)} MB. Warn this far ahead starts at "
              + $"{Number(ProjectionDefaultMinutes)} minutes. Put your own numbers in if those "
              + "don't suit your PC.";

    /// <summary>
    /// One decimal, and the trailing <c>.0</c> dropped, so 32 GiB reads "32 GB" while the 31.9 a
    /// real 32 GB machine reports after hardware-reserved memory reads "31.9 GB". Rounding that up
    /// to a marketing figure would be inventing a number the derivation never saw.
    /// </summary>
    private static string FormatGb(long totalPhysicalBytes) =>
        (totalPhysicalBytes / BytesPerGb).ToString("0.#", CultureInfo.InvariantCulture) + " GB";

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
