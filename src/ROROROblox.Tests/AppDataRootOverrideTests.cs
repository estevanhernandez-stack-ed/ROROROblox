using System.IO;
using System.Threading;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// The scoped override seam (<c>AppDataRoot.OverrideForTest</c>): an <c>AsyncLocal</c>, so a test can
/// run code "under an override" without touching the process-wide value that other test classes,
/// running in parallel, would see. These tests never set the process-wide override; those live in
/// <see cref="AppDataRootProcessWideTests"/>, in a collection that runs alone.
/// </summary>
public class AppDataRootOverrideTests
{
    private static string TempRoot() =>
        Path.Combine(Path.GetTempPath(), "rororo-override-test-" + Guid.NewGuid().ToString("N"));

    private static string DefaultFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ROROROblox");

    public static TheoryData<string> SiteNames()
    {
        var data = new TheoryData<string>();
        foreach (var (site, _, _) in AppDataRootPathPinTests.SiteTable)
        {
            data.Add(site);
        }
        return data;
    }

    [Fact]
    public void SiteTable_HasAllEighteenSites()
    {
        // Vacuity floor: the shared table is what both the default pins and the override check read.
        // An emptied table would pass every row below by having none.
        Assert.Equal(18, AppDataRootPathPinTests.SiteTable.Count);
        Assert.Equal(AppDataRootPathPinTests.SiteTable.Count,
            AppDataRootPathPinTests.SiteTable.Select(r => r.Site).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(SiteNames))]
    public void EverySite_ResolvesUnderTheOverride_AndNotUnderLocalAppData(string site)
    {
        var root = TempRoot();
        var row = AppDataRootPathPinTests.SiteTable.Single(r => r.Site == site);

        string actual;
        using (AppDataRoot.OverrideForTest(root))
        {
            actual = row.Resolve();
        }

        var expected = Path.Combine([root, .. row.Parts]);
        Assert.True(string.Equals(expected, actual, StringComparison.Ordinal),
            $"{site}: expected '{expected}' under the override but resolved '{actual}'.");
        Assert.False(
            actual.StartsWith(DefaultFolder(), StringComparison.OrdinalIgnoreCase),
            $"{site}: resolved under %LOCALAPPDATA%\\ROROROblox ('{actual}') while an override was in force.");
    }

    [Fact]
    public void Folder_AndCombine_FollowTheOverride()
    {
        var root = TempRoot();
        using (AppDataRoot.OverrideForTest(root))
        {
            Assert.Equal(root, AppDataRoot.Folder);
            Assert.Equal(Path.Combine(root, "a.json"), AppDataRoot.Combine("a.json"));
            Assert.Equal(root, AppDataRoot.Combine());
        }
    }

    [Fact]
    public void OutsideTheScope_TheDefaultIsBack()
    {
        var before = AppDataRoot.Folder;
        Assert.Equal(DefaultFolder(), before);

        var root = TempRoot();
        using (AppDataRoot.OverrideForTest(root))
        {
            Assert.Equal(root, AppDataRoot.Folder);
        }

        Assert.Equal(before, AppDataRoot.Folder);
        Assert.Equal(DefaultFolder(), AppDataRoot.Combine());
    }

    [Fact]
    public void NestedScopes_RestoreTheOuterOne()
    {
        var outer = TempRoot();
        var inner = TempRoot();
        using (AppDataRoot.OverrideForTest(outer))
        {
            using (AppDataRoot.OverrideForTest(inner))
            {
                Assert.Equal(inner, AppDataRoot.Folder);
            }
            Assert.Equal(outer, AppDataRoot.Folder);
        }
        Assert.Equal(DefaultFolder(), AppDataRoot.Folder);
    }

    [Fact]
    public void DisposingTwice_IsHarmless()
    {
        var scope = AppDataRoot.OverrideForTest(TempRoot());
        scope.Dispose();
        scope.Dispose();

        Assert.Equal(DefaultFolder(), AppDataRoot.Folder);
    }

    [Fact]
    public async Task Override_FlowsAcrossAwait()
    {
        var root = TempRoot();
        using (AppDataRoot.OverrideForTest(root))
        {
            await Task.Delay(5);
            await Task.Yield();
            Assert.Equal(root, AppDataRoot.Folder);
            var seen = await Task.Run(() => AppDataRoot.Folder);
            Assert.Equal(root, seen);
        }
        Assert.Equal(DefaultFolder(), AppDataRoot.Folder);
    }

    [Fact]
    public async Task TwoOverridesOnTwoConcurrentTasks_DoNotSeeEachOther()
    {
        var rootA = TempRoot();
        var rootB = TempRoot();
        // Each task enters its override, then waits until the OTHER has entered theirs, so the
        // two scopes are provably open at the same moment before either reads.
        using var barrier = new Barrier(2);
        var bound = TestWaits.Liveness;

        async Task<(string Folder, string Combined, string AfterOther)> Run(string root)
        {
            using (AppDataRoot.OverrideForTest(root))
            {
                Assert.True(barrier.SignalAndWait(bound), "The other task never entered its override.");
                await Task.Yield();
                var folder = AppDataRoot.Folder;
                var combined = AppDataRoot.Combine("x.json");
                Assert.True(barrier.SignalAndWait(bound), "The other task never reached its read.");
                await Task.Delay(5);
                return (folder, combined, AppDataRoot.Folder);
            }
        }

        var a = Task.Run(() => Run(rootA));
        var b = Task.Run(() => Run(rootB));
        var both = await Task.WhenAll(a, b).WaitAsync(bound);

        Assert.Equal(rootA, both[0].Folder);
        Assert.Equal(Path.Combine(rootA, "x.json"), both[0].Combined);
        Assert.Equal(rootA, both[0].AfterOther);
        Assert.Equal(rootB, both[1].Folder);
        Assert.Equal(Path.Combine(rootB, "x.json"), both[1].Combined);
        Assert.Equal(rootB, both[1].AfterOther);

        // And the test's own context never saw either.
        Assert.Equal(DefaultFolder(), AppDataRoot.Folder);
    }
}
