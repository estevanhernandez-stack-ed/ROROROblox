using ROROROblox.App.SquadLaunch;
using ROROROblox.Core;
using Xunit;

namespace ROROROblox.Tests;

public class SquadLaunchOrderingTests
{
    private static SavedPrivateServer Server(string name, bool isDefault = false,
        DateTimeOffset? added = null, DateTimeOffset? launched = null) => new(
        Id: Guid.NewGuid(), PlaceId: 1, Code: name, CodeKind: PrivateServerCodeKind.LinkCode,
        Name: name, PlaceName: "P", ThumbnailUrl: "",
        AddedAt: added ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        LastLaunchedAt: launched, IsDefault: isDefault);

    [Fact]
    public void Order_DefaultFirst_ThenRecency()
    {
        var older = Server("older", launched: new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        var newer = Server("newer", launched: new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero));
        var def   = Server("default", isDefault: true, launched: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var ordered = SquadLaunchOrdering.Order([older, newer, def]);

        Assert.Equal(new[] { "default", "newer", "older" }, ordered.Select(s => s.Name).ToArray());
    }

    [Fact]
    public void Order_NoDefault_PureRecency_LaunchedBeatsAdded()
    {
        var addedOnly = Server("addedOnly", added: new DateTimeOffset(2026, 1, 4, 0, 0, 0, TimeSpan.Zero));
        var launched  = Server("launched",  added: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                               launched: new DateTimeOffset(2026, 1, 6, 0, 0, 0, TimeSpan.Zero));

        var ordered = SquadLaunchOrdering.Order([addedOnly, launched]);

        Assert.Equal(new[] { "launched", "addedOnly" }, ordered.Select(s => s.Name).ToArray());
    }

    [Fact]
    public void Order_EmptyList_Empty()
    {
        Assert.Empty(SquadLaunchOrdering.Order([]));
    }
}
