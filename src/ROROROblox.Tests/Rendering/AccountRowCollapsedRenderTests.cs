using System.Windows;
using System.Windows.Media;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Theming;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// The v1.36 "nothing moves for the six-alt user" pixel bar (spec §6, §11): one collapsed account
/// row, rendered through the real <c>MainWindow</c> row template, pinned to the height it had in
/// v1.35.
/// <para>
/// THE CONSTANTS BELOW WERE MEASURED ON v1.35 (commit 5a8fe61), BEFORE the first XAML change of the
/// cycle. They are never re-measured to make a failing test pass: a red here means the collapsed
/// row moved, and the row is only allowed to move by being fixed back. Changing a constant is a
/// ruling, not a repair.
/// </para>
/// <para>
/// WHAT IS MEASURED. The <c>AccountRowBorder</c> header of a single <see cref="AccountSummary"/>
/// (no tags, not expired, not running), at the window's declared 900x600. The outer StackPanel's
/// 8px bottom margin and the drop-indicator border (collapsed) are not part of the header.
/// </para>
/// <para>
/// 96 AND 120 DPI, AND WHAT THAT DOES AND DOES NOT MEAN. Layout runs once, in device-independent
/// units (the row is 92 DIPs tall); the header is then rendered to a bitmap at each DPI, so the
/// pinned value is the device-pixel height, <c>round(ActualHeight * dpi / 96)</c>. This is the same
/// shape <c>HistoryRowRenderTests</c> has. It does NOT re-run layout at 125%: an attempt to do that
/// with <c>VisualTreeHelper.SetRootDpi</c> produced 91.2 or 91.5 DIPs depending on which DPI ran
/// first in the shared host thread (the first call's scale leaked into the second), which is
/// order-dependent and so not a baseline. Layout rounding at a real 125% display stays on the
/// eyes-on capture walk.
/// </para>
/// </summary>
public class AccountRowCollapsedRenderTests
{
    /// <summary>v1.35 (5a8fe61) rendered <c>AccountRowBorder</c> height at 96 DPI, in device pixels.</summary>
    internal const int V135RowHeight96 = 92;

    /// <summary>v1.35 (5a8fe61) rendered <c>AccountRowBorder</c> height at 120 DPI, in device pixels.</summary>
    internal const int V135RowHeight120 = 115;

    /// <summary>
    /// Rendered <c>AccountRowBorder</c> height at 96 DPI, in device pixels, with the v1.36 facts line
    /// VISIBLE (Robux, friends online, a note and a fresh age hint). Measured on the item-6 rework
    /// commit. It is the SECOND bar, beside <see cref="V135RowHeight96"/>, which stays the collapsed row.
    /// <para>22 px over the collapsed 92: one line. These constants MOVED from the first cut (174 at
    /// 96, 218 at 120) because the line used to sit inside the name column, which at the declared
    /// 900 px is only about 160 px wide, so four items wrapped onto four lines (+82 px). The line now
    /// lives in grid row 1 and spans the name and actions columns, so the same populated row fits on
    /// one line. (The one-line clipped variant of the first cut measured +21.) Row 0 is a Star row, not
    /// Auto: an Auto row 0 made the collapsed row 8 px taller than v1.35.</para>
    /// </summary>
    internal const int PopulatedRowHeight96 = 114;

    /// <summary>Same row at 120 DPI; see <see cref="PopulatedRowHeight96"/>. 27 px over the collapsed 115.</summary>
    internal const int PopulatedRowHeight120 = 142;

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public AccountRowCollapsedRenderTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    private static Window BuildWindow() => BuildWindow(facts: null, note: null);

    private static Window BuildWindow(AccountFacts? facts, string? note)
    {
        var built = MainViewModelTests.Build();
        built.Vm.Accounts.Add(new AccountSummary(new Account(
            Id: Guid.NewGuid(),
            DisplayName: "Account One",
            AvatarUrl: "",
            CreatedAt: DateTimeOffset.UtcNow,
            LastLaunchedAt: null,
            Tags: null,
            Facts: facts,
            Note: note)));

        return new ROROROblox.App.MainWindow(built.Vm);
    }

    /// <summary>The populated row: three facts, a note and a fresh age hint, so the facts line is visible.</summary>
    private static Window BuildPopulatedWindow() => BuildWindow(
        new AccountFacts(1240, null, 3, DateTimeOffset.UtcNow.AddMinutes(-3), null),
        "Farm alt, keep at 20 FPS");

    /// <summary>
    /// The header, rendered to a bitmap at <paramref name="dpi"/>; the bitmap's height is the
    /// device-pixel height. <c>ThemedWindowRender.Find</c> throws when the row is absent, so a
    /// missing row can never read as a height of 0.
    /// </summary>
    private static RegionSample RenderHeader(double dpi, Func<Window>? build = null) =>
        ThemedWindowRender.MeasureRegion(
            BuiltInFlatline(),
            $"MainWindow collapsed account row @{dpi}dpi",
            build ?? BuildWindow,
            content => ThemedWindowRender.Find(
                content, fe => fe.Name == "AccountRowBorder" && fe.ActualHeight > 0, "AccountRowBorder"),
            dpi);

    private static Theme BuiltInFlatline()
    {
        var scratch = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "rororo-accountrow-render-" + Guid.NewGuid().ToString("N"));
        var themes = new ThemeStore(scratch).ListAsync().GetAwaiter().GetResult();

        try { if (System.IO.Directory.Exists(scratch)) System.IO.Directory.Delete(scratch, recursive: true); }
        catch (System.IO.IOException) { }

        return themes.Single(t => t.IsBuiltIn && t.Id == "flatline");
    }

    private void Pin(double dpi, int pinnedPx)
    {
        var sample = RenderHeader(dpi);
        _output.WriteLine($"@{dpi}dpi  header {sample.Width}x{sample.Height} device px");

        Assert.True(sample.Height == pinnedPx,
            $"@{dpi}dpi: the collapsed account row header renders {sample.Height}px tall; v1.35 "
            + $"(5a8fe61) measured {pinnedPx}px. The collapsed row moved. v1.36 promises it does not.");
    }

    [WindowRenderFact]
    public void TheCollapsedRowMatchesV135At96Dpi() => Pin(96, V135RowHeight96);

    [WindowRenderFact]
    public void TheCollapsedRowMatchesV135At120Dpi() => Pin(120, V135RowHeight120);

    private void PinPopulated(double dpi, int pinnedPx)
    {
        var collapsed = RenderHeader(dpi).Height;
        var populated = RenderHeader(dpi, BuildPopulatedWindow);
        _output.WriteLine($"@{dpi}dpi  populated {populated.Width}x{populated.Height}, collapsed {collapsed}, delta {populated.Height - collapsed}");

        Assert.True(populated.Height == pinnedPx,
            $"@{dpi}dpi: the populated account row header renders {populated.Height}px tall; the item-6 "
            + $"commit measured {pinnedPx}px. The facts line changed height.");
        Assert.True(populated.Height > collapsed,
            $"@{dpi}dpi: a row with facts must be taller than the collapsed row ({collapsed}px); the facts line did not show.");
    }

    [WindowRenderFact]
    public void ARowWithFacts_IsTheMeasuredDeltaTallerAt96Dpi() => PinPopulated(96, PopulatedRowHeight96);

    [WindowRenderFact]
    public void ARowWithFacts_IsTheMeasuredDeltaTallerAt120Dpi() => PinPopulated(120, PopulatedRowHeight120);

    /// <summary>
    /// "Collapsed when it has nothing to say" at the pixel level, stated against the facts line
    /// specifically: an upgraded roster (no facts, no history, no note) and a row whose facts are
    /// all null, with only an age hint to its name, both render at exactly the v1.35 height.
    /// </summary>
    [WindowRenderFact]
    public void AFactsRecordWithNothingInIt_StaysAtTheV135HeightAt96Dpi()
    {
        var sample = RenderHeader(96, () => BuildWindow(new AccountFacts(null, null, null, null, "403"), null));
        Assert.Equal(V135RowHeight96, sample.Height);
    }
}
