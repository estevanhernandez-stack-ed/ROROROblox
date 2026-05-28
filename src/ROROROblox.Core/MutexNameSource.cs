namespace ROROROblox.Core;

/// <summary>
/// Which tier of the config-driven mutex-name resolver produced the name in use. Logged at startup
/// so a Roblox-compat event traces to whether live config, a stale cache, or the hardcoded default
/// served the name. Spec item #1.
/// </summary>
public enum MutexNameSource
{
    /// <summary>A valid name came straight from the freshly-fetched roblox-compat.json.</summary>
    RemoteConfig,

    /// <summary>Fetch failed or returned an invalid name; a previously-cached valid name was used.</summary>
    LastKnownGood,

    /// <summary>No valid remote or cached name; fell back to <see cref="MutexHolder.DefaultMutexName"/>.</summary>
    Default,
}
