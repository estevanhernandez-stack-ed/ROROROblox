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
            var map = await ReadExistingOrEmptyAsync().ConfigureAwait(false);
            map[key] = identity;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(map)).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Read-merge base for <see cref="SaveAsync"/>. Symmetric with <see cref="LoadAllAsync"/>:
    /// a missing, corrupt, or partial file degrades to a fresh empty map so a garbage
    /// streamer-identities.dat gets overwritten cleanly instead of throwing forever.
    /// Caller holds the gate.
    /// </summary>
    private async Task<Dictionary<string, StreamerIdentity>> ReadExistingOrEmptyAsync()
    {
        if (!File.Exists(_path)) return new Dictionary<string, StreamerIdentity>();
        try
        {
            var json = await File.ReadAllTextAsync(_path).ConfigureAwait(false);
            return JsonSerializer.Deserialize<Dictionary<string, StreamerIdentity>>(json)
                   ?? new Dictionary<string, StreamerIdentity>();
        }
        catch
        {
            return new Dictionary<string, StreamerIdentity>(); // corrupt/partial file → start fresh
        }
    }
}
