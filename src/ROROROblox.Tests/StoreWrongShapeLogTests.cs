using System.IO;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// A wrong-shape store file is logged once at Warning, with the file name and never the path or the
/// content, however many times the store loads. Separate from the table so the table keeps compiling
/// against a store that has no logger.
/// </summary>
public sealed class StoreWrongShapeLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"rororo-shape-log-{Guid.NewGuid():N}");

    public StoreWrongShapeLogTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Favorites_WrongShape_WarnsOnceWithTheFileName()
    {
        var path = Path.Combine(_dir, "favorites.json");
        File.WriteAllText(path, """{"version":1}""");
        var log = new CapturingLogger<FavoriteGameStore>();
        using var store = new FavoriteGameStore(path, log);

        await store.ListAsync();
        await store.ListAsync();
        await store.GetDefaultAsync();

        var warning = Assert.Single(log.Snapshot(), l => l.StartsWith("[Warning]", StringComparison.Ordinal));
        Assert.Contains("favorites.json", warning);
        Assert.DoesNotContain(_dir, warning);
    }

    [Fact]
    public async Task SessionStats_WrongShape_WarnsOnce()
    {
        var path = Path.Combine(_dir, "session-stats.json");
        File.WriteAllText(path, """{"accounts":null}""");
        var log = new CapturingLogger<SessionStatsStore>();
        using var store = new SessionStatsStore(path, log);

        await store.ReadAsync();
        await store.ReadAsync();

        Assert.Single(log.Snapshot(), l => l.StartsWith("[Warning]", StringComparison.Ordinal) && l.Contains("session-stats.json"));
    }

    [Fact]
    public async Task HealthyFiles_LogNothing()
    {
        var path = Path.Combine(_dir, "favorites.json");
        var log = new CapturingLogger<FavoriteGameStore>();
        using var store = new FavoriteGameStore(path, log);
        await store.AddAsync(1, 1, "n", "t");

        await store.ListAsync();

        Assert.Empty(log.Snapshot());
    }
}
