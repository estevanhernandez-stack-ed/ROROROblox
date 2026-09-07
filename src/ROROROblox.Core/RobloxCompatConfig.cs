namespace ROROROblox.Core;

/// <summary>
/// Remote config schema for Roblox compatibility. Fetched from GitHub Releases at startup,
/// drives the version-drift banner. Spec §7.1.
/// </summary>
public sealed record RobloxCompatConfig(
    string KnownGoodVersionMin,
    string KnownGoodVersionMax,
    string MutexName,
    DateTimeOffset GeneratedAt);

/// <summary>
/// Outcome of a compat check — feeds the MainViewModel's banner property. Carries DATA, not prose
/// (the Core string boundary, localization Phase D 2026-09-07): when there is drift, the App turns
/// <see cref="Drift"/> into the localized banner via <c>CoreMessageCatalog.For(CompatDrift)</c>.
/// </summary>
public sealed record CompatCheckResult(bool HasDrift, CompatDrift? Drift);

/// <summary>Which way installed Roblox moved out of the tested range.</summary>
public enum CompatDriftDirection
{
    UpdatedTo,
    DowngradedTo,
}

/// <summary>The facts behind a drift banner — the App composes the sentence from these.</summary>
public sealed record CompatDrift(
    CompatDriftDirection Direction,
    string InstalledVersion,
    string TestedMaxVersion);
