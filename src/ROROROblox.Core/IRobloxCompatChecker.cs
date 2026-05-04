namespace ROROROblox.Core;

/// <summary>
/// Reads the locally-installed Roblox version + fetches the remote known-good config and
/// returns a drift indicator + banner copy for the MainViewModel. Spec §7.1.
/// </summary>
public interface IRobloxCompatChecker
{
    Task<CompatCheckResult> CheckAsync();
}
