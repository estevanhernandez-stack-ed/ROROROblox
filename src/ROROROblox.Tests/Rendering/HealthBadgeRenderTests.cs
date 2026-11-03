using System.Windows;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Theming;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// v1.36 item 2, re-ruled at item 13 (Este, 2026-11-03): the health badge and the Follow-marked badge LEAD the facts
/// line. On the name line, where item 2 put them, they pushed the account name to zero width at 900 px. A badge row
/// therefore shows the facts line and is exactly the populated height
/// (<see cref="AccountRowCollapsedRenderTests.PopulatedRowHeight96"/>): one facts line more than a clean v1.35 row, and
/// no more, whether or not the account has facts. Each test also proves the badge sits in the facts line's WrapPanel,
/// not on the name line.
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
                var badge = ThemedWindowRender.Find(
                    content,
                    fe => fe is System.Windows.Controls.Button
                          && fe.Visibility == System.Windows.Visibility.Visible && fe.ActualHeight > 0
                          && System.Windows.Automation.AutomationProperties.GetName(fe).StartsWith(badgeNameStart, StringComparison.Ordinal),
                    "badge " + badgeNameStart);
                Assert.True(System.Windows.Media.VisualTreeHelper.GetParent(badge) is System.Windows.Controls.WrapPanel { } line
                            && System.Windows.Controls.Grid.GetRow(line) == 1,
                    $"the {badgeNameStart} badge is not in the facts line (the row grid's row 1 WrapPanel)");
                return ThemedWindowRender.Find(
                    content, fe => fe.Name == "AccountRowBorder" && fe.ActualHeight > 0, "AccountRowBorder");
            },
            96).Height;

    [WindowRenderFact]
    public void AFollowMarkedRow_OpensTheFactsLine_AtThePopulatedHeight() =>
        Assert.Equal(AccountRowCollapsedRenderTests.PopulatedRowHeight96, HeaderHeight(r => r.JoinViaFriend = true, "Joins by following"));

    [WindowRenderFact]
    public void ALimitedAndMarkedRow_OpensTheFactsLine_AtThePopulatedHeight() =>
        Assert.Equal(AccountRowCollapsedRenderTests.PopulatedRowHeight96, HeaderHeight(r =>
        {
            r.JoinViaFriend = true;
            r.SessionLimited = true;
        }, "Limited by Roblox"));

    [WindowRenderFact]
    public void ANeedsVerificationRow_OpensTheFactsLine_AtThePopulatedHeight() =>
        Assert.Equal(AccountRowCollapsedRenderTests.PopulatedRowHeight96, HeaderHeight(r => r.SessionNeedsVerification = true, "Needs verification"));
}
