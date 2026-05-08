using System.IO;
using System.Xml.Linq;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Tests use temp directories that mimic the %LOCALAPPDATA%\Roblox layout.
/// The writer accepts an override root via its public constructor.
/// </summary>
public sealed class GlobalBasicSettingsWriterTests : IDisposable
{
    private readonly string _tempRoot;

    public GlobalBasicSettingsWriterTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "rororo-gbs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    private static string RealisticSettingsXml(int frameRateCap) =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <roblox xmlns:xmime="http://www.w3.org/2005/05/xmlmime" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="http://www.roblox.com/roblox.xsd" version="4">
        	<External>null</External>
        	<External>nil</External>
        	<Item class="UserGameSettings" referent="RBX9189099697774B2490D0CB66C3BEFF14">
        		<Properties>
        			<bool name="AllTutorialsDisabled">false</bool>
        			<bool name="BadgeVisible">true</bool>
        			<int name="FramerateCap">{frameRateCap}</int>
        			<bool name="Fullscreen">false</bool>
        			<int name="GraphicsQualityLevel">0</int>
        		</Properties>
        	</Item>
        </roblox>
        """;

    private string SeedSettings(string fileName, string contents)
    {
        var path = Path.Combine(_tempRoot, fileName);
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public async Task WriteFramerateCapAsync_UpdatesExistingNode()
    {
        var path = SeedSettings("GlobalBasicSettings_13.xml", RealisticSettingsXml(60));
        var writer = new GlobalBasicSettingsWriter(_tempRoot);

        await writer.WriteFramerateCapAsync(20);

        var doc = XDocument.Load(path);
        var node = doc.Descendants("int").Single(e => (string?)e.Attribute("name") == "FramerateCap");
        Assert.Equal("20", node.Value);
    }

    [Fact]
    public async Task WriteFramerateCapAsync_PreservesOtherProperties()
    {
        var path = SeedSettings("GlobalBasicSettings_13.xml", RealisticSettingsXml(60));
        var writer = new GlobalBasicSettingsWriter(_tempRoot);

        await writer.WriteFramerateCapAsync(144);

        var doc = XDocument.Load(path);
        var props = doc.Descendants("Properties").Single();
        Assert.Equal("false", props.Elements("bool").Single(e => (string?)e.Attribute("name") == "AllTutorialsDisabled").Value);
        Assert.Equal("true", props.Elements("bool").Single(e => (string?)e.Attribute("name") == "BadgeVisible").Value);
        Assert.Equal("false", props.Elements("bool").Single(e => (string?)e.Attribute("name") == "Fullscreen").Value);
        Assert.Equal("0", props.Elements("int").Single(e => (string?)e.Attribute("name") == "GraphicsQualityLevel").Value);
    }

    [Fact]
    public async Task WriteFramerateCapAsync_PicksHighestSchemaVersion()
    {
        // Both files exist; writer must update _13 (the higher schema), not _12.
        var path12 = SeedSettings("GlobalBasicSettings_12.xml", RealisticSettingsXml(60));
        var path13 = SeedSettings("GlobalBasicSettings_13.xml", RealisticSettingsXml(60));
        var writer = new GlobalBasicSettingsWriter(_tempRoot);

        await writer.WriteFramerateCapAsync(30);

        var doc12 = XDocument.Load(path12);
        var doc13 = XDocument.Load(path13);
        Assert.Equal("60", doc12.Descendants("int").Single(e => (string?)e.Attribute("name") == "FramerateCap").Value);
        Assert.Equal("30", doc13.Descendants("int").Single(e => (string?)e.Attribute("name") == "FramerateCap").Value);
    }

    [Fact]
    public async Task WriteFramerateCapAsync_SkipsStudioVariant()
    {
        // _Studio is for Roblox Studio (developer tool); writer must ignore it.
        SeedSettings("GlobalBasicSettings_13_Studio.xml", RealisticSettingsXml(60));
        var path13 = SeedSettings("GlobalBasicSettings_13.xml", RealisticSettingsXml(60));
        var writer = new GlobalBasicSettingsWriter(_tempRoot);

        await writer.WriteFramerateCapAsync(45);

        var doc = XDocument.Load(path13);
        Assert.Equal("45", doc.Descendants("int").Single(e => (string?)e.Attribute("name") == "FramerateCap").Value);
    }

    [Fact]
    public async Task WriteFramerateCapAsync_NullLeavesFileUntouched()
    {
        var path = SeedSettings("GlobalBasicSettings_13.xml", RealisticSettingsXml(60));
        var beforeContents = File.ReadAllText(path);
        var beforeMtime = File.GetLastWriteTimeUtc(path);
        var writer = new GlobalBasicSettingsWriter(_tempRoot);

        await writer.WriteFramerateCapAsync(null);

        Assert.Equal(beforeContents, File.ReadAllText(path));
        Assert.Equal(beforeMtime, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public async Task WriteFramerateCapAsync_NoFile_Throws()
    {
        var writer = new GlobalBasicSettingsWriter(_tempRoot);
        await Assert.ThrowsAsync<GlobalBasicSettingsWriteException>(
            () => writer.WriteFramerateCapAsync(60));
    }

    [Fact]
    public async Task WriteFramerateCapAsync_MalformedXml_Throws()
    {
        SeedSettings("GlobalBasicSettings_13.xml", "this is not xml at all");
        var writer = new GlobalBasicSettingsWriter(_tempRoot);
        await Assert.ThrowsAsync<GlobalBasicSettingsWriteException>(
            () => writer.WriteFramerateCapAsync(60));
    }

    [Fact]
    public async Task WriteFramerateCapAsync_MissingFramerateCapNode_InsertsIt()
    {
        // Pre-existing settings without the FramerateCap node — defensive insert path.
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <roblox version="4">
            	<Item class="UserGameSettings" referent="RBX1">
            		<Properties>
            			<bool name="BadgeVisible">true</bool>
            		</Properties>
            	</Item>
            </roblox>
            """;
        var path = SeedSettings("GlobalBasicSettings_13.xml", xml);
        var writer = new GlobalBasicSettingsWriter(_tempRoot);

        await writer.WriteFramerateCapAsync(75);

        var doc = XDocument.Load(path);
        var node = doc.Descendants("int").Single(e => (string?)e.Attribute("name") == "FramerateCap");
        Assert.Equal("75", node.Value);
        // Pre-existing prop survives.
        Assert.Equal("true", doc.Descendants("bool").Single(e => (string?)e.Attribute("name") == "BadgeVisible").Value);
    }

    [Fact]
    public async Task WriteFramerateCapAsync_AtomicWrite_LeavesNoTempFileOnSuccess()
    {
        var path = SeedSettings("GlobalBasicSettings_13.xml", RealisticSettingsXml(60));
        var writer = new GlobalBasicSettingsWriter(_tempRoot);

        await writer.WriteFramerateCapAsync(30);

        Assert.False(File.Exists(path + ".tmp"));
    }
}
