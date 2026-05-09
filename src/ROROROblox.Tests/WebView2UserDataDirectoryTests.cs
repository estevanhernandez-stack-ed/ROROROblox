using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Regression coverage for the field-reported "Add Account #2 captures Account #1's cookie"
/// bug. Pre-1.3.4 the capture flow shared a single webview2-data\ dir and silently swallowed
/// the IOException when stale msedgewebview2.exe handles pinned files. Per-capture GUID dirs
/// + best-effort sweep is the new shape; these tests pin that contract.
/// </summary>
public sealed class WebView2UserDataDirectoryTests : IDisposable
{
    private readonly string _root;

    public WebView2UserDataDirectoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"ROROROblox-wv2udd-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // Test process may still hold a stream open in the locked-handle test below; OS will clean tmp.
            }
        }
    }

    private WebView2UserDataDirectory NewSut() =>
        new(_root, NullLogger<WebView2UserDataDirectory>.Instance);

    [Fact]
    public void AllocateNew_ReturnsDistinctDirsAcrossCalls()
    {
        var sut = NewSut();

        var first = sut.AllocateNew();
        var second = sut.AllocateNew();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void AllocateNew_CreatesDirectoryOnDisk()
    {
        var sut = NewSut();

        var path = sut.AllocateNew();

        Assert.True(Directory.Exists(path));
        Assert.Equal(_root, Path.GetDirectoryName(path));
    }

    [Fact]
    public void SweepStale_RemovesAllSiblingDirs_WhenNoExclude()
    {
        var sut = NewSut();
        var a = sut.AllocateNew();
        var b = sut.AllocateNew();

        sut.SweepStale();

        Assert.False(Directory.Exists(a));
        Assert.False(Directory.Exists(b));
    }

    [Fact]
    public void SweepStale_PreservesExcludedDir()
    {
        var sut = NewSut();
        var keep = sut.AllocateNew();
        var drop = sut.AllocateNew();

        sut.SweepStale(exclude: keep);

        Assert.True(Directory.Exists(keep));
        Assert.False(Directory.Exists(drop));
    }

    [Fact]
    public void SweepStale_DoesNotThrow_WhenRootMissing()
    {
        var sut = NewSut();

        // Root never created — should be a no-op, not an exception.
        sut.SweepStale();
    }

    [Fact]
    public void SweepStale_DoesNotThrow_WhenSiblingHasLockedFile()
    {
        // The exact field-bug shape: a previous capture's msedgewebview2.exe child still holds
        // a file handle in the sibling dir. Pre-1.3.4 this ate the IOException silently and
        // left WebView2 to reuse the locked dir. Now we allocate a brand-new dir per capture
        // and the sweep just logs the warning instead of taking the next capture down with it.
        var sut = NewSut();
        var locked = sut.AllocateNew();
        var clean = sut.AllocateNew();
        var lockedFile = Path.Combine(locked, "Cookies");

        using var pin = new FileStream(lockedFile, FileMode.Create, FileAccess.Write, FileShare.None);

        sut.SweepStale(exclude: clean);

        // Locked dir survives; clean dir survives because excluded; the next capture is unaffected.
        Assert.True(Directory.Exists(locked));
        Assert.True(Directory.Exists(clean));
    }

    [Fact]
    public void SweepStale_RemovesUnlockedSiblings_EvenWhenAnotherIsLocked()
    {
        // Locked sibling shouldn't poison the rest of the sweep — every sibling is its own
        // try/catch, so leftovers from one bad msedgewebview2 child don't leave the whole
        // root untouched.
        var sut = NewSut();
        var locked = sut.AllocateNew();
        var unlocked = sut.AllocateNew();
        var lockedFile = Path.Combine(locked, "Cookies");

        using var pin = new FileStream(lockedFile, FileMode.Create, FileAccess.Write, FileShare.None);

        sut.SweepStale();

        Assert.True(Directory.Exists(locked));      // pinned, preserved
        Assert.False(Directory.Exists(unlocked));   // swept
    }

    [Fact]
    public void Root_ReflectsConstructorArg()
    {
        var sut = NewSut();
        Assert.Equal(_root, sut.Root);
    }
}
