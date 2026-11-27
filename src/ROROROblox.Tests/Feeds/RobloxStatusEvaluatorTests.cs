using System.Text.Json;
using System.Text.Json.Nodes;
using ROROROblox.Core.Feeds;

namespace ROROROblox.Tests.Feeds;

/// <summary>
/// The evaluator over four recorded answers. Roblox's own answer proves the five watched pairs exist;
/// the three other-page answers are real incidents, each passed with a watch table of that page's own
/// ids, so no payload is edited to fit (spec 6.4).
/// </summary>
public class RobloxStatusEvaluatorTests
{
    internal static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "RobloxStatus", name);

    internal static byte[] FixtureBytes(string name) => File.ReadAllBytes(FixturePath(name));

    internal static RobloxStatusSnapshot Load(string name) =>
        RobloxStatusApi.Parse(FixtureBytes(name)) ?? throw new InvalidOperationException($"{name} did not parse.");

    private static RobloxStatusSnapshot Snapshot(params (string Component, string Container, int Code)[] cells) =>
        new(cells.Select(c => new StatusCell(c.Component, c.Container, c.Code)).ToList());

    // ---- Roblox's own answer ----

    [Fact]
    public void RobloxOperational_FindsAllFiveWatchedPairs_AndReadsNone()
    {
        var snapshot = Load("status-roblox-operational-2026-11-26.json");
        var missing = new List<RobloxStatusRow>();

        var reading = RobloxStatusEvaluator.Evaluate(snapshot, RobloxStatusWatch.Roblox, missing.Add);

        Assert.Empty(missing);
        Assert.Equal(5, RobloxStatusWatch.Roblox.Count);
        foreach (var pair in RobloxStatusWatch.Roblox)
        {
            Assert.Contains(snapshot.Cells, c => c.ComponentId == pair.ComponentId && c.ContainerId == pair.ContainerId && c.Code == 100);
        }

        Assert.Same(RobloxStatusReading.None, reading);
    }

    // ---- 2022-03-28: one row id, two groups, two codes ----

    private const string ControlPanel = "5982d729f6278a3b05000618";
    private const string CloudPlatform = "5982dc5bd537a2a7050005d4";
    private const string DnsHosting = "5982e291383934bd050006b0";
    private const string Dedicated = "5982de1e383934bd050006ac";
    private const string Network = "59b69ac3331fbea405000743";
    private const string CoreNetwork = "59b69a93d4c04f860500078d";

    private static readonly WatchedCell[] Page2022March =
    [
        new(RobloxStatusRow.Website, CloudPlatform, ControlPanel),
        new(RobloxStatusRow.Games, DnsHosting, ControlPanel),
        new(RobloxStatusRow.Avatar, Network, CoreNetwork),
        new(RobloxStatusRow.GameJoin, Dedicated, ControlPanel),
    ];

    [Fact]
    public void March2022_OneRowId_ReadsFourHundredUnderOneGroupAndThreeHundredUnderAnother()
    {
        var snapshot = Load("status-otherpage-incident-2022-03-28.json");

        Assert.Equal(400, snapshot.Cells.Single(c => c.ComponentId == CloudPlatform && c.ContainerId == ControlPanel).Code);
        Assert.Equal(300, snapshot.Cells.Single(c => c.ComponentId == DnsHosting && c.ContainerId == ControlPanel).Code);

        var reading = RobloxStatusEvaluator.Evaluate(snapshot, Page2022March);

        Assert.Equal(new RobloxStatusReading(RobloxStatusRow.Website, RobloxStatusLevel.PartialOutage, 1), reading);
    }

    [Fact]
    public void March2022_EachGroupReadsItsOwnCode()
    {
        var snapshot = Load("status-otherpage-incident-2022-03-28.json");

        var underDns = RobloxStatusEvaluator.Evaluate(snapshot, [new(RobloxStatusRow.Games, DnsHosting, ControlPanel)]);
        var underCloud = RobloxStatusEvaluator.Evaluate(snapshot, [new(RobloxStatusRow.Games, CloudPlatform, ControlPanel)]);
        var underDedicated = RobloxStatusEvaluator.Evaluate(snapshot, [new(RobloxStatusRow.Games, Dedicated, ControlPanel)]);

        Assert.Equal(RobloxStatusLevel.Degraded, underDns.Level);
        Assert.Equal(RobloxStatusLevel.PartialOutage, underCloud.Level);
        Assert.Same(RobloxStatusReading.None, underDedicated);
    }

    [Fact]
    public void March2022_TwoOpenIncidentsAndElevenScheduledMaintenancesChangeNothing()
    {
        var raw = JsonNode.Parse(FixtureBytes("status-otherpage-incident-2022-03-28.json"))!.AsObject();
        var result = raw["result"]!.AsObject();
        Assert.Equal(2, result["incidents"]!.AsArray().Count);
        Assert.Equal(11, result["maintenance"]!["upcoming"]!.AsArray().Count);

        result.Remove("incidents");
        result.Remove("maintenance");
        var stripped = RobloxStatusApi.Parse(JsonSerializer.SerializeToUtf8Bytes(raw))!;

        var withEverything = RobloxStatusEvaluator.Evaluate(Load("status-otherpage-incident-2022-03-28.json"), Page2022March);
        var withoutThem = RobloxStatusEvaluator.Evaluate(stripped, Page2022March);

        Assert.Equal(withoutThem, withEverything);
    }

    // ---- 2022-11-29: 500 and a maintenance under way ----

    private const string CdnGroup = "5f9c10907f50b304c1e7c8c8";
    private const string CdnRow = "5f9c108618906b04baced440";
    private const string DatacenterGroup = "5982d7b2470c77a205000606";
    private const string Mmts9 = "6384da87dc4010053470e294";

    [Fact]
    public void November2022_FiveHundredOutranksTwoHundred()
    {
        var snapshot = Load("status-otherpage-disruption-maintenance-2022-11-29.json");

        var reading = RobloxStatusEvaluator.Evaluate(snapshot,
        [
            new(RobloxStatusRow.Games, DatacenterGroup, Mmts9),
            new(RobloxStatusRow.Website, CdnGroup, CdnRow),
        ]);

        Assert.Equal(new RobloxStatusReading(RobloxStatusRow.Website, RobloxStatusLevel.Outage, 1), reading);
    }

    [Fact]
    public void November2022_MaintenanceUnderWay_ReadsMaintenance()
    {
        var snapshot = Load("status-otherpage-disruption-maintenance-2022-11-29.json");

        var reading = RobloxStatusEvaluator.Evaluate(snapshot, [new(RobloxStatusRow.Games, DatacenterGroup, Mmts9)]);

        Assert.Equal(new RobloxStatusReading(RobloxStatusRow.Games, RobloxStatusLevel.Maintenance, 0), reading);
    }

    // ---- 2026-11-06: a field the parser has never seen ----

    [Fact]
    public void November2026_UnknownFieldIsIgnored()
    {
        var raw = System.Text.Encoding.UTF8.GetString(FixtureBytes("status-otherpage-degraded-2026-11-06.json"));
        Assert.Contains("current_active", raw);

        var snapshot = Load("status-otherpage-degraded-2026-11-06.json");
        var reading = RobloxStatusEvaluator.Evaluate(snapshot,
        [
            new(RobloxStatusRow.Website, "5862b0588ff9510105000f1b", "694af23bf5a1430583c53847"),
            new(RobloxStatusRow.Games, "5855f56e32dde2fc5b00036b", "694af23bf5a1430583c53847"),
            new(RobloxStatusRow.Avatar, "52cbb6a6e746d36b130000ee", "694af23bf5a1430583c53847"),
            new(RobloxStatusRow.GameJoin, "5855f489e7b36afe5b000041", "5856d4318ff951010500042a"),
        ]);

        Assert.Equal(new RobloxStatusReading(RobloxStatusRow.Website, RobloxStatusLevel.Degraded, 2), reading);
    }

    // ---- rows, pairs, missing ----

    private static readonly WatchedCell WebsitePlayer = RobloxStatusWatch.Roblox[0];
    private static readonly WatchedCell WebsiteUser = RobloxStatusWatch.Roblox[1];

    [Fact]
    public void TwoPairsOfOneRow_CountAsOneRow()
    {
        var snapshot = Snapshot(
            (WebsitePlayer.ComponentId, WebsitePlayer.ContainerId, 300),
            (WebsiteUser.ComponentId, WebsiteUser.ContainerId, 300));

        var reading = RobloxStatusEvaluator.Evaluate(snapshot, [WebsitePlayer, WebsiteUser]);

        Assert.Equal(new RobloxStatusReading(RobloxStatusRow.Website, RobloxStatusLevel.Degraded, 0), reading);
    }

    [Fact]
    public void ARowReadsItsWorstPair()
    {
        var snapshot = Snapshot(
            (WebsitePlayer.ComponentId, WebsitePlayer.ContainerId, 300),
            (WebsiteUser.ComponentId, WebsiteUser.ContainerId, 500));

        var reading = RobloxStatusEvaluator.Evaluate(snapshot, [WebsitePlayer, WebsiteUser]);

        Assert.Equal(RobloxStatusLevel.Outage, reading.Level);
        Assert.Equal(0, reading.OtherRows);
    }

    [Fact]
    public void AMissingPair_IsUnknown_AndCallsOnMissingForItsRow()
    {
        var all = Load("status-roblox-operational-2026-11-26.json").Cells
            .Where(c => !(c.ComponentId == RobloxStatusWatch.Roblox[2].ComponentId && c.ContainerId == RobloxStatusWatch.Roblox[2].ContainerId))
            .ToList();
        var missing = new List<RobloxStatusRow>();

        var reading = RobloxStatusEvaluator.Evaluate(new RobloxStatusSnapshot(all), RobloxStatusWatch.Roblox, missing.Add);

        Assert.Equal([RobloxStatusRow.Games], missing);
        Assert.Same(RobloxStatusReading.None, reading);
    }

    [Fact]
    public void AMissingPair_NeverMakesARowWorse_AndTheRestStillRead()
    {
        var snapshot = Snapshot((WebsiteUser.ComponentId, WebsiteUser.ContainerId, 400));
        var missing = new List<RobloxStatusRow>();

        var reading = RobloxStatusEvaluator.Evaluate(snapshot, [WebsitePlayer, WebsiteUser], missing.Add);

        Assert.Equal([RobloxStatusRow.Website], missing);
        Assert.Equal(new RobloxStatusReading(RobloxStatusRow.Website, RobloxStatusLevel.PartialOutage, 0), reading);
    }

    // ---- levels (spec 6.3) ----

    [Theory]
    [InlineData(100, RobloxStatusLevel.Operational)]
    [InlineData(200, RobloxStatusLevel.Maintenance)]
    [InlineData(300, RobloxStatusLevel.Degraded)]
    [InlineData(400, RobloxStatusLevel.PartialOutage)]
    [InlineData(500, RobloxStatusLevel.Outage)]
    [InlineData(600, RobloxStatusLevel.SecurityEvent)]
    [InlineData(0, RobloxStatusLevel.Unrecognised)]
    [InlineData(700, RobloxStatusLevel.Unrecognised)]
    [InlineData(-1, RobloxStatusLevel.Unrecognised)]
    public void CodesMapToLevels(int code, RobloxStatusLevel expected)
    {
        Assert.Equal(expected, RobloxStatusEvaluator.LevelOf(code));
    }

    [Fact]
    public void Ranking_FollowsTheSpecTable()
    {
        RobloxStatusLevel[] ascending =
        [
            RobloxStatusLevel.Operational, RobloxStatusLevel.Maintenance, RobloxStatusLevel.Unrecognised,
            RobloxStatusLevel.Degraded, RobloxStatusLevel.PartialOutage, RobloxStatusLevel.SecurityEvent,
            RobloxStatusLevel.Outage,
        ];

        Assert.Equal(ascending, ascending.OrderBy(RobloxStatusEvaluator.Rank));
        Assert.Equal(ascending.Length, ascending.Select(RobloxStatusEvaluator.Rank).Distinct().Count());
    }

    [Fact]
    public void AnUnrecognisedCode_ShowsAsUnrecognised_AndOutranksMaintenance()
    {
        var snapshot = Snapshot(
            (WebsitePlayer.ComponentId, WebsitePlayer.ContainerId, 777),
            (RobloxStatusWatch.Roblox[2].ComponentId, RobloxStatusWatch.Roblox[2].ContainerId, 200));

        var reading = RobloxStatusEvaluator.Evaluate(snapshot, RobloxStatusWatch.Roblox);

        Assert.Equal(new RobloxStatusReading(RobloxStatusRow.Website, RobloxStatusLevel.Unrecognised, 1), reading);
    }
}
