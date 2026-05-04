namespace ROROROblox.Core;

/// <summary>
/// Persistent, DPAPI-encrypted store of saved Roblox accounts. Spec §5.4 + §6.1.
/// Cookies live only inside the encrypted blob; the public <see cref="Account"/> record never
/// carries them. <see cref="RetrieveCookieAsync"/> is the only path back to plaintext.
/// </summary>
public interface IAccountStore
{
    Task<IReadOnlyList<Account>> ListAsync();
    Task<Account> AddAsync(string displayName, string avatarUrl, string cookie);
    Task RemoveAsync(Guid id);
    Task<string> RetrieveCookieAsync(Guid id);
    Task UpdateCookieAsync(Guid id, string newCookie);
    Task TouchLastLaunchedAsync(Guid id);
}
