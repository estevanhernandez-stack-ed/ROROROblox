using ROROROblox.App.ViewModels;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// Task 7 — the memory chip's rendering rules extracted as a pure function so they're testable
/// without a running app/dispatcher. Each case names the production change that would make it
/// fail, per the standing bar set on Tasks 3-6's reviews.
/// </summary>
public class MemoryChipFormatterTests
{
    // 2469606195 bytes / 1024^3 == 2.29999995... -> "2.3" under F1. Chosen so a formula bug
    // (wrong divisor, wrong rounding) is visible in the assertion, not hidden by a round number.
    private const long TwoPointThreeGb = 2469606195L;

    private static AccountMemory Reading(bool readOk = true, long privateBytes = TwoPointThreeGb) =>
        new(AccountId: Guid.NewGuid(), PrivateBytes: privateBytes, GrowthBytesPerHour: 0,
            MinutesToCeiling: 0, OverCap: false, IsTarget: false, ReadOk: readOk);

    [Fact]
    public void Format_NotWarned_RendersBytesOnly()
    {
        // Fails if production stops gating the "▲" prefix / countdown on `warned` (e.g. always
        // prepends it, or drops the GB figure entirely).
        var text = MemoryChipFormatter.Format(Reading(), warned: false, hasProjection: true, minutesToCeiling: 45);
        Assert.Equal("2.3 GB", text);
    }

    [Fact]
    public void Format_Warned_WithProjection_AppendsCountdown()
    {
        // Fails if production drops the countdown clause, or formats it without the "▲" marker
        // or the "· ~" separator, when a real projection IS available.
        var text = MemoryChipFormatter.Format(Reading(), warned: true, hasProjection: true, minutesToCeiling: 45);
        Assert.Equal("▲ 2.3 GB · ~45 min", text);
    }

    [Fact]
    public void Format_Warned_WithoutProjection_OmitsCountdown()
    {
        // Fails if production renders a countdown even when HasProjection is false — the exact
        // "arithmetic we could not complete" case rule 5 exists to prevent. A regression here
        // would surface a fabricated "~0 min" (or similar) to the user.
        var text = MemoryChipFormatter.Format(Reading(), warned: true, hasProjection: false, minutesToCeiling: 45);
        Assert.Equal("▲ 2.3 GB", text);
    }

    [Fact]
    public void Format_ReadNotOk_RendersNothing()
    {
        // Fails if production falls through to the bytes format for an unreadable pid (e.g.
        // renders "0.0 GB" or a stale carried-forward figure) instead of returning null so the
        // row's chip collapses entirely.
        var text = MemoryChipFormatter.Format(Reading(readOk: false), warned: true, hasProjection: true, minutesToCeiling: 45);
        Assert.Null(text);
    }
}
