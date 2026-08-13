using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Coverage of the F-104 signals: the version the <c>roblox-player</c> handler actually points at,
/// and the install-churn count.
///
/// <para><b>Why these exist.</b> <see cref="RobloxCompatChecker.GetInstalledRobloxVersion"/> answers
/// "what is the newest thing on disk." The pre-warm gate needs "what is about to run," and those are
/// different questions — a launch runs whatever the handler is pinned to. Measured on a live box
/// 2026-08-12: handler pinned to <c>version-082eb75e16714844</c> at <c>0,733,448</c> while
/// <c>version-ddf602d9cfe44005</c> at <c>0,734,0</c> sat newer on disk. The gate read 0,734,0,
/// concluded "no update pending," and released a batch that then self-updated client by client.</para>
///
/// <para><b>Why the folder timestamps cannot be trusted to answer it either.</b> Both orderings were
/// checked against the same box and both are wrong:
/// <list type="bullet">
/// <item><c>LastWriteTimeUtc</c> moves when a client <i>runs</i>, not when one installs. Three
/// folders all showed write times inside a three-minute span that was a launch batch, not an
/// install batch.</item>
/// <item><c>CreationTimeUtc</c> does not order by version either — <c>version-7d4de67b</c> is
/// <c>0,733,603</c> created 08-09, and <c>version-082eb75e</c> is the LOWER <c>0,733,448</c>
/// created 08-10.</item>
/// </list>
/// So the handler pin is not merely the better signal, it is the only one that answers the
/// question. Install churn is a separate, weaker signal and is counted on creation time, which is
/// the right clock for "was this installed recently" even though it is the wrong clock for "which
/// is newest."</para>
/// </summary>
public class HandlerVersionTests
{
    // === ExtractVersionFolder: pure parse of the registry command string ===

    [Fact]
    public void ExtractVersionFolder_ReadsTheRealHandlerShape()
    {
        // The shape of HKCU\SOFTWARE\Classes\roblox-player\shell\open\command on a live box:
        // wrapping quotes, backslashes, a trailing %1. All three are what the parser has to
        // survive, and all three are preserved here.
        //
        // The real value starts with a user-profile prefix, which is dropped on purpose — the
        // parser only looks for a "version-" path segment, so the prefix contributes nothing to
        // the test, and a hardcoded user-profile path in committed code is pattern kk (breaks CI
        // on every machine that is not the author's).
        const string command =
            "\"C:\\Roblox\\Versions\\version-ddf602d9cfe44005\\RobloxPlayerBeta.exe\" %1";

        Assert.Equal("version-ddf602d9cfe44005", RobloxCompatChecker.ExtractVersionFolder(command));
    }

    [Fact]
    public void ExtractVersionFolder_UnquotedAndForwardSlashes()
    {
        Assert.Equal(
            "version-abc123",
            RobloxCompatChecker.ExtractVersionFolder("C:/Roblox/Versions/version-abc123/RobloxPlayerBeta.exe %1"));
    }

    [Fact]
    public void ExtractVersionFolder_NullWhenAStrapOwnsTheHandler()
    {
        // Bloxstrap/Fishstrap point at their own binary, which carries no version-* segment. The
        // caller falls back; it must not invent a folder name out of this.
        Assert.Null(RobloxCompatChecker.ExtractVersionFolder(
            "\"C:\\Bloxstrap\\Bloxstrap.exe\" -player %1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    public void ExtractVersionFolder_NullOnAbsentOrUnparseable(string? command)
    {
        Assert.Null(RobloxCompatChecker.ExtractVersionFolder(command));
    }

    [Fact]
    public void ExtractVersionFolder_DoesNotMatchAMerelySimilarSegment()
    {
        // "versions" and "version-" are different tokens; a path component has to actually start
        // with "version-" to count.
        Assert.Null(RobloxCompatChecker.ExtractVersionFolder(
            "C:\\Roblox\\Versions\\notaversion\\RobloxPlayerBeta.exe"));
    }

    // === CountRecentVersionInstalls: creation-time churn, no network ===

    [Fact]
    public void CountRecentInstalls_CountsOnlyFoldersCreatedInsideTheWindow()
    {
        using var dir = new TempVersionsDir();
        var now = new DateTime(2026, 8, 12, 20, 0, 0, DateTimeKind.Utc);

        dir.AddVersion("version-aaa", createdUtc: now.AddMinutes(-2));
        dir.AddVersion("version-bbb", createdUtc: now.AddMinutes(-8));
        dir.AddVersion("version-ccc", createdUtc: now.AddDays(-3));

        Assert.Equal(
            2,
            RobloxCompatChecker.CountRecentVersionInstalls(dir.Path, TimeSpan.FromMinutes(10), now));
    }

    [Fact]
    public void CountRecentInstalls_IgnoresLastWriteTime()
    {
        // The whole point. A folder written seconds ago but installed days ago is a client that
        // RAN, not an update that landed. Counting it would make every multilaunch look like churn.
        using var dir = new TempVersionsDir();
        var now = new DateTime(2026, 8, 12, 20, 0, 0, DateTimeKind.Utc);

        dir.AddVersion("version-old", createdUtc: now.AddDays(-3), lastWriteUtc: now.AddSeconds(-30));

        Assert.Equal(
            0,
            RobloxCompatChecker.CountRecentVersionInstalls(dir.Path, TimeSpan.FromMinutes(10), now));
    }

    [Fact]
    public void CountRecentInstalls_IgnoresNonVersionFolders()
    {
        using var dir = new TempVersionsDir();
        var now = new DateTime(2026, 8, 12, 20, 0, 0, DateTimeKind.Utc);

        dir.AddVersion("version-aaa", createdUtc: now.AddMinutes(-1));
        dir.AddVersion("downloads", createdUtc: now.AddMinutes(-1));

        Assert.Equal(
            1,
            RobloxCompatChecker.CountRecentVersionInstalls(dir.Path, TimeSpan.FromMinutes(10), now));
    }

    [Fact]
    public void CountRecentInstalls_ZeroWhenVersionsDirIsMissing()
    {
        // Degrade-safe like every other read in this family: no Roblox install is not an error,
        // and it is certainly not churn.
        var missing = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rororo-no-such-dir-" + Guid.NewGuid());

        Assert.Equal(
            0,
            RobloxCompatChecker.CountRecentVersionInstalls(missing, TimeSpan.FromMinutes(10), DateTime.UtcNow));
    }

    /// <summary>
    /// A throwaway Versions directory whose folder timestamps are set explicitly, so the tests
    /// assert on the clock the production code reads rather than on whatever the filesystem did.
    /// </summary>
    private sealed class TempVersionsDir : IDisposable
    {
        public string Path { get; }

        public TempVersionsDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rororo-versions-" + Guid.NewGuid());
            Directory.CreateDirectory(Path);
        }

        public void AddVersion(string name, DateTime createdUtc, DateTime? lastWriteUtc = null)
        {
            var full = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(full);
            Directory.SetCreationTimeUtc(full, createdUtc);
            Directory.SetLastWriteTimeUtc(full, lastWriteUtc ?? createdUtc);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* temp dir; best effort */ }
        }
    }
}
