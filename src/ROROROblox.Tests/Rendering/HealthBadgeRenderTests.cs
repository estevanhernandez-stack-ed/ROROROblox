using System.Windows;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Theming;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// v1.36 item 2: the health badge and the Follow-marked badge sit on existing lines of the row and
/// add no height. <see cref="AccountRowCollapsedRenderTests"/> pins the v1.35 header for a clean
/// row; this renders the rows that SHOW a badge and holds them to the same constant.
/// <para>
/// Expired is not here: an expired row has always drawn a 1px border (a v1.35 behaviour), so its
/// height is not the clean-row constant by design.
/// </para>
/// </summary>
public class HealthBadgeRenderTests
{
    private static Window BuildWindow(Action<AccountSummary> shape)
    {
        var built = MainViewModelTests.Build();
        var row = new AccountSummary(new Account(
            Id: Guid.NewGuid(),
            DisplayName: "Account One",
            AvatarUrl: "",
            CreatedAt: DateTimeOffset.UtcNow,
            LastLaunchedAt: null,
            Tags: null));
        shape(row);
        built.Vm.Accounts.Add(row);
        return new ROROROblox.App.MainWindow(built.Vm);
    }

    private static Theme Flatline()
    {
        var scratch = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "rororo-healthbadge-render-" + Guid.NewGuid().ToString("N"));
        var themes = new ThemeStore(scratch).ListAsync().GetAwaiter().GetResult();
        try { if (System.IO.Directory.Exists(scratch)) System.IO.Directory.Delete(scratch, recursive: true); }
        catch (System.IO.IOException) { }
        return themes.Single(t => t.IsBuiltIn && t.Id == "flatline");
    }

    /// <summary>
    /// The header height, after proving the badge named <paramref name="badgeNameStart"/> really
    /// rendered: <c>Find</c> throws when no visible, laid-out button carries that accessible name, so
    /// a collapsed badge can never pass this as "added no height".
    /// </summary>
    private static int HeaderHeight(Action<AccountSummary> shape, string badgeNameStart) =>
        ThemedWindowRender.MeasureRegion(
            Flatline(),
            "MainWindow badge row @96dpi",
            () => BuildWindow(shape),
            content =>
            {
                ThemedWindowRender.Find(
                    content,
                    fe => fe is System.Windows.Controls.Button
                          && fe.Visibility == System.Windows.Visibility.Visible && fe.ActualHeight > 0
                          && System.Windows.Automation.AutomationProperties.GetName(fe).StartsWith(badgeNameStart, StringComparison.Ordinal),
                    "badge " + badgeNameStart);
                return ThemedWindowRender.Find(
                    content, fe => fe.Name == "AccountRowBorder" && fe.ActualHeight > 0, "AccountRowBorder");
            },
            96).Height;

    [WindowRenderFact]
    public void AFollowMarkedRow_AddsNoHeight() =>
        Assert.Equal(AccountRowCollapsedRenderTests.V135RowHeight96, HeaderHeight(r => r.JoinViaFriend = true, "Joins by following"));

    [WindowRenderFact]
    public void ALimitedAndMarkedRow_AddsNoHeight() =>
        Assert.Equal(AccountRowCollapsedRenderTests.V135RowHeight96, HeaderHeight(r =>
        {
            r.JoinViaFriend = true;
            r.SessionLimited = true;
        }, "Limited by Roblox"));

    [WindowRenderFact]
    public void ANeedsVerificationRow_AddsNoHeight() =>
        Assert.Equal(AccountRowCollapsedRenderTests.V135RowHeight96, HeaderHeight(r => r.SessionNeedsVerification = true, "Needs verification"));
}
