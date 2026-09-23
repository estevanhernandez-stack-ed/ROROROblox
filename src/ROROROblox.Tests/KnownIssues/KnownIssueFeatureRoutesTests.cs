using ROROROblox.App.KnownIssues;
using ROROROblox.Core.KnownIssues;

namespace ROROROblox.Tests.KnownIssues;

public class KnownIssueFeatureRoutesTests
{
    [Theory]
    [InlineData(KnownIssueFeatures.MemoryWatchdog, KnownIssueFeatureRoute.MemorySettings)]
    [InlineData(KnownIssueFeatures.FpsCaps, KnownIssueFeatureRoute.MainWindow)]
    [InlineData(KnownIssueFeatures.Recycle, KnownIssueFeatureRoute.MainWindow)]
    [InlineData("teleport-helper", KnownIssueFeatureRoute.None)]
    [InlineData("MEMORY-WATCHDOG", KnownIssueFeatureRoute.None)]
    [InlineData(null, KnownIssueFeatureRoute.None)]
    public void EachKeyGoesWhereTheSpecSays(string? key, KnownIssueFeatureRoute expected)
    {
        Assert.Equal(expected, KnownIssueFeatureRoutes.For(key));
    }

    [Fact]
    public void OnlyARealRouteHasAButtonLabel()
    {
        Assert.Equal("KnownIssuesPage_OpenMemorySettings", KnownIssueFeatureRoutes.ButtonLabelKey(KnownIssueFeatureRoute.MemorySettings));
        Assert.Equal("KnownIssuesPage_ShowAccounts", KnownIssueFeatureRoutes.ButtonLabelKey(KnownIssueFeatureRoute.MainWindow));
        Assert.Null(KnownIssueFeatureRoutes.ButtonLabelKey(KnownIssueFeatureRoute.None));
    }
}
