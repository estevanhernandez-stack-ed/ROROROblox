using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace ROROROblox.App.Plugins;

/// <summary>
/// DPAPI-encrypted (per-user, per-machine) store of plugin consent records.
/// Mirrors the AccountStore pattern: a JSON list, encrypted with
/// <c>ProtectedData.Protect(..., DataProtectionScope.CurrentUser)</c>.
/// On tamper / decryption failure, returns an empty list (so a stray file
/// can't crash plugin discovery).
/// </summary>
public sealed class ConsentStore
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly ILogger<ConsentStore>? _log;
    private int _shapeReported;

    public ConsentStore(string filePath, ILogger<ConsentStore>? log = null)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _log = log;
    }

    public async Task<IReadOnlyList<ConsentRecord>> ListAsync()
    {
        var records = await LoadAsync().ConfigureAwait(false);
        return records.Values.ToList();
    }

    public async Task GrantAsync(string pluginId, IEnumerable<string> capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        var records = await LoadAsync().ConfigureAwait(false);
        records[pluginId] = new ConsentRecord
        {
            PluginId = pluginId,
            GrantedCapabilities = capabilities.Distinct().ToList(),
            AutostartEnabled = records.TryGetValue(pluginId, out var existing) && existing.AutostartEnabled,
        };
        await SaveAsync(records).ConfigureAwait(false);
    }

    public async Task RevokeAsync(string pluginId)
    {
        var records = await LoadAsync().ConfigureAwait(false);
        records.Remove(pluginId);
        await SaveAsync(records).ConfigureAwait(false);
    }

    public async Task SetAutostartAsync(string pluginId, bool enabled)
    {
        var records = await LoadAsync().ConfigureAwait(false);
        if (!records.TryGetValue(pluginId, out var existing))
        {
            throw new InvalidOperationException($"No consent record for plugin {pluginId}.");
        }
        records[pluginId] = existing with { AutostartEnabled = enabled };
        await SaveAsync(records).ConfigureAwait(false);
    }

    private async Task<Dictionary<string, ConsentRecord>> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, ConsentRecord>();
        }

        try
        {
            var encrypted = await File.ReadAllBytesAsync(_filePath).ConfigureAwait(false);
            var decrypted = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
            var list = JsonSerializer.Deserialize<List<ConsentRecord>>(decrypted, JsonOptions);
            if (list is null)
            {
                return new Dictionary<string, ConsentRecord>();
            }
            // One bad row (null, no plugin id, no capability list, or an id that appears twice) costs
            // that plugin's record and nothing else. A dropped record means that plugin asks for consent
            // again: the safe direction, since consent is never granted by a row going missing.
            var kept = new Dictionary<string, ConsentRecord>(list.Count, StringComparer.Ordinal);
            var dropped = 0;
            var repeated = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in list)
            {
                if (record is null || record.PluginId is null || record.GrantedCapabilities is null)
                {
                    dropped++;
                }
                else if (!kept.TryAdd(record.PluginId, record))
                {
                    repeated.Add(record.PluginId);
                    dropped++;
                }
            }
            // Two rows for one plugin cannot both be believed, and neither is a grant the user can be
            // shown to have made, so that plugin asks again.
            foreach (var id in repeated)
            {
                kept.Remove(id);
                dropped++;
            }
            if (dropped > 0 && Interlocked.Exchange(ref _shapeReported, 1) == 0)
            {
                _log?.LogWarning("{File} had {Dropped} unusable row(s); they were dropped and the rest kept.", Path.GetFileName(_filePath), dropped);
            }
            return kept;
        }
        catch (CryptographicException)
        {
            // Tampered or wrong-user envelope. Treat as empty.
            return new Dictionary<string, ConsentRecord>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, ConsentRecord>();
        }
    }

    private async Task SaveAsync(Dictionary<string, ConsentRecord> records)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(records.Values.ToList(), JsonOptions);
        var encrypted = ProtectedData.Protect(json, optionalEntropy: null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        await File.WriteAllBytesAsync(_filePath, encrypted).ConfigureAwait(false);
    }
}

public sealed record ConsentRecord
{
    public required string PluginId { get; init; }
    public required IReadOnlyList<string> GrantedCapabilities { get; init; }
    public bool AutostartEnabled { get; init; }
}
