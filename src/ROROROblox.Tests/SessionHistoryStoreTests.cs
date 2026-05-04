using System.IO;
using ROROROblox.Core;

namespace ROROROblox.Tests;

public class SessionHistoryStoreTests : IDisposable
{
    private readonly string _tempPath;

    public SessionHistoryStoreTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"rororoblox-history-test-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try { if (File.Exists(_tempPath)) File.Delete(_tempPath); }
        catch { }
    }

    private static LaunchSession SampleSession(
        Guid? id = null,
        string display = "Pokey",
        string game = "Pet Sim 99",
        DateTimeOffset? launchedAt = null,
        bool isPrivate = false)
    {
        return new LaunchSession(
            Id: id ?? Guid.NewGuid(),
            AccountId: Guid.NewGuid(),
            AccountDisplayName: display,
            AccountAvatarUrl: null,
            GameName: game,
            PlaceId: 12345L,
            IsPrivateServer: isPrivate,
            LaunchedAtUtc: launchedAt ?? DateTimeOffset.UtcNow,
            EndedAtUtc: null,
            OutcomeHint: null);
    }

    [Fact]
    public async Task ListAsync_OnFreshFile_ReturnsEmpty()
    {
        using var store = new SessionHistoryStore(_tempPath);
        var list = await store.ListAsync();
        Assert.Empty(list);
    }

    [Fact]
    public async Task AddAsync_AppendsRow()
    {
        using var store = new SessionHistoryStore(_tempPath);
        var session = SampleSession();

        await store.AddAsync(session);

        var list = await store.ListAsync();
        Assert.Single(list);
        Assert.Equal(session.Id, list[0].Id);
    }

    [Fact]
    public async Task ListAsync_ReturnsNewestFirst()
    {
        using var store = new SessionHistoryStore(_tempPath);
        var older = SampleSession(launchedAt: DateTimeOffset.UtcNow.AddHours(-2), display: "Old");
        var newer = SampleSession(launchedAt: DateTimeOffset.UtcNow.AddMinutes(-2), display: "New");

        await store.AddAsync(older);
        await store.AddAsync(newer);

        var list = await store.ListAsync();
        Assert.Equal(2, list.Count);
        Assert.Equal("New", list[0].AccountDisplayName);
        Assert.Equal("Old", list[1].AccountDisplayName);
    }

    [Fact]
    public async Task MarkEndedAsync_KnownId_StampsEndAndDuration()
    {
        using var store = new SessionHistoryStore(_tempPath);
        var session = SampleSession(launchedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        await store.AddAsync(session);

        await store.MarkEndedAsync(session.Id, DateTimeOffset.UtcNow);

        var list = await store.ListAsync();
        Assert.NotNull(list[0].EndedAtUtc);
        Assert.NotNull(list[0].Duration);
        Assert.True(list[0].Duration!.Value > TimeSpan.FromMinutes(4));
    }

    [Fact]
    public async Task MarkEndedAsync_WithOutcomeHint_PersistsHint()
    {
        using var store = new SessionHistoryStore(_tempPath);
        var session = SampleSession();
        await store.AddAsync(session);

        await store.MarkEndedAsync(session.Id, DateTimeOffset.UtcNow, "Never connected");

        var list = await store.ListAsync();
        Assert.Equal("Never connected", list[0].OutcomeHint);
    }

    [Fact]
    public async Task MarkEndedAsync_UnknownId_NoOp()
    {
        using var store = new SessionHistoryStore(_tempPath);
        await store.AddAsync(SampleSession());

        await store.MarkEndedAsync(Guid.NewGuid(), DateTimeOffset.UtcNow);

        var list = await store.ListAsync();
        Assert.Single(list);
        Assert.Null(list[0].EndedAtUtc);
    }

    [Fact]
    public async Task AddAsync_BeyondMaxRows_DropsOldest()
    {
        using var store = new SessionHistoryStore(_tempPath);
        var baseTime = DateTimeOffset.UtcNow.AddHours(-1);
        for (var i = 0; i < SessionHistoryStore.MaxRows + 5; i++)
        {
            await store.AddAsync(SampleSession(
                launchedAt: baseTime.AddSeconds(i),
                display: $"Acct{i}"));
        }

        var list = await store.ListAsync();
        Assert.Equal(SessionHistoryStore.MaxRows, list.Count);
        // Oldest rows (Acct0..Acct4) should have been dropped; newest (Acct104) at the head.
        Assert.Equal($"Acct{SessionHistoryStore.MaxRows + 4}", list[0].AccountDisplayName);
    }

    [Fact]
    public async Task ClearAsync_EmptiesTheStore()
    {
        using var store = new SessionHistoryStore(_tempPath);
        await store.AddAsync(SampleSession());
        await store.AddAsync(SampleSession());

        await store.ClearAsync();

        var list = await store.ListAsync();
        Assert.Empty(list);
    }

    [Fact]
    public async Task PersistsAcrossInstances()
    {
        var session = SampleSession();
        {
            using var first = new SessionHistoryStore(_tempPath);
            await first.AddAsync(session);
        }
        using var second = new SessionHistoryStore(_tempPath);
        var list = await second.ListAsync();
        Assert.Single(list);
        Assert.Equal(session.Id, list[0].Id);
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_FallsBackToEmpty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_tempPath)!);
        await File.WriteAllTextAsync(_tempPath, "not json");

        using var store = new SessionHistoryStore(_tempPath);
        Assert.Empty(await store.ListAsync());
    }
}
