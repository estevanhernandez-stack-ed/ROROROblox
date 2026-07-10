using System.Text.Json;

namespace ROROROblox.Core.StreamerMode;

/// <summary>
/// Persists friend fake-identities to a JSON file. NOT a secret store — never put a cookie here.
/// Account identities live on the DPAPI-backed Account record instead (see AccountStore).
/// </summary>
public sealed class FileStreamerIdentityStore : IStreamerIdentityStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileStreamerIdentityStore() : this(DefaultPath()) { }

    public FileStreamerIdentityStore(string path) => _path = path;

    private static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ROROROblox", "streamer-identities.dat");

    public async Task<IReadOnlyDictionary<string, StreamerIdentity>> LoadAllAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path)) return new Dictionary<string, StreamerIdentity>();
            var json = await File.ReadAllTextAsync(_path).ConfigureAwait(false);
            return JsonSerializer.Deserialize<Dictionary<string, StreamerIdentity>>(json)
                   ?? new Dictionary<string, StreamerIdentity>();
        }
        catch
        {
            return new Dictionary<string, StreamerIdentity>(); // corrupt file must not brick startup
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(string key, StreamerIdentity identity)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var map = File.Exists(_path)
                ? (JsonSerializer.Deserialize<Dictionary<string, StreamerIdentity>>(
                       await File.ReadAllTextAsync(_path).ConfigureAwait(false))
                   ?? new Dictionary<string, StreamerIdentity>())
                : new Dictionary<string, StreamerIdentity>();
            map[key] = identity;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(map)).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
}
