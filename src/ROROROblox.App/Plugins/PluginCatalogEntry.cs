using System.Text.Json;
using System.Text.Json.Serialization;

namespace ROROROblox.App.Plugins;

/// <summary>
/// One plugin as listed in the remote marketplace catalog. Metadata + an install URL only — the
/// catalog never carries plugin code or hashes; install stays SHA-verified through
/// <see cref="PluginInstaller"/> against the release's own manifest.sha256.
/// </summary>
internal sealed record PluginCatalogEntry(
    string Id,
    string Name,
    string Description,
    string Publisher,
    string? IconUrl,
    string LatestVersion,
    string InstallUrl,
    string? MinHostVersion);

/// <summary>
/// Parses catalog.json (an array of entries) into <see cref="PluginCatalogEntry"/>. Total: malformed
/// JSON, a non-array root, or an entry missing a required field never throws — the bad input (or bad
/// entry) is dropped and the rest returned. The marketplace degrades to "no catalog" on any failure.
/// </summary>
internal static class PluginCatalogParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<PluginCatalogEntry> Parse(string json)
    {
        List<Dto>? dtos;
        try
        {
            dtos = JsonSerializer.Deserialize<List<Dto>>(json, Options);
        }
        catch (JsonException)
        {
            return [];
        }

        if (dtos is null)
        {
            return [];
        }

        var entries = new List<PluginCatalogEntry>(dtos.Count);
        foreach (var d in dtos)
        {
            // Required fields — an entry missing any of these can't be shown or installed, so drop it
            // rather than surface a half-broken row.
            if (string.IsNullOrWhiteSpace(d.Id) || string.IsNullOrWhiteSpace(d.Name)
                || string.IsNullOrWhiteSpace(d.Description) || string.IsNullOrWhiteSpace(d.Publisher)
                || string.IsNullOrWhiteSpace(d.LatestVersion) || string.IsNullOrWhiteSpace(d.InstallUrl))
            {
                continue;
            }

            entries.Add(new PluginCatalogEntry(
                d.Id!, d.Name!, d.Description!, d.Publisher!, d.IconUrl, d.LatestVersion!, d.InstallUrl!, d.MinHostVersion));
        }
        return entries;
    }

    private sealed class Dto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("publisher")] public string? Publisher { get; set; }
        [JsonPropertyName("iconUrl")] public string? IconUrl { get; set; }
        [JsonPropertyName("latestVersion")] public string? LatestVersion { get; set; }
        [JsonPropertyName("installUrl")] public string? InstallUrl { get; set; }
        [JsonPropertyName("minHostVersion")] public string? MinHostVersion { get; set; }
    }
}
