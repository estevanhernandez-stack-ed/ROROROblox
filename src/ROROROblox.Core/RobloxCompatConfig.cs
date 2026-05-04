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
/// Outcome of a compat check — feeds the MainViewModel's banner property.
/// </summary>
public sealed record CompatCheckResult(bool HasDrift, string? Banner);
