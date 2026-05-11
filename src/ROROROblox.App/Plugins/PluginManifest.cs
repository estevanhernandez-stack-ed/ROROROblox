using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ROROROblox.App.Plugins;

public sealed record PluginManifest
{
    public required int SchemaVersion { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string ContractVersion { get; init; }
    public required string Publisher { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<string> Capabilities { get; init; }
    public string? Icon { get; init; }
    public string? UpdateFeed { get; init; }

    public const int CurrentSchemaVersion = 1;
    private static readonly Regex IdPattern = new(@"^[a-z0-9]+(\.[a-z0-9-]+)+$", RegexOptions.Compiled);

    public static PluginManifest Parse(string json)
    {
        ManifestDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ManifestDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (JsonException ex)
        {
            throw new PluginManifestException($"Manifest JSON is malformed: {ex.Message}", ex);
        }

        if (dto is null)
        {
            throw new PluginManifestException("Manifest JSON parsed to null.");
        }

        if (dto.SchemaVersion is null)
        {
            throw new PluginManifestException("Manifest is missing schemaVersion.");
        }
        if (dto.SchemaVersion != CurrentSchemaVersion)
        {
            throw new PluginManifestException(
                $"Unsupported schemaVersion {dto.SchemaVersion}. This RoRoRo expects schemaVersion {CurrentSchemaVersion}.");
        }

        Require(dto.Id, "id");
        Require(dto.Name, "name");
        Require(dto.Version, "version");
        Require(dto.ContractVersion, "contractVersion");
        Require(dto.Publisher, "publisher");
        Require(dto.Description, "description");
        if (dto.Capabilities is null)
        {
            throw new PluginManifestException("Manifest is missing capabilities.");
        }

        if (!IdPattern.IsMatch(dto.Id!))
        {
            throw new PluginManifestException(
                $"Manifest id '{dto.Id}' is not in reverse-DNS form (e.g. '626labs.auto-keys').");
        }

        return new PluginManifest
        {
            SchemaVersion = dto.SchemaVersion.Value,
            Id = dto.Id!,
            Name = dto.Name!,
            Version = dto.Version!,
            ContractVersion = dto.ContractVersion!,
            Publisher = dto.Publisher!,
            Description = dto.Description!,
            Capabilities = dto.Capabilities,
            Icon = dto.Icon,
            UpdateFeed = dto.UpdateFeed,
        };
    }

    private static void Require(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new PluginManifestException($"Manifest is missing {fieldName}.");
        }
    }

    private sealed class ManifestDto
    {
        [JsonPropertyName("schemaVersion")] public int? SchemaVersion { get; set; }
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("version")] public string? Version { get; set; }
        [JsonPropertyName("contractVersion")] public string? ContractVersion { get; set; }
        [JsonPropertyName("publisher")] public string? Publisher { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("capabilities")] public List<string>? Capabilities { get; set; }
        [JsonPropertyName("icon")] public string? Icon { get; set; }
        [JsonPropertyName("updateFeed")] public string? UpdateFeed { get; set; }
    }
}

public sealed class PluginManifestException : Exception
{
    public PluginManifestException(string message) : base(message) { }
    public PluginManifestException(string message, Exception inner) : base(message, inner) { }
}
