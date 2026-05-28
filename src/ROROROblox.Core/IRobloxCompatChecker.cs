namespace ROROROblox.Core;

/// <summary>
/// Reads the locally-installed Roblox version + fetches the remote known-good config and
/// returns a drift indicator + banner copy for the MainViewModel. Spec §7.1.
/// </summary>
public interface IRobloxCompatChecker
{
    Task<CompatCheckResult> CheckAsync();

    /// <summary>
    /// Resolves the singleton mutex name to bind at startup: valid remote <c>roblox-compat.json</c>
    /// -> last-known-good cache -> hardcoded <see cref="MutexHolder.DefaultMutexName"/>. 2s-bounded,
    /// degrade-safe, NEVER throws. The returned <see cref="MutexNameSource"/> tells the caller which
    /// tier won, for honest startup logging. Spec item #1.
    /// </summary>
    Task<(string Name, MutexNameSource Source)> ResolveMutexNameAsync();
}
