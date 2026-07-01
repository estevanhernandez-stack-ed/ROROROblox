using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.CookieCapture;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// The 2026-06-12 review found the most-recently-used WebView2 capture profile — a fully
/// logged-in Roblox session with .ROBLOSECURITY in Chromium's cookie store — persisted on
/// disk indefinitely: the only sweep ran on the NEXT capture and excluded the dir just used,
/// so a single-account user kept a live session under webview2-data\ forever, and removing
/// the account did not revoke it. Contract pinned here: the capture's own user-data dir is
/// swept as soon as the capture window completes — success, failure, or seam throw.
/// </summary>
public sealed class CookieCaptureSweepTests : IDisposable
{
    private readonly string _root;

    public CookieCaptureSweepTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"ROROROblox-wv2-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private CookieCapture NewCapture(Func<string, Task<CookieCaptureResult>> runCapture, out WebView2UserDataDirectory userDataDir)
    {
        userDataDir = new WebView2UserDataDirectory(_root, NullLogger<WebView2UserDataDirectory>.Instance);
        return new CookieCapture(
            new ThrowingRobloxApi(),
            userDataDir,
            NullLoggerFactory.Instance,
            runCapture);
    }

    [Fact]
    public async Task CaptureCore_Success_WipesTheCaptureDirAfterTheWindowCloses()
    {
        string? dirHandedToWindow = null;
        var capture = NewCapture(dir =>
        {
            dirHandedToWindow = dir;
            Assert.True(Directory.Exists(dir), "Capture dir must exist while the window runs.");
            return Task.FromResult<CookieCaptureResult>(new CookieCaptureResult.Success("cookie", 1, "user"));
        }, out _);

        var result = await capture.CaptureCoreAsync();

        Assert.IsType<CookieCaptureResult.Success>(result);
        Assert.NotNull(dirHandedToWindow);
        Assert.False(Directory.Exists(dirHandedToWindow),
            "The just-used capture profile is a live logged-in Roblox session — it must not outlive the capture.");
    }

    [Fact]
    public async Task CaptureCore_WindowThrows_StillWipesTheCaptureDir()
    {
        string? dirHandedToWindow = null;
        var capture = NewCapture(dir =>
        {
            dirHandedToWindow = dir;
            throw new InvalidOperationException("WebView2 runtime missing");
        }, out _);

        var result = await capture.CaptureCoreAsync();

        Assert.IsType<CookieCaptureResult.Failed>(result);
        Assert.False(Directory.Exists(dirHandedToWindow!),
            "A failed capture must not leave its profile behind either.");
    }

    [Fact]
    public async Task CaptureCore_SweepsCrashOrphanedSiblingsToo()
    {
        // A prior crash left an orphaned profile. The post-capture sweep runs with no
        // exclusion, so it clears the orphan along with the dir just used.
        Directory.CreateDirectory(Path.Combine(_root, "orphan-from-a-crash"));

        var capture = NewCapture(
            _ => Task.FromResult<CookieCaptureResult>(new CookieCaptureResult.Cancelled()),
            out _);

        await capture.CaptureCoreAsync();

        Assert.False(Directory.Exists(Path.Combine(_root, "orphan-from-a-crash")));
    }

    /// <summary>Ctor filler only — the capture seam means no API call is ever made.</summary>
    private sealed class ThrowingRobloxApi : IRobloxApi
    {
        public Task<AuthTicket> GetAuthTicketAsync(string cookie) => throw new NotImplementedException();
        public Task<UserProfile> GetUserProfileAsync(string cookie) => throw new NotImplementedException();
        public Task<string> GetAvatarHeadshotUrlAsync(long userId) => throw new NotImplementedException();
        public Task<GameMetadata?> GetGameMetadataByPlaceIdAsync(long placeId) => throw new NotImplementedException();
        public Task<IReadOnlyList<GameSearchResult>> SearchGamesAsync(string query) => throw new NotImplementedException();
        public Task<IReadOnlyList<Friend>> GetFriendsAsync(string cookie, long userId) => throw new NotImplementedException();
        public Task<IReadOnlyList<UserPresence>> GetPresenceAsync(string cookie, IEnumerable<long> userIds) => throw new NotImplementedException();
        public Task<ShareLinkResolution?> ResolveShareLinkAsync(string cookie, string code, string linkType) => throw new NotImplementedException();
    }
}
