using System.IO;
using ROROROblox.Core.Theming;

namespace ROROROblox.Tests;

public class ThemeStoreTests : IDisposable
{
    private readonly string _tempDir;

    public ThemeStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"rororoblox-themes-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ListAsync_ReturnsBuiltInsEvenWithEmptyFolder()
    {
        var store = new ThemeStore(_tempDir);
        var list = await store.ListAsync();

        Assert.Contains(list, t => t.Id == "brand" && t.IsBuiltIn);
        Assert.Contains(list, t => t.Id == "midnight" && t.IsBuiltIn);
        Assert.Contains(list, t => t.Id == "magenta-heat" && t.IsBuiltIn);
    }

    [Fact]
    public async Task ListAsync_LoadsValidUserTheme()
    {
        var json = """
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
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "sunset.json"), json);

        var store = new ThemeStore(_tempDir);
        var list = await store.ListAsync();

        var theme = list.SingleOrDefault(t => t.Id == "sunset");
        Assert.NotNull(theme);
        Assert.Equal("Sunset", theme!.Name);
        Assert.Equal("#1a0a1f", theme.Bg);
        Assert.Equal("#3bb4d9", theme.Cyan);
        Assert.False(theme.IsBuiltIn);
    }

    [Fact]
    public async Task ListAsync_SkipsMalformedJson_KeepsValidOnes()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "bad.json"), "{ this is not valid json");

        var validJson = """
        {
          "name": "Valid",
          "bg": "#000000",
          "cyan": "#00ffff",
          "magenta": "#ff00ff",
          "white": "#ffffff",
          "muted_text": "#888888",
          "divider": "#222222",
          "row_bg": "#111111",
          "row_expired_bg": "#332200",
          "row_expired_accent": "#ffaa00",
          "navy": "#000000"
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "valid.json"), validJson);

        var store = new ThemeStore(_tempDir);
        var list = await store.ListAsync();

        Assert.Contains(list, t => t.Id == "valid");
        Assert.DoesNotContain(list, t => t.Id == "bad");
    }

    [Fact]
    public async Task ListAsync_SkipsThemeMissingRequiredField()
    {
        // Missing "magenta" — record ctor throws inside RawTheme.ToTheme, file is dropped.
        var json = """
        {
          "name": "Incomplete",
          "bg": "#000000",
          "cyan": "#00ffff",
          "white": "#ffffff",
          "muted_text": "#888888",
          "divider": "#222222",
          "row_bg": "#111111",
          "row_expired_bg": "#332200",
          "row_expired_accent": "#ffaa00",
          "navy": "#000000"
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "incomplete.json"), json);

        var store = new ThemeStore(_tempDir);
        var list = await store.ListAsync();

        Assert.DoesNotContain(list, t => t.Id == "incomplete");
    }

    [Fact]
    public async Task ListAsync_UserThemeShadowingBuiltInId_LosesToBuiltIn()
    {
        // A user file named "brand.json" should NOT replace the built-in "brand" theme.
        var json = """
        {
          "name": "Hijacked Brand",
          "bg": "#ff0000",
          "cyan": "#ff0000",
          "magenta": "#ff0000",
          "white": "#ff0000",
          "muted_text": "#ff0000",
          "divider": "#ff0000",
          "row_bg": "#ff0000",
          "row_expired_bg": "#ff0000",
          "row_expired_accent": "#ff0000",
          "navy": "#ff0000"
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "brand.json"), json);

        var store = new ThemeStore(_tempDir);
        var list = await store.ListAsync();

        var brand = list.Single(t => t.Id == "brand");
        Assert.True(brand.IsBuiltIn);
        Assert.NotEqual("Hijacked Brand", brand.Name);
        Assert.NotEqual("#ff0000", brand.Bg);
    }

    [Fact]
    public async Task GetByIdAsync_KnownId_ReturnsTheme()
    {
        var store = new ThemeStore(_tempDir);
        var theme = await store.GetByIdAsync("midnight");
        Assert.NotNull(theme);
        Assert.Equal("Midnight", theme!.Name);
    }

    [Fact]
    public async Task GetByIdAsync_CaseInsensitive()
    {
        var store = new ThemeStore(_tempDir);
        Assert.NotNull(await store.GetByIdAsync("BRAND"));
        Assert.NotNull(await store.GetByIdAsync("Brand"));
        Assert.NotNull(await store.GetByIdAsync("brand"));
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        var store = new ThemeStore(_tempDir);
        Assert.Null(await store.GetByIdAsync("nope"));
    }

    [Fact]
    public async Task SaveUserThemeAsync_WritesFileAndReturnsParsedTheme()
    {
        var store = new ThemeStore(_tempDir);
        var json = """
        {
          "name": "Cool Test Theme",
          "bg": "#102030",
          "cyan": "#11aacc",
          "magenta": "#cc2299",
          "white": "#fafafa",
          "muted_text": "#7a8090",
          "divider": "#1a2030",
          "row_bg": "#152535",
          "row_expired_bg": "#3a2d14",
          "row_expired_accent": "#f1b232",
          "navy": "#0a1320"
        }
        """;

        var saved = await store.SaveUserThemeAsync(json);

        Assert.Equal("cool-test-theme", saved.Id);
        Assert.Equal("Cool Test Theme", saved.Name);
        Assert.Equal("#102030", saved.Bg);
        Assert.False(saved.IsBuiltIn);

        // File should exist with kebab id.
        var file = Path.Combine(_tempDir, "cool-test-theme.json");
        Assert.True(File.Exists(file));

        // And the saved theme should appear in subsequent ListAsync calls.
        var list = await store.ListAsync();
        Assert.Contains(list, t => t.Id == "cool-test-theme");
    }

    [Fact]
    public async Task SaveUserThemeAsync_OverwritesOnSameKebabId()
    {
        var store = new ThemeStore(_tempDir);
        var first = """
        {
          "name": "My Theme",
          "bg": "#000000",
          "cyan": "#000000",
          "magenta": "#000000",
          "white": "#ffffff",
          "muted_text": "#888888",
          "divider": "#000000",
          "row_bg": "#000000",
          "row_expired_bg": "#000000",
          "row_expired_accent": "#000000",
          "navy": "#000000"
        }
        """;
        var updated = """
        {
          "name": "My Theme",
          "bg": "#aabbcc",
          "cyan": "#000000",
          "magenta": "#000000",
          "white": "#ffffff",
          "muted_text": "#888888",
          "divider": "#000000",
          "row_bg": "#000000",
          "row_expired_bg": "#000000",
          "row_expired_accent": "#000000",
          "navy": "#000000"
        }
        """;

        await store.SaveUserThemeAsync(first);
        var second = await store.SaveUserThemeAsync(updated);

        Assert.Equal("#aabbcc", second.Bg);
        var list = await store.ListAsync();
        Assert.Single(list, t => t.Id == "my-theme");
    }

    [Fact]
    public async Task SaveUserThemeAsync_MalformedJson_ThrowsInvalidThemeException()
    {
        var store = new ThemeStore(_tempDir);

        var ex = await Assert.ThrowsAsync<InvalidThemeException>(() =>
            store.SaveUserThemeAsync("{ this is not valid json"));
        Assert.Contains("JSON", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveUserThemeAsync_MissingRequiredField_ThrowsInvalidThemeException()
    {
        var store = new ThemeStore(_tempDir);
        // Missing "magenta" field.
        var json = """
        {
          "name": "Incomplete",
          "bg": "#000000",
          "cyan": "#000000",
          "white": "#ffffff",
          "muted_text": "#888888",
          "divider": "#000000",
          "row_bg": "#000000",
          "row_expired_bg": "#000000",
          "row_expired_accent": "#000000",
          "navy": "#000000"
        }
        """;

        var ex = await Assert.ThrowsAsync<InvalidThemeException>(() => store.SaveUserThemeAsync(json));
        Assert.Contains("magenta", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveUserThemeAsync_MissingName_ThrowsInvalidThemeException()
    {
        var store = new ThemeStore(_tempDir);
        var json = """
        {
          "bg": "#000000",
          "cyan": "#000000",
          "magenta": "#000000",
          "white": "#ffffff",
          "muted_text": "#888888",
          "divider": "#000000",
          "row_bg": "#000000",
          "row_expired_bg": "#000000",
          "row_expired_accent": "#000000",
          "navy": "#000000"
        }
        """;

        await Assert.ThrowsAsync<InvalidThemeException>(() => store.SaveUserThemeAsync(json));
    }

    [Fact]
    public async Task SaveUserThemeAsync_EmptyInput_ThrowsInvalidThemeException()
    {
        var store = new ThemeStore(_tempDir);
        await Assert.ThrowsAsync<InvalidThemeException>(() => store.SaveUserThemeAsync(""));
        await Assert.ThrowsAsync<InvalidThemeException>(() => store.SaveUserThemeAsync("   "));
    }

    [Fact]
    public async Task ListAsync_MissingFolder_ReturnsBuiltInsOnly()
    {
        var nonexistent = Path.Combine(_tempDir, "does-not-exist-yet");
        var store = new ThemeStore(nonexistent);

        var list = await store.ListAsync();

        Assert.NotEmpty(list);
        Assert.All(list, t => Assert.True(t.IsBuiltIn));
    }
}
