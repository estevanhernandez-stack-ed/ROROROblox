namespace ROROROblox.Core.Discord;

/// <summary>
/// Persistence seam for <see cref="DiscordConfig"/>. Exists so <see cref="DiscordConfigService"/>
/// can be exercised against a store that fails on demand — the service's contract on a failed
/// persist (publish nothing, raise nothing) is not testable against DPAPI and a real file.
/// </summary>
public interface IDiscordConfigStore
{
    Task<DiscordConfig> LoadAsync();

    Task SaveAsync(DiscordConfig config);
}
