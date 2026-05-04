using System.IO;
using ROROROblox.Core;

namespace ROROROblox.Tests;

public class AppSettingsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public AppSettingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ROROROblox-settings-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "settings.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetDefaultPlaceUrlAsync_ReturnsNull_WhenFileDoesNotExist()
    {
        using var settings = new AppSettings(_filePath);

        var url = await settings.GetDefaultPlaceUrlAsync();

        Assert.Null(url);
    }

    [Fact]
    public async Task SetThenGet_RoundTrips()
    {
        using var settings = new AppSettings(_filePath);

        await settings.SetDefaultPlaceUrlAsync("https://www.roblox.com/games/920587237/Adopt-Me");

        var url = await settings.GetDefaultPlaceUrlAsync();
        Assert.Equal("https://www.roblox.com/games/920587237/Adopt-Me", url);
    }

    [Fact]
    public async Task SetDefaultPlaceUrlAsync_RejectsEmptyOrWhitespace()
    {
        using var settings = new AppSettings(_filePath);

        await Assert.ThrowsAsync<ArgumentException>(() => settings.SetDefaultPlaceUrlAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => settings.SetDefaultPlaceUrlAsync("   "));
    }

    [Fact]
    public async Task ColdStart_ReadsBackPersistedValue()
    {
        var first = new AppSettings(_filePath);
        await first.SetDefaultPlaceUrlAsync("https://x.example/place");
        first.Dispose();

        using var second = new AppSettings(_filePath);
        var url = await second.GetDefaultPlaceUrlAsync();

        Assert.Equal("https://x.example/place", url);
    }

    [Fact]
    public async Task TamperedJson_ReturnsNullDefault()
    {
        File.WriteAllText(_filePath, "{ this is not valid JSON");
        using var settings = new AppSettings(_filePath);

        var url = await settings.GetDefaultPlaceUrlAsync();

        Assert.Null(url);
    }

    [Fact]
    public async Task SaveAsync_RemovesTempFile_OnSuccess()
    {
        using var settings = new AppSettings(_filePath);
        await settings.SetDefaultPlaceUrlAsync("https://x");

        var tempPath = _filePath + ".tmp";
        Assert.False(File.Exists(tempPath));
        Assert.True(File.Exists(_filePath));
    }

    [Fact]
    public async Task SetDefaultPlaceUrlAsync_OverwritesExistingValue()
    {
        using var settings = new AppSettings(_filePath);

        await settings.SetDefaultPlaceUrlAsync("https://first");
        await settings.SetDefaultPlaceUrlAsync("https://second");

        var url = await settings.GetDefaultPlaceUrlAsync();
        Assert.Equal("https://second", url);
    }
}
