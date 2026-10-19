using System.IO;
using System.Text;
using ROROROblox.Core.Theming;

namespace ROROROblox.Tests;

public class ThemeShareTests : IDisposable
{
    private const string SunsetJson = """
        {
          "name": "Sunset",
          "bg": "#1a0a1f",
          "cyan": "#3bb4d9",
          "magenta": "#ff6fa1",
          "white": "#f5e0f0",
          "muted_text": "#8a6a82",
          "divider": "#4a2a5e",
          "row_bg": "#2a1835",
          "row_expired_bg": "#3a2d14",
          "row_expired_accent": "#fbbf24",
          "navy": "#140a18"
        }
        """;

    private readonly string _tempDir;

    public ThemeShareTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"rororoblox-themeshare-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    private async Task<Theme> SavedSunsetAsync() => await new ThemeStore(_tempDir).SaveUserThemeAsync(SunsetJson);

    [Fact]
    public void ValidateImport_GoodJson_ReturnsTheme()
    {
        var check = ThemeShare.ValidateImport(Bytes(SunsetJson));

        Assert.True(check.IsValid);
        Assert.Equal("sunset", check.Theme!.Id);
        Assert.Equal("Sunset", check.Theme.Name);
        Assert.False(check.Theme.IsBuiltIn);
    }

    [Fact]
    public void ValidateImport_ByteOrderMark_StillParses()
    {
        var withBom = Encoding.UTF8.GetPreamble().Concat(Bytes(SunsetJson)).ToArray();

        Assert.True(ThemeShare.ValidateImport(withBom).IsValid);
    }

    [Fact]
    public void ValidateImport_BadJson_RefusesWithUnreadableJson()
    {
        var check = ThemeShare.ValidateImport(Bytes("{ not json"));

        Assert.False(check.IsValid);
        Assert.Equal(ThemeImportRefusal.Invalid, check.Refusal);
        Assert.Equal(InvalidThemeKind.UnreadableJson, check.Invalid!.Kind);
    }

    [Fact]
    public void ValidateImport_MissingField_RefusesAndNamesIt()
    {
        var check = ThemeShare.ValidateImport(Bytes("""{ "name": "Half", "bg": "#000000" }"""));

        Assert.Equal(ThemeImportRefusal.Invalid, check.Refusal);
        Assert.Equal(InvalidThemeKind.MissingField, check.Invalid!.Kind);
    }

    [Fact]
    public void ValidateImport_EmptyFile_Refuses()
    {
        var check = ThemeShare.ValidateImport([]);

        Assert.Equal(ThemeImportRefusal.Invalid, check.Refusal);
        Assert.Equal(InvalidThemeKind.EmptyInput, check.Invalid!.Kind);
    }

    [Theory]
    [InlineData("Brand")]
    [InlineData("Midnight")]
    [InlineData("Magenta Heat")]
    [InlineData("flatline")]
    public void ValidateImport_BuiltInId_Refuses(string name)
    {
        var json = SunsetJson.Replace("\"Sunset\"", $"\"{name}\"");

        var check = ThemeShare.ValidateImport(Bytes(json));

        Assert.False(check.IsValid);
        Assert.Equal(ThemeImportRefusal.BuiltInId, check.Refusal);
    }

    [Fact]
    public void ImportInto_GoodJson_WritesIdDotJsonTheStoreCanList()
    {
        var result = ThemeShare.ImportInto(_tempDir, Bytes(SunsetJson));

        Assert.Equal(ThemeImportOutcome.Written, result.Outcome);
        Assert.True(File.Exists(Path.Combine(_tempDir, "sunset.json")));
        Assert.False(File.Exists(Path.Combine(_tempDir, "sunset.json.tmp")));
        var listed = new ThemeStore(_tempDir).ListAsync().GetAwaiter().GetResult();
        Assert.Contains(listed, t => t.Id == "sunset");
    }

    [Fact]
    public void ImportInto_CreatesTheFolderWhenMissing()
    {
        var nested = Path.Combine(_tempDir, "not-yet");

        var result = ThemeShare.ImportInto(nested, Bytes(SunsetJson));

        Assert.Equal(ThemeImportOutcome.Written, result.Outcome);
        Assert.True(File.Exists(Path.Combine(nested, "sunset.json")));
    }

    [Fact]
    public void ImportInto_ExistingId_RefusesAndLeavesTheFileAlone()
    {
        var path = Path.Combine(_tempDir, "sunset.json");
        File.WriteAllText(path, "original");

        var result = ThemeShare.ImportInto(_tempDir, Bytes(SunsetJson));

        Assert.Equal(ThemeImportOutcome.AlreadyExists, result.Outcome);
        Assert.Equal("original", File.ReadAllText(path));
    }

    [Fact]
    public void ImportInto_ExistingId_OverwritesWhenTold()
    {
        var path = Path.Combine(_tempDir, "sunset.json");
        File.WriteAllText(path, "original");

        var result = ThemeShare.ImportInto(_tempDir, Bytes(SunsetJson), overwrite: true);

        Assert.Equal(ThemeImportOutcome.Written, result.Outcome);
        Assert.NotEqual("original", File.ReadAllText(path));
    }

    [Fact]
    public void ImportInto_BadJson_ThrowsInvalidTheme_AndWritesNothing()
    {
        Assert.Throws<InvalidThemeException>(() => ThemeShare.ImportInto(_tempDir, Bytes("{ nope")));
        Assert.Empty(Directory.GetFiles(_tempDir));
    }

    [Fact]
    public void ImportInto_BuiltInId_Throws_AndWritesNothing()
    {
        var json = SunsetJson.Replace("\"Sunset\"", "\"Brand\"");

        Assert.Throws<ArgumentException>(() => ThemeShare.ImportInto(_tempDir, Bytes(json)));
        Assert.Empty(Directory.GetFiles(_tempDir));
    }

    [Fact]
    public async Task ExportBytes_IsTheJsonTheStoreWrites()
    {
        var saved = await SavedSunsetAsync();
        var onDisk = await File.ReadAllBytesAsync(Path.Combine(_tempDir, "sunset.json"));

        var export = ThemeShare.ExportBytes(saved);

        Assert.Equal(onDisk, export.Bytes);
        Assert.Equal("Sunset.json", export.SuggestedFileName);
    }

    [Fact]
    public void ExportBytes_BuiltIn_Throws()
    {
        var brand = new ThemeStore(_tempDir).ListAsync().GetAwaiter().GetResult().First(t => t.Id == "brand");

        Assert.Throws<ArgumentException>(() => ThemeShare.ExportBytes(brand));
    }

    [Fact]
    public async Task ExportBytes_FileNameDropsCharactersWindowsRefuses()
    {
        var saved = await new ThemeStore(_tempDir).SaveUserThemeAsync(SunsetJson.Replace("\"Sunset\"", "\"Dusk: v2 / final?\""));

        var export = ThemeShare.ExportBytes(saved);

        Assert.Empty(export.SuggestedFileName.Intersect(Path.GetInvalidFileNameChars()));
        Assert.EndsWith(".json", export.SuggestedFileName);
        Assert.StartsWith("Dusk", export.SuggestedFileName);
    }

    [Fact]
    public async Task RoundTrip_ExportThenImport_YieldsTheSameTheme()
    {
        var saved = await SavedSunsetAsync();
        var export = ThemeShare.ExportBytes(saved);
        var otherFolder = Path.Combine(_tempDir, "friend");

        var result = ThemeShare.ImportInto(otherFolder, export.Bytes);

        Assert.Equal(ThemeImportOutcome.Written, result.Outcome);
        Assert.Equal(saved, result.Theme);
        var reloaded = (await new ThemeStore(otherFolder).ListAsync()).Single(t => !t.IsBuiltIn);
        Assert.Equal(saved, reloaded);
        Assert.Equal(export.Bytes, await File.ReadAllBytesAsync(Path.Combine(otherFolder, "sunset.json")));
    }
}
