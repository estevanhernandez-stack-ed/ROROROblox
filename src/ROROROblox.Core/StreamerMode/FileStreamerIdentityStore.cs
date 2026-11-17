using System.Text.Json;
using Microsoft.Extensions.Logging;

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

    private readonly WrongShapeWarning _shape;

    public FileStreamerIdentityStore(string path, ILogger<FileStreamerIdentityStore>? log = null)
    {
        _path = path;
        _shape = new WrongShapeWarning(log);
    }

    public static string DefaultPath() => AppDataRoot.Combine("streamer-identities.dat");

    public async Task<IReadOnlyDictionary<string, StreamerIdentity>> LoadAllAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path)) return new Dictionary<string, StreamerIdentity>();
            var json = await File.ReadAllTextAsync(_path).ConfigureAwait(false);
            return ParseOrEmpty(json);
        }
        catch
        {
            return new Dictionary<string, StreamerIdentity>(); // corrupt file must not brick startup
        }
        finally { _gate.Release(); }
    }

    // A struct row deserializes from {} with null names, which a reader then dereferences, and a null entry
    // would stop the parse; both are dropped, and the good entries kept. A null map is a wrong shape and
    // reads as empty, the same as an unparseable file.
    private Dictionary<string, StreamerIdentity> ParseOrEmpty(string json)
    {
        var map = JsonSerializer.Deserialize<Dictionary<string, StreamerIdentity?>>(json);
        if (map is null)
        {
            _shape.Report(_path, "empty");
            return new Dictionary<string, StreamerIdentity>();
        }
        var kept = new Dictionary<string, StreamerIdentity>(map.Count);
        var dropped = 0;
        foreach (var (key, value) in map)
        {
            if (value is { FakeName: not null, FakeAvatarId: not null } identity) { kept[key] = identity; }
            else { dropped++; }
        }
        _shape.ReportDropped(_path, dropped);
        return kept;
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
            return ParseOrEmpty(json);
        }
        catch
        {
            return new Dictionary<string, StreamerIdentity>(); // corrupt/partial file → start fresh
        }
    }
}
