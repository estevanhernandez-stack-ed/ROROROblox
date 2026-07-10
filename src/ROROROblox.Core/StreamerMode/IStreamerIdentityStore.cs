namespace ROROROblox.Core.StreamerMode;

public interface IStreamerIdentityStore
{
    Task<IReadOnlyDictionary<string, StreamerIdentity>> LoadAllAsync();
    Task SaveAsync(string key, StreamerIdentity identity);
}
