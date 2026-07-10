using ROROROblox.Core;

namespace ROROROblox.Core.StreamerMode;

public sealed class StreamerIdentityProvider : IStreamerIdentityProvider
{
    private readonly IStreamerNamePool _names;
    private readonly IStreamerAvatarPool _avatars;
    private readonly IStreamerIdentityStore _friendStore;
    private readonly IAppSettings _settings;
    private readonly Func<Guid, StreamerIdentity, Task> _persistAccount;
    private readonly Dictionary<string, StreamerIdentity> _map = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public StreamerIdentityProvider(
        IStreamerNamePool names, IStreamerAvatarPool avatars,
        IStreamerIdentityStore friendStore, IAppSettings settings,
        Func<Guid, StreamerIdentity, Task> persistAccount)
    {
        _names = names; _avatars = avatars; _friendStore = friendStore;
        _settings = settings; _persistAccount = persistAccount;
    }

    public bool IsActive { get; private set; }
    public event EventHandler? Changed;

    public static string AccountKey(Guid id) => $"account:{id}";
    public static string FriendKey(long userId) => $"friend:{userId}";

    public async Task InitializeAsync(IReadOnlyCollection<(Guid accountId, StreamerIdentity identity)> accountIdentities)
    {
        IsActive = await _settings.GetStreamerModeAsync().ConfigureAwait(false);
        var friends = await _friendStore.LoadAllAsync().ConfigureAwait(false);
        lock (_lock)
        {
            foreach (var (id, identity) in accountIdentities)
                if (!string.IsNullOrEmpty(identity.FakeName)) _map[AccountKey(id)] = identity;
            foreach (var kv in friends) _map[kv.Key] = kv.Value;
        }
    }

    public DisplayIdentity ForAccount(Guid accountId, string realName, string realAvatarUrl)
        => Resolve(AccountKey(accountId), realName, realAvatarUrl, accountId);

    public DisplayIdentity ForFriend(long robloxUserId, string realName, string realAvatarUrl)
        => Resolve(FriendKey(robloxUserId), realName, realAvatarUrl, accountId: null);

    private DisplayIdentity Resolve(string key, string realName, string realAvatarUrl, Guid? accountId)
    {
        if (!IsActive) return new DisplayIdentity(realName, realAvatarUrl);

        StreamerIdentity id;
        bool assigned = false;
        lock (_lock)
        {
            if (!_map.TryGetValue(key, out id))
            {
                id = MakeFresh();
                _map[key] = id;
                assigned = true;
            }
        }
        if (assigned) _ = Persist(key, id, accountId); // fire-and-forget persist of the lazy assignment
        return new DisplayIdentity(id.FakeName, _avatars.ResourceUri(id.FakeAvatarId));
    }

    private StreamerIdentity MakeFresh()
    {
        var usedNames = _map.Values.Select(v => v.FakeName).ToHashSet(StringComparer.Ordinal);
        var usedAvatars = _map.Values.Select(v => v.FakeAvatarId).ToHashSet(StringComparer.Ordinal);
        return new StreamerIdentity(_names.Next(usedNames), _avatars.Next(usedAvatars));
    }

    public async Task RerollAsync(string identityKey)
    {
        StreamerIdentity id;
        Guid? accountId = TryAccountId(identityKey);
        lock (_lock) { id = MakeFresh(); _map[identityKey] = id; }
        await Persist(identityKey, id, accountId).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task RerollAllAsync()
    {
        List<string> keys;
        lock (_lock) { keys = _map.Keys.ToList(); _map.Clear(); }
        foreach (var key in keys)
        {
            StreamerIdentity id; lock (_lock) { id = MakeFresh(); _map[key] = id; }
            await Persist(key, id, TryAccountId(key)).ConfigureAwait(false);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetActiveAsync(bool active)
    {
        IsActive = active;
        await _settings.SetStreamerModeAsync(active).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private Task Persist(string key, StreamerIdentity id, Guid? accountId)
        => accountId is { } gid ? _persistAccount(gid, id) : _friendStore.SaveAsync(key, id);

    private static Guid? TryAccountId(string key)
        => key.StartsWith("account:", StringComparison.Ordinal) && Guid.TryParse(key.AsSpan(8), out var g) ? g : null;
}
