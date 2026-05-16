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

    /// <summary>Initial autostart preference for a fresh install: "on" or "off". Null = "off" (current default).
    /// Does NOT override an existing consent record on re-install — set once at first install.</summary>
    public string? AutostartDefault { get; init; }

    /// <summary>Minimum RoRoRo version required to install. Compared via <see cref="System.Version"/>;
    /// pre-release tags (e.g. "1.4.3-beta") tolerated by parsing the numeric head. Null = no constraint.</summary>
    public string? MinHostVersion { get; init; }

    /// <summary>Plugin EXE filename relative to install dir. Null = fall back to <c>&lt;id&gt;.exe</c>.</summary>
    public string? Entrypoint { get; init; }

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

        // autostartDefault is a string enum so a future "prompt" value can join without a schema bump.
        // Anything other than the known values is a manifest authoring bug — reject loudly.
        if (dto.AutostartDefault is not null
            && dto.AutostartDefault != "on"
            && dto.AutostartDefault != "off")
        {
            throw new PluginManifestException(
                $"Manifest autostartDefault '{dto.AutostartDefault}' is not recognized. Expected \"on\" or \"off\".");
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
            AutostartDefault = dto.AutostartDefault,
            MinHostVersion = dto.MinHostVersion,
            Entrypoint = dto.Entrypoint,
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
        [JsonPropertyName("autostartDefault")] public string? AutostartDefault { get; set; }
        [JsonPropertyName("minHostVersion")] public string? MinHostVersion { get; set; }
        [JsonPropertyName("entrypoint")] public string? Entrypoint { get; set; }
    }
}

public sealed class PluginManifestException : Exception
{
    public PluginManifestException(string message) : base(message) { }
    public PluginManifestException(string message, Exception inner) : base(message, inner) { }
}
