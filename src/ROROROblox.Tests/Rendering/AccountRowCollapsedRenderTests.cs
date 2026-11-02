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

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public AccountRowCollapsedRenderTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    private static Window BuildWindow()
    {
        var built = MainViewModelTests.Build();
        built.Vm.Accounts.Add(new AccountSummary(new Account(
            Id: Guid.NewGuid(),
            DisplayName: "Account One",
            AvatarUrl: "",
            CreatedAt: DateTimeOffset.UtcNow,
            LastLaunchedAt: null,
            Tags: null)));

        return new ROROROblox.App.MainWindow(built.Vm);
    }

    /// <summary>
    /// The header, rendered to a bitmap at <paramref name="dpi"/>; the bitmap's height is the
    /// device-pixel height. <c>ThemedWindowRender.Find</c> throws when the row is absent, so a
    /// missing row can never read as a height of 0.
    /// </summary>
    private static RegionSample RenderHeader(double dpi) =>
        ThemedWindowRender.MeasureRegion(
            BuiltInFlatline(),
            $"MainWindow collapsed account row @{dpi}dpi",
            BuildWindow,
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
}
