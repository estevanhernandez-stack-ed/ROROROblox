using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// F-103. Roblox restarts itself, RoRoRo never launched the replacement, and the replacement's
/// window stays untitled forever — counted as running, owned by nobody, unreachable by the row's
/// Stop. These pin the one decision that turns detection into a fix: which orphan belongs to which
/// account, and — more importantly — when to refuse to say.
/// </summary>
public class ClientSuccessionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid AccountA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AccountB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void TheOrdinaryCase_OneClientLeaves_ItsReplacementIsAdopted()
    {
        // Exactly the shipped defect: Roblox updates, the tracked pid dies, a bare one appears.
        var exits = new[] { new ClientSuccession.Exit(AccountA, 100, T0) };
        var orphans = new[] { new ClientSuccession.Orphan(200, T0.AddSeconds(6)) };

        var result = ClientSuccession.Attribute(exits, orphans, T0.AddSeconds(8));

        Assert.Single(result);
        Assert.Equal((200, AccountA), result[0]);
    }

    [Fact]
    public void TwoOfEachIsAGuess_AndItDeclines()
    {
        // THE RULE THAT MATTERS. A wrong attribution puts one alt's window under another account's
        // name, and a label gets believed in a way a bare window does not. Refusing is the correct
        // answer, not a gap.
        var exits = new[]
        {
            new ClientSuccession.Exit(AccountA, 100, T0),
            new ClientSuccession.Exit(AccountB, 101, T0.AddSeconds(1)),
        };
        var orphans = new[]
        {
            new ClientSuccession.Orphan(200, T0.AddSeconds(5)),
            new ClientSuccession.Orphan(201, T0.AddSeconds(6)),
        };

        Assert.Empty(ClientSuccession.Attribute(exits, orphans, T0.AddSeconds(8)));
    }

    [Fact]
    public void AnOrphanThatPredatesTheExitIsNotItsReplacement()
    {
        // A client the user opened before anything died. Adopting it would claim a window RoRoRo
        // never had anything to do with.
        var exits = new[] { new ClientSuccession.Exit(AccountA, 100, T0.AddSeconds(30)) };
        var orphans = new[] { new ClientSuccession.Orphan(200, T0) };

        Assert.Empty(ClientSuccession.Attribute(exits, orphans, T0.AddSeconds(35)));
    }

    [Fact]
    public void AStaleExitStopsBeingAPlausiblePredecessor()
    {
        // Otherwise a client opened much later gets claimed by whoever last exited.
        var exits = new[] { new ClientSuccession.Exit(AccountA, 100, T0) };
        var orphans = new[] { new ClientSuccession.Orphan(200, T0 + ClientSuccession.Window + TimeSpan.FromSeconds(10)) };

        Assert.Empty(ClientSuccession.Attribute(exits, orphans, T0 + ClientSuccession.Window + TimeSpan.FromSeconds(12)));
    }

    [Fact]
    public void AnUpdateThatTakesAWhileIsStillASuccession()
    {
        // Roblox writes files before restarting. A window measured in seconds would miss the real
        // case this exists for.
        var exits = new[] { new ClientSuccession.Exit(AccountA, 100, T0) };
        var orphans = new[] { new ClientSuccession.Orphan(200, T0.AddSeconds(60)) };

        Assert.Single(ClientSuccession.Attribute(exits, orphans, T0.AddSeconds(62)));
    }

    [Fact]
    public void NothingExited_NothingIsAdopted()
    {
        // A client launched outside RoRoRo, with nothing having died. Not ours to claim.
        var orphans = new[] { new ClientSuccession.Orphan(200, T0) };

        Assert.Empty(ClientSuccession.Attribute([], orphans, T0.AddSeconds(5)));
    }

    [Fact]
    public void TwoOrphansForOneExit_NeitherIsSafe()
    {
        // One client died and two appeared. One of them is somebody else's; we cannot tell which,
        // so neither gets a name.
        var exits = new[] { new ClientSuccession.Exit(AccountA, 100, T0) };
        var orphans = new[]
        {
            new ClientSuccession.Orphan(200, T0.AddSeconds(4)),
            new ClientSuccession.Orphan(201, T0.AddSeconds(5)),
        };

        Assert.Empty(ClientSuccession.Attribute(exits, orphans, T0.AddSeconds(7)));
    }

    [Fact]
    public void TheLive2026_10_26Case_ThreeExitsOneOrphan_AttributesNothing()
    {
        // Real timeline (#241). Orphan 47024 is a --launch-to-tray child of an untracked closing
        // game; two launches were handed off to tray clients seconds apart. The old rule saw
        // exactly one candidate (39596's exit, 2 s grace) and adopted 47024 for ELeonDog: wrong.
        var eLeonDog = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000e1e0");
        var cElCPapa = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000c1c0");
        var day = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        var exits = new[]
        {
            new ClientSuccession.Exit(eLeonDog, 39596, day.Add(new TimeSpan(0, 0, 42, 23, 23))),
            new ClientSuccession.Exit(cElCPapa, 45652, day.Add(new TimeSpan(0, 0, 42, 56, 857))),
            new ClientSuccession.Exit(eLeonDog, 32012, day.Add(new TimeSpan(0, 0, 42, 57, 570))),
        };
        var orphans = new[] { new ClientSuccession.Orphan(47024, day.Add(new TimeSpan(0, 0, 42, 21, 235))) };
        var now = day.Add(new TimeSpan(0, 0, 43, 1, 706));

        Assert.Empty(ClientSuccession.Attribute(exits, orphans, now));
        Assert.True(ClientSuccession.IsAmbiguous(exits, orphans, now));
    }

    [Fact]
    public void NullsAreNotACrashOnAPollingPath()
    {
        Assert.Empty(ClientSuccession.Attribute(null!, null!, T0));
    }
}
