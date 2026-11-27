namespace ROROROblox.Tests.Feeds;

/// <summary>A fixture is a recording: it is the probe file, byte for byte, and is never edited.</summary>
public class RobloxStatusFixtureTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.GetFiles(dir, "ROROROblox.slnx").Any())
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.False(dir is null, "Could not locate ROROROblox.slnx.");
        return dir!;
    }

    private static string ProbeDirectory() =>
        Path.Combine(RepoRoot(), "docs", "cycles", "2026-11-26-roblox-feeds", "probes");

    private static string FixtureDirectory() =>
        Path.Combine(RepoRoot(), "src", "ROROROblox.Tests", "Fixtures", "RobloxStatus");

    private static readonly string[] Recorded =
    [
        "status-roblox-operational-2026-11-26.json",
        "status-otherpage-incident-2022-03-28.json",
        "status-otherpage-disruption-maintenance-2022-11-29.json",
        "status-otherpage-degraded-2026-11-06.json",
    ];

    public static IEnumerable<object[]> Names() => Recorded.Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(Names))]
    public void Fixture_IsTheProbeFile_ByteForByte(string name)
    {
        var probe = File.ReadAllBytes(Path.Combine(ProbeDirectory(), name));
        var fixture = File.ReadAllBytes(Path.Combine(FixtureDirectory(), name));

        Assert.Equal(probe, fixture);
    }

    [Fact]
    public void EveryRecordedFixture_IsNamedAbove_AndEveryOtherJsonIsSynthetic()
    {
        var recorded = Recorded.ToHashSet();

        var strays = Directory.GetFiles(FixtureDirectory(), "*.json")
            .Select(Path.GetFileName)
            .Where(n => !recorded.Contains(n!) && !n!.StartsWith("synthetic-", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(strays);
    }

    [Fact]
    public void FixturesAreCopiedToTheTestOutput()
    {
        foreach (var name in Recorded)
        {
            Assert.True(File.Exists(RobloxStatusEvaluatorTests.FixturePath(name)), $"{name} is not in the test output.");
        }
    }
}
