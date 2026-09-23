using ROROROblox.Core.KnownIssues;

namespace ROROROblox.App.KnownIssues;

public enum KnownIssueFeatureRoute
{
    /// <summary>An unknown key: the entry's sentence shows, with no button.</summary>
    None,

    /// <summary>Tools › Settings › Alerts &amp; memory, scrolled to "Watch memory while accounts are running".</summary>
    MemorySettings,

    /// <summary>The main window, brought forward — frame-rate caps and Recycle are per account row.</summary>
    MainWindow,
}

/// <summary>Where a <c>rororoHelps.feature</c> key leads. Exact, ordinal match: keys are data, not prose.</summary>
internal static class KnownIssueFeatureRoutes
{
    public static KnownIssueFeatureRoute For(string? featureKey) => featureKey switch
    {
        KnownIssueFeatures.MemoryWatchdog => KnownIssueFeatureRoute.MemorySettings,
        KnownIssueFeatures.FpsCaps or KnownIssueFeatures.Recycle => KnownIssueFeatureRoute.MainWindow,
        _ => KnownIssueFeatureRoute.None,
    };

    public static string? ButtonLabelKey(KnownIssueFeatureRoute route) => route switch
    {
        KnownIssueFeatureRoute.MemorySettings => "KnownIssuesPage_OpenMemorySettings",
        KnownIssueFeatureRoute.MainWindow => "KnownIssuesPage_ShowAccounts",
        _ => null,
    };
}
