using System.IO;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// The startup path's pieces: <c>AppDataRoot.Configure</c> (decide, create the folder, set the
/// process-wide override once). Runs in a collection that is alone in the process, and resets the
/// process-wide state in <see cref="Dispose"/> so nothing leaks to what runs next.
/// </summary>
[Collection(AppDataRootProcessWideCollection.Name)]
public class AppDataRootProcessWideTests : IDisposable
{
    private readonly List<string> _temps = [];

    public AppDataRootProcessWideTests() => AppDataRoot.ResetProcessWideForTest();

    public void Dispose()
    {
        AppDataRoot.ResetProcessWideForTest();
        foreach (var t in _temps)
        {
            try { Directory.Delete(t, recursive: true); } catch { /* temp dir; best effort */ }
        }
    }

    private string Temp()
    {
        var p = Path.Combine(Path.GetTempPath(), "rororo-processwide-test-" + Guid.NewGuid().ToString("N"));
        _temps.Add(p);
        return p;
    }

    private static string DefaultFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ROROROblox");

    [Fact]
    public void Configure_WithAMissingFolder_CreatesIt_AndPointsTheHelperThere()
    {
        var folder = Temp();
        Assert.False(Directory.Exists(folder));

        var d = AppDataRoot.Configure(isPackaged: false, isVelopackInstalled: false, folder);

        Assert.Equal(DataRootDecisionReason.Used, d.Reason);
        Assert.True(Directory.Exists(folder), "The startup path must create the override folder.");
        Assert.Equal(folder, AppDataRoot.Folder);
        Assert.Equal(Path.Combine(folder, "settings.json"), AppSettings.DefaultPath());
    }

    [Fact]
    public void Configure_WithAnExistingFolder_LeavesItsContentsAlone()
    {
        var folder = Temp();
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "keep.txt"), "x");

        AppDataRoot.Configure(false, false, folder);

        Assert.Equal("x", File.ReadAllText(Path.Combine(folder, "keep.txt")));
    }

    [Fact]
    public void Configure_KeepsTheDecisionForALaterLogLine()
    {
        var folder = Temp();

        var d = AppDataRoot.Configure(false, false, folder);

        Assert.Equal(d, AppDataRoot.Decision);
        Assert.Equal(DataRootDecisionReason.Used, AppDataRoot.Decision.Reason);
        Assert.Equal(folder, AppDataRoot.Decision.Folder);
    }

    /// <summary>Stands for "an absolute path under a fresh temp folder" in the theory below (InlineData cannot compute one).</summary>
    private const string Abs = "<absolute-temp>";

    [Theory]
    [InlineData(true, false, Abs, DataRootDecisionReason.NotADevBuild)]
    [InlineData(false, true, Abs, DataRootDecisionReason.NotADevBuild)]
    [InlineData(false, false, "relative", DataRootDecisionReason.NotAnAbsolutePath)]
    [InlineData(false, false, null, DataRootDecisionReason.Unset)]
    public void Configure_WhenNotUsed_CreatesNothing_AndLeavesTheDefault(
        bool packaged, bool installed, string? raw, DataRootDecisionReason reason)
    {
        var folder = Temp();
        if (raw == Abs)
        {
            raw = Path.Combine(folder, "x");
        }

        var d = AppDataRoot.Configure(packaged, installed, raw);

        Assert.Equal(reason, d.Reason);
        Assert.Equal(reason, AppDataRoot.Decision.Reason);
        Assert.Equal(DefaultFolder(), AppDataRoot.Folder);
        Assert.False(Directory.Exists(folder), "A decision that does not use the folder must not create it.");
    }

    [Fact]
    public void Configure_SecondTimeWithADifferentFolder_Throws_AndKeepsTheFirst()
    {
        var first = Temp();
        var second = Temp();
        AppDataRoot.Configure(false, false, first);

        Assert.Throws<InvalidOperationException>(() => AppDataRoot.Configure(false, false, second));

        Assert.Equal(first, AppDataRoot.Folder);
        Assert.False(Directory.Exists(second), "A refused configuration must not create its folder.");
    }

    [Fact]
    public void Configure_SecondTimeWithTheSameFolder_IsAllowed()
    {
        var folder = Temp();
        AppDataRoot.Configure(false, false, folder);

        var again = AppDataRoot.Configure(false, false, folder);

        Assert.Equal(DataRootDecisionReason.Used, again.Reason);
        Assert.Equal(folder, AppDataRoot.Folder);
    }

    [Fact]
    public void Configure_SameFolderSpelledDifferently_IsTheSameFolder()
    {
        var folder = Temp();
        AppDataRoot.Configure(false, false, folder);

        // Trailing separator and case: the same folder on Windows.
        var again = AppDataRoot.Configure(false, false, folder.ToUpperInvariant() + Path.DirectorySeparatorChar);

        Assert.Equal(DataRootDecisionReason.Used, again.Reason);
    }

    [Fact]
    public void Configure_ToTheDefaultAfterAFolder_Throws()
    {
        AppDataRoot.Configure(false, false, Temp());

        Assert.Throws<InvalidOperationException>(() => AppDataRoot.Configure(false, false, null));
    }

    [Fact]
    public void Configure_AFolderAfterTheDefaultWasSettled_Throws()
    {
        // A late caller cannot move the folder under code that already resolved paths with the default.
        AppDataRoot.Configure(false, false, null);

        Assert.Throws<InvalidOperationException>(() => AppDataRoot.Configure(false, false, Temp()));
        Assert.Equal(DefaultFolder(), AppDataRoot.Folder);
    }

    [Fact]
    public void ScopedOverride_TakesPrecedenceOverTheProcessWideOne_AndRevealsItOnDispose()
    {
        var processWide = Temp();
        var scoped = Temp();
        AppDataRoot.Configure(false, false, processWide);

        using (AppDataRoot.OverrideForTest(scoped))
        {
            Assert.Equal(scoped, AppDataRoot.Folder);
        }

        Assert.Equal(processWide, AppDataRoot.Folder);
    }

    [Fact]
    public void Reset_PutsEverythingBack()
    {
        AppDataRoot.Configure(false, false, Temp());

        AppDataRoot.ResetProcessWideForTest();

        Assert.Equal(DefaultFolder(), AppDataRoot.Folder);
        Assert.Equal(DataRootDecisionReason.Unset, AppDataRoot.Decision.Reason);
        // And a fresh configuration is allowed again.
        AppDataRoot.Configure(false, false, Temp());
    }
}
