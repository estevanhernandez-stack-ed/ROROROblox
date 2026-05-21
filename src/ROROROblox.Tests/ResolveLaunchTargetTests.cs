using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Pure unit coverage for <see cref="MainViewModel.ResolveLaunchTarget"/> — the row-picker →
/// launch-target mapping extracted from <c>LaunchAccountAsync</c> in v1.6.0 when saved private
/// servers became selectable in the per-account dropdown. No WPF / VM standup required.
/// </summary>
public class ResolveLaunchTargetTests
{
    private static FavoriteGame Game(long placeId, string name = "Adopt Me") =>
        new(placeId, UniverseId: 0, name, ThumbnailUrl: "", IsDefault: false, AddedAt: DateTimeOffset.UnixEpoch);

    private static FavoriteGame PsEntry(
        long placeId,
        string code,
        PrivateServerCodeKind kind,
        string name = "My VIP",
        Guid? id = null) =>
        new(placeId, UniverseId: 0, name, ThumbnailUrl: "", IsDefault: false, AddedAt: DateTimeOffset.UnixEpoch,
            LocalName: null,
            PrivateServerCode: code,
            PrivateServerCodeKind: kind,
            PrivateServerId: id ?? Guid.NewGuid());

    // === Override always wins ===

    [Fact]
    public void Override_TrumpsRowSelection()
    {
        var over = new LaunchTarget.FollowFriend(98765);

        var target = MainViewModel.ResolveLaunchTarget(Game(920587237), over);

        Assert.Same(over, target);
    }

    [Fact]
    public void Override_TrumpsEvenAPrivateServerRow()
    {
        var over = new LaunchTarget.Place(111);

        var target = MainViewModel.ResolveLaunchTarget(
            PsEntry(920587237, "LINK", PrivateServerCodeKind.LinkCode), over);

        Assert.Same(over, target);
    }

    [Fact]
    public void Override_TrumpsNullSelection()
    {
        var over = new LaunchTarget.Place(222);

        var target = MainViewModel.ResolveLaunchTarget(selected: null, over);

        Assert.Same(over, target);
    }

    // === Private-server entry → LaunchTarget.PrivateServer (branches BEFORE plain Place) ===

    [Fact]
    public void PrivateServerEntry_LinkCode_MapsToPrivateServerTarget()
    {
        var entry = PsEntry(920587237, "ABC-123_xyz", PrivateServerCodeKind.LinkCode);

        var target = MainViewModel.ResolveLaunchTarget(entry, overrideTarget: null);

        var ps = Assert.IsType<LaunchTarget.PrivateServer>(target);
        Assert.Equal(920587237L, ps.PlaceId);
        Assert.Equal("ABC-123_xyz", ps.Code);
        Assert.Equal(PrivateServerCodeKind.LinkCode, ps.Kind);
    }

    [Fact]
    public void PrivateServerEntry_AccessCode_PreservesKind()
    {
        var entry = PsEntry(42, "secret", PrivateServerCodeKind.AccessCode);

        var target = MainViewModel.ResolveLaunchTarget(entry, overrideTarget: null);

        var ps = Assert.IsType<LaunchTarget.PrivateServer>(target);
        Assert.Equal(42L, ps.PlaceId);
        Assert.Equal("secret", ps.Code);
        Assert.Equal(PrivateServerCodeKind.AccessCode, ps.Kind);
    }

    [Fact]
    public void PrivateServerEntry_NullKind_DefaultsToLinkCode()
    {
        // Defensive: a PS entry built without an explicit kind (e.g. legacy record) defaults to
        // LinkCode — the form users paste 95% of the time. Mirrors SavedPrivateServer.DefaultLegacyKind.
        var entry = new FavoriteGame(
            7, UniverseId: 0, "Legacy VIP", ThumbnailUrl: "", IsDefault: false, AddedAt: DateTimeOffset.UnixEpoch,
            LocalName: null,
            PrivateServerCode: "LEGACY",
            PrivateServerCodeKind: null,
            PrivateServerId: Guid.NewGuid());

        var target = MainViewModel.ResolveLaunchTarget(entry, overrideTarget: null);

        var ps = Assert.IsType<LaunchTarget.PrivateServer>(target);
        Assert.Equal(PrivateServerCodeKind.LinkCode, ps.Kind);
    }

    // === Plain game → LaunchTarget.Place ===

    [Fact]
    public void PlainGame_MapsToPlaceTarget()
    {
        var target = MainViewModel.ResolveLaunchTarget(Game(920587237), overrideTarget: null);

        Assert.Equal(new LaunchTarget.Place(920587237), target);
    }

    // === Null / sentinel → DefaultGame ===

    [Fact]
    public void NullSelection_MapsToDefaultGame()
    {
        var target = MainViewModel.ResolveLaunchTarget(selected: null, overrideTarget: null);

        Assert.IsType<LaunchTarget.DefaultGame>(target);
    }

    [Fact]
    public void JoinByLinkSentinel_MapsToDefaultGame()
    {
        // The sentinel is PlaceId == 0 — it must NOT produce a Place(0) or a PrivateServer.
        var target = MainViewModel.ResolveLaunchTarget(MainViewModel.JoinByLinkSentinel, overrideTarget: null);

        Assert.IsType<LaunchTarget.DefaultGame>(target);
    }

    // === FavoriteGame projection invariants the dropdown relies on ===

    [Fact]
    public void PlainGame_IsNotFlaggedAsPrivateServer()
    {
        Assert.False(Game(1).IsPrivateServer);
    }

    [Fact]
    public void Sentinel_IsNotFlaggedAsPrivateServer()
    {
        Assert.False(MainViewModel.JoinByLinkSentinel.IsPrivateServer);
    }

    [Fact]
    public void PrivateServerEntry_IsFlaggedAndCarriesSuffix()
    {
        var entry = PsEntry(1, "X", PrivateServerCodeKind.LinkCode, name: "Clan HQ");

        Assert.True(entry.IsPrivateServer);
        Assert.Equal("Clan HQ (private server)", entry.DropdownLabel);
    }

    [Fact]
    public void PlainGame_DropdownLabel_IsJustTheRenderName()
    {
        Assert.Equal("Adopt Me", Game(1, "Adopt Me").DropdownLabel);
    }

    [Fact]
    public void PrivateServerEntry_DropdownLabel_UsesLocalNameOverride()
    {
        var entry = new FavoriteGame(
            1, UniverseId: 0, "Roblox-side name", ThumbnailUrl: "", IsDefault: false,
            AddedAt: DateTimeOffset.UnixEpoch,
            LocalName: "My Nickname",
            PrivateServerCode: "X",
            PrivateServerCodeKind: PrivateServerCodeKind.LinkCode,
            PrivateServerId: Guid.NewGuid());

        Assert.Equal("My Nickname (private server)", entry.DropdownLabel);
    }
}
