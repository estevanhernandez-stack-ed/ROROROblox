namespace ROROROblox.Core.KnownIssues;

/// <summary>
/// The feature keys an entry's <c>rororoHelps.feature</c> may name, version 1. Data, not routing:
/// where each one leads is the App's business (<c>KnownIssueFeatureRoutes</c>).
/// </summary>
public static class KnownIssueFeatures
{
    /// <summary>The Memory section of Settings — "Watch memory while accounts are running".</summary>
    public const string MemoryWatchdog = "memory-watchdog";

    /// <summary>The per-account frame-rate cap on each main-window account row.</summary>
    public const string FpsCaps = "fps-caps";

    /// <summary>The per-account Recycle button on each main-window account row.</summary>
    public const string Recycle = "recycle";
}
