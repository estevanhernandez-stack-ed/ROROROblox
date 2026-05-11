using System.Text.Json;
using ROROROblox.App.Plugins;

namespace ROROROblox.Tests;

public class PluginManifestTests
{
    private const string ValidManifestJson = """
    {
        "schemaVersion": 1,
        "id": "626labs.test-plugin",
        "name": "Test Plugin",
        "version": "0.1.0",
        "contractVersion": "1.0",
        "publisher": "626 Labs LLC",
        "description": "A test plugin.",
        "capabilities": ["host.events.account-launched", "host.ui.tray-menu"],
        "icon": "icon.png",
        "updateFeed": "https://example.com/feed.atom"
    }
    """;

    [Fact]
    public void Parse_ValidJson_ReturnsManifest()
    {
        var manifest = PluginManifest.Parse(ValidManifestJson);

        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal("626labs.test-plugin", manifest.Id);
        Assert.Equal("Test Plugin", manifest.Name);
        Assert.Equal("0.1.0", manifest.Version);
        Assert.Equal("1.0", manifest.ContractVersion);
        Assert.Contains("host.events.account-launched", manifest.Capabilities);
        Assert.Contains("host.ui.tray-menu", manifest.Capabilities);
    }

    [Fact]
    public void Parse_MissingRequiredField_Throws()
    {
        const string missingId = """
        { "schemaVersion": 1, "name": "x", "version": "1.0", "contractVersion": "1.0", "publisher": "x", "description": "x", "capabilities": [] }
        """;

        Assert.Throws<PluginManifestException>(() => PluginManifest.Parse(missingId));
    }

    [Fact]
    public void Parse_UnsupportedSchemaVersion_Throws()
    {
        const string futureSchema = """
        { "schemaVersion": 99, "id": "x", "name": "x", "version": "1.0", "contractVersion": "1.0", "publisher": "x", "description": "x", "capabilities": [] }
        """;

        var ex = Assert.Throws<PluginManifestException>(() => PluginManifest.Parse(futureSchema));
        Assert.Contains("schemaVersion", ex.Message);
    }

    [Fact]
    public void Parse_InvalidId_Throws()
    {
        const string badId = """
        { "schemaVersion": 1, "id": "Not Valid Id With Spaces", "name": "x", "version": "1.0", "contractVersion": "1.0", "publisher": "x", "description": "x", "capabilities": [] }
        """;

        Assert.Throws<PluginManifestException>(() => PluginManifest.Parse(badId));
    }
}
