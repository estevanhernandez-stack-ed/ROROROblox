using ROROROblox.App.Plugins;
using System.Net.Http;

namespace ROROROblox.Tests;

public class PluginCatalogTests
{
    private const string GoodJson = """
    [
      {
        "id": "626labs.ur-task",
        "name": "Ur Task",
        "description": "Record once, play on any alt.",
        "publisher": "626 Labs",
        "iconUrl": "https://example.invalid/ico.png",
        "latestVersion": "0.4.0",
        "installUrl": "https://github.com/estevanhernandez-stack-ed/rororo-ur-task/releases/latest/download/",
        "minHostVersion": "1.8.0.0"
      }
    ]
    """;

    [Fact]
    public void Parse_WellFormed_ReturnsEntries()
    {
        var entries = PluginCatalogParser.Parse(GoodJson);

        var e = Assert.Single(entries);
        Assert.Equal("626labs.ur-task", e.Id);
        Assert.Equal("Ur Task", e.Name);
        Assert.Equal("0.4.0", e.LatestVersion);
        Assert.Equal("https://github.com/estevanhernandez-stack-ed/rororo-ur-task/releases/latest/download/", e.InstallUrl);
        Assert.Equal("1.8.0.0", e.MinHostVersion);
    }

    [Fact]
    public void Parse_Malformed_ReturnsEmpty()
    {
        Assert.Empty(PluginCatalogParser.Parse("{ not json"));
    }

    [Fact]
    public void Parse_EmptyArray_ReturnsEmpty()
    {
        Assert.Empty(PluginCatalogParser.Parse("[]"));
    }

    [Fact]
    public void Parse_EntryMissingRequiredField_IsSkipped()
    {
        // Missing installUrl → that entry is dropped (an entry with no install target is useless).
        const string json = """
        [ { "id": "x.y", "name": "X", "description": "d", "publisher": "p", "latestVersion": "1.0" } ]
        """;

        Assert.Empty(PluginCatalogParser.Parse(json));
    }

    [Fact]
    public async Task Client_FetchThrows_ReturnsEmpty()
    {
        var client = new PluginCatalogClient(_ => throw new HttpRequestException("offline"));

        Assert.Empty(await client.FetchAsync());
    }

    [Fact]
    public async Task Client_FetchReturnsGoodJson_ReturnsEntries()
    {
        var client = new PluginCatalogClient(_ => Task.FromResult(GoodJson));

        Assert.Single(await client.FetchAsync());
    }
}
