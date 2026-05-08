using System.IO;
using System.Security.Cryptography;
using System.Text;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Item 5 (v1.3.x) — verifies <see cref="RenameDispatch.ApplyAsync"/> routes by
/// <see cref="RenameTargetKind"/>. Uses real stores against temp files so the dispatch IS
/// the unit under test, not the per-store update logic (those are covered separately in
/// FavoriteGameStoreTests / PrivateServerStoreTests / AccountStoreTests).
/// </summary>
public class RenameDispatchTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _favoritesPath;
    private readonly string _privateServersPath;
    private readonly string _accountsPath;

    public RenameDispatchTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ROROROblox-dispatch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _favoritesPath = Path.Combine(_tempDir, "favorites.json");
        _privateServersPath = Path.Combine(_tempDir, "private-servers.json");
        _accountsPath = Path.Combine(_tempDir, "accounts.dat");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ApplyAsync_GameKind_RoutesToFavoriteStoreOnly()
    {
        using var favorites = new FavoriteGameStore(_favoritesPath);
        using var privateServers = new PrivateServerStore(_privateServersPath);
        using var accounts = new AccountStore(_accountsPath);

        await favorites.AddAsync(111, 1, "Adopt Me", "https://x");
        var serverAdded = await privateServers.AddAsync(222, "code", PrivateServerCodeKind.LinkCode, "Squad", "Place", "");
        var accountAdded = await accounts.AddAsync("TestUser", "https://avatar", "fake-cookie");

        var target = new RenameTarget(RenameTargetKind.Game, 111L, "Adopt Me", null);
        await RenameDispatch.ApplyAsync(favorites, privateServers, accounts, target, "My Adopt Me");

        Assert.Equal("My Adopt Me", (await favorites.ListAsync())[0].LocalName);
        Assert.Null((await privateServers.ListAsync())[0].LocalName);
        Assert.Null((await accounts.ListAsync())[0].LocalName);
    }

    [Fact]
    public async Task ApplyAsync_PrivateServerKind_RoutesToPrivateServerStoreOnly()
    {
        using var favorites = new FavoriteGameStore(_favoritesPath);
        using var privateServers = new PrivateServerStore(_privateServersPath);
        using var accounts = new AccountStore(_accountsPath);

        await favorites.AddAsync(111, 1, "Adopt Me", "https://x");
        var serverAdded = await privateServers.AddAsync(222, "code", PrivateServerCodeKind.LinkCode, "Squad", "Place", "");
        var accountAdded = await accounts.AddAsync("TestUser", "https://avatar", "fake-cookie");

        var target = new RenameTarget(RenameTargetKind.PrivateServer, serverAdded.Id, "Squad", null);
        await RenameDispatch.ApplyAsync(favorites, privateServers, accounts, target, "My Squad");

        Assert.Null((await favorites.ListAsync())[0].LocalName);
        Assert.Equal("My Squad", (await privateServers.ListAsync())[0].LocalName);
        Assert.Null((await accounts.ListAsync())[0].LocalName);
    }

    [Fact]
    public async Task ApplyAsync_AccountKind_RoutesToAccountStoreOnly()
    {
        using var favorites = new FavoriteGameStore(_favoritesPath);
        using var privateServers = new PrivateServerStore(_privateServersPath);
        using var accounts = new AccountStore(_accountsPath);

        await favorites.AddAsync(111, 1, "Adopt Me", "https://x");
        var serverAdded = await privateServers.AddAsync(222, "code", PrivateServerCodeKind.LinkCode, "Squad", "Place", "");
        var accountAdded = await accounts.AddAsync("TestUser", "https://avatar", "fake-cookie");

        var target = new RenameTarget(RenameTargetKind.Account, accountAdded.Id, "TestUser", null);
        await RenameDispatch.ApplyAsync(favorites, privateServers, accounts, target, "Mr. Solo Dolo");

        Assert.Null((await favorites.ListAsync())[0].LocalName);
        Assert.Null((await privateServers.ListAsync())[0].LocalName);
        Assert.Equal("Mr. Solo Dolo", (await accounts.ListAsync())[0].LocalName);
    }

    [Fact]
    public async Task ApplyAsync_NullNewName_ResetsLocalName()
    {
        using var favorites = new FavoriteGameStore(_favoritesPath);
        using var privateServers = new PrivateServerStore(_privateServersPath);
        using var accounts = new AccountStore(_accountsPath);

        await favorites.AddAsync(111, 1, "Adopt Me", "https://x");
        await favorites.UpdateLocalNameAsync(111, "Custom");
        var serverAdded = await privateServers.AddAsync(222, "code", PrivateServerCodeKind.LinkCode, "Squad", "Place", "");
        var accountAdded = await accounts.AddAsync("TestUser", "https://avatar", "fake-cookie");

        var target = new RenameTarget(RenameTargetKind.Game, 111L, "Adopt Me", "Custom");
        await RenameDispatch.ApplyAsync(favorites, privateServers, accounts, target, newLocalName: null);

        Assert.Null((await favorites.ListAsync())[0].LocalName);
    }

    [Fact]
    public async Task ApplyAsync_MissingIdOnRoutedStore_ThrowsKeyNotFoundException()
    {
        using var favorites = new FavoriteGameStore(_favoritesPath);
        using var privateServers = new PrivateServerStore(_privateServersPath);
        using var accounts = new AccountStore(_accountsPath);

        // No game added — dispatch with placeId=999 should bubble KeyNotFoundException from the
        // store unchanged (the dispatcher never swallows store exceptions).
        var target = new RenameTarget(RenameTargetKind.Game, 999L, "Phantom", null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            RenameDispatch.ApplyAsync(favorites, privateServers, accounts, target, "Custom"));
    }

    [Fact]
    public async Task ApplyAsync_NullStores_ThrowsArgumentNullException()
    {
        using var favorites = new FavoriteGameStore(_favoritesPath);
        using var privateServers = new PrivateServerStore(_privateServersPath);
        using var accounts = new AccountStore(_accountsPath);

        var target = new RenameTarget(RenameTargetKind.Game, 1L, "x", null);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            RenameDispatch.ApplyAsync(null!, privateServers, accounts, target, "x"));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            RenameDispatch.ApplyAsync(favorites, null!, accounts, target, "x"));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            RenameDispatch.ApplyAsync(favorites, privateServers, null!, target, "x"));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            RenameDispatch.ApplyAsync(favorites, privateServers, accounts, target: null!, "x"));
    }
}
