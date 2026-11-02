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

    // ---- v1.36 item 4: PrivateServerId and UpdateGameAsync ----

    [Fact]
    public async Task UpdateGameAsync_PatchesGameAndPlaceOnOneRow_AndNoOther()
    {
        using var store = new SessionHistoryStore(_tempPath);
        var follow = SampleSession(game: "Following a friend") with { PlaceId = null };
        var other = SampleSession(game: "Other", launchedAt: DateTimeOffset.UtcNow.AddMinutes(-5)) with { PlaceId = null };
        await store.AddAsync(follow);
        await store.AddAsync(other);
        var serverId = Guid.NewGuid();

        await store.UpdateGameAsync(follow.Id, "Pet Sim 99", 8737899170, serverId);

        var rows = await store.ListAsync();
        var patched = rows.Single(r => r.Id == follow.Id);
        Assert.Equal("Pet Sim 99", patched.GameName);
        Assert.Equal(8737899170, patched.PlaceId);
        Assert.Equal(serverId, patched.PrivateServerId);
        Assert.Equal(follow.AccountId, patched.AccountId);
        Assert.Equal(follow.LaunchedAtUtc, patched.LaunchedAtUtc);
        Assert.Equal(other, rows.Single(r => r.Id == other.Id)); // untouched, field for field
    }

    [Fact]
    public async Task UpdateGameAsync_WithANullName_KeepsTheRowsName_AndAMissingRowIsANoOp()
    {
        using var store = new SessionHistoryStore(_tempPath);
        var row = SampleSession(game: "Keep Me") with { PlaceId = null };
        await store.AddAsync(row);

        await store.UpdateGameAsync(row.Id, null, 55, null);
        await store.UpdateGameAsync(Guid.NewGuid(), "Nobody", 66, null);

        var only = Assert.Single(await store.ListAsync());
        Assert.Equal("Keep Me", only.GameName);
        Assert.Equal(55, only.PlaceId);
    }

    [Fact]
    public async Task ALegacyRow_WithNoPrivateServerIdOnDisk_ReadsNull()
    {
        var id = Guid.NewGuid();
        await File.WriteAllTextAsync(_tempPath, """
            {"version":1,"sessions":[{"id":"ROWID","accountId":"22222222-2222-2222-2222-222222222222",
            "accountDisplayName":"Pokey","gameName":"Old","placeId":1,"isPrivateServer":true,
            "launchedAtUtc":"2026-09-01T00:00:00+00:00"}]}
            """.Replace("ROWID", id.ToString()));
        using var store = new SessionHistoryStore(_tempPath);

        var row = Assert.Single(await store.ListAsync());

        Assert.Null(row.PrivateServerId);
        Assert.Equal(id, row.Id);
    }
}
