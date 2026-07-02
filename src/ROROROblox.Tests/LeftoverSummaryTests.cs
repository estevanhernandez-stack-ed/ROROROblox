using ROROROblox.App.ViewModels;
using Xunit;

namespace ROROROblox.Tests;

public class LeftoverSummaryTests
{
    [Fact]
    public void Format_BothKinds_ReadsSplitThenReassurance()
    {
        Assert.Equal(
            "Found 3 leftover Roblox processes with no window, and 2 open Roblox windows from before. Multi-instance is fine — RoRoRo has the lock.",
            LeftoverSummary.Format(windowless: 3, windowed: 2));
    }

    [Fact]
    public void Format_WindowlessOnly_OmitsWindowedClause()
    {
        Assert.Equal(
            "Found 3 leftover Roblox processes with no window. Multi-instance is fine — RoRoRo has the lock.",
            LeftoverSummary.Format(windowless: 3, windowed: 0));
    }

    [Fact]
    public void Format_WindowedOnly_OmitsWindowlessClause()
    {
        Assert.Equal(
            "Found 2 open Roblox windows from before. Multi-instance is fine — RoRoRo has the lock.",
            LeftoverSummary.Format(windowless: 0, windowed: 2));
    }

    [Fact]
    public void Format_Singulars()
    {
        Assert.Equal(
            "Found 1 leftover Roblox process with no window, and 1 open Roblox window from before. Multi-instance is fine — RoRoRo has the lock.",
            LeftoverSummary.Format(windowless: 1, windowed: 1));
    }
}
