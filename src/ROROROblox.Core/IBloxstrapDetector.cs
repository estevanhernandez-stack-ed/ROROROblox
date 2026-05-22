namespace ROROROblox.Core;

/// <summary>
/// Detects whether a launch-handling "strap" owns the <c>roblox-player</c> protocol handler.
/// </summary>
public interface IBloxstrapDetector
{
    /// <summary>
    /// True when Bloxstrap specifically is the registered <c>roblox-player</c> handler.
    /// When true, our FFlag write is overridden by Bloxstrap's launch-time rewrite — the user
    /// sees a one-time dismissible banner. Spec §5.2.
    /// </summary>
    bool IsBloxstrapHandler();

    /// <summary>
    /// True when EITHER Bloxstrap or Fishstrap is the registered <c>roblox-player</c> handler.
    /// A strap intercepts launches and updates Roblox proactively itself, so RoRoRo's pre-warm /
    /// version pre-check should be skipped to avoid a double-update (install-deferral spec,
    /// Riders §7). Detect only — RoRoRo never co-drives the strap's update mutex.
    /// </summary>
    bool IsStrapHandlingLaunches();
}
