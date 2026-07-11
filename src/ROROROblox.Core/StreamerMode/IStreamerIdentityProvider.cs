namespace ROROROblox.Core.StreamerMode;

public readonly record struct DisplayIdentity(string Name, string AvatarSource);

public interface IStreamerIdentityProvider
{
    bool IsActive { get; }
    Task InitializeAsync(IReadOnlyCollection<(Guid accountId, StreamerIdentity identity)> accountIdentities);
    Task SetActiveAsync(bool active);
    DisplayIdentity ForAccount(Guid accountId, string realName, string realAvatarUrl);
    DisplayIdentity ForFriend(long robloxUserId, string realName, string realAvatarUrl);
    Task RerollAsync(string identityKey);
    Task RerollAllAsync();
    event EventHandler? Changed;
}
