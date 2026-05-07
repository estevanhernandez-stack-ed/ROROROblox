using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

/// <summary>
/// Layer 2 outbound integration: RobloxLauncher hooks ServerShareExtractor + IDiscordPresence
/// after a successful Process.Start. Discord call is fire-and-forget on the thread pool, so
/// tests synchronize via a TaskCompletionSource the fake fills.
/// </summary>
public class RobloxLauncherDiscordHookTests
{
    private const string TestCookie = "FAKE_COOKIE_FOR_TESTS_ONLY";

    [Fact]
    public async Task LaunchAsync_PrivateServerTarget_CallsSetPartyAsync_WithExtractedShareUrl()
    {
        var presence = new RecordingPresence();
        var launcher = NewLauncher(presence, ticket: "TKT");

        var result = await launcher.LaunchAsync(TestCookie,
            new LaunchTarget.PrivateServer(PlaceId: 920587237, Code: "ABCDEF", Kind: PrivateServerCodeKind.AccessCode));

        Assert.IsType<LaunchResult.Started>(result);

        var partyUrl = await presence.WaitForSetPartyAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(partyUrl);
        Assert.Contains("accessCode=ABCDEF", partyUrl);
    }

    [Fact]
    public async Task LaunchAsync_PublicGameTarget_CallsSetPartyAsync_WithPlaceUrl()
    {
        // v1.2.5 expansion (CHECKPOINT 1.7): public games now also produce a shareable URL
        // so the Discord Join button supports clan "come find me" coordination, not just
        // private-server matching. Public-game launches go through SetPartyAsync (not
        // ClearPartyAsync) and the shareUrl carries placeId= without any narrower share
        // signal.
        var presence = new RecordingPresence();
        var launcher = NewLauncher(presence, ticket: "TKT");

        var result = await launcher.LaunchAsync(TestCookie, new LaunchTarget.Place(PlaceId: 920587237));

        Assert.IsType<LaunchResult.Started>(result);

        var partyUrl = await presence.WaitForSetPartyAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(partyUrl);
        Assert.Contains("placeId=920587237", partyUrl);
        Assert.DoesNotContain("accessCode=", partyUrl);
        Assert.DoesNotContain("linkCode=", partyUrl);
    }

    [Fact]
    public async Task LaunchAsync_NoDiscordPresenceInjected_LaunchSucceeds_WithoutTouchingDiscord()
    {
        // Repeats the contract guarantee: when no Discord dep is present, behavior is identical
        // to v1.0 — already covered by the existing 26 RobloxLauncherTests, but pin it here so
        // a future refactor can't silently introduce a hard dep.
        var launcher = NewLauncher(presence: null, ticket: "TKT");

        var result = await launcher.LaunchAsync(TestCookie, new LaunchTarget.Place(1));

        Assert.IsType<LaunchResult.Started>(result);
    }

    [Fact]
    public async Task LaunchAsync_DiscordSetPartyThrows_LaunchStillStarted()
    {
        var presence = new RecordingPresence(setPartyThrows: new InvalidOperationException("Discord IPC down"));
        var launcher = NewLauncher(presence, ticket: "TKT");

        var result = await launcher.LaunchAsync(TestCookie,
            new LaunchTarget.PrivateServer(PlaceId: 1, Code: "C", Kind: PrivateServerCodeKind.AccessCode));

        // Launch result must be Started — Discord IPC failures cannot break launches.
        Assert.IsType<LaunchResult.Started>(result);

        // Allow the fire-and-forget task to attempt and swallow the exception.
        await Task.Delay(150);
        // Still recorded the attempt (the call happened; the exception was swallowed).
        Assert.NotNull(presence.LastSetPartyAttempt);
    }

    // ---- Helpers ----

    private static RobloxLauncher NewLauncher(IDiscordPresence? presence, string ticket)
    {
        var api = new StubApi(_ => Task.FromResult(new AuthTicket(ticket, DateTimeOffset.UtcNow)));
        var settings = new InMemoryAppSettings();
        var processStarter = new ProcessStarterStub(_ => 4242);
        return new RobloxLauncher(api, settings, processStarter, favorites: null, discordPresence: presence);
    }

    private sealed class RecordingPresence : IDiscordPresence
    {
        private readonly TaskCompletionSource<string> _setPartyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _clearPartyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Exception? _setPartyThrows;

        public RecordingPresence(Exception? setPartyThrows = null)
        {
            _setPartyThrows = setPartyThrows;
        }

        public string? LastSetPartyAttempt { get; private set; }
        public string? LastSetPartyUrl { get; private set; }

        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
        public Task UpdateStateAsync(RichPresenceState state, CancellationToken ct) => Task.CompletedTask;

        public Task SetPartyAsync(string serverShareUrl, CancellationToken ct)
        {
            LastSetPartyAttempt = serverShareUrl;
            if (_setPartyThrows is not null)
            {
                throw _setPartyThrows;
            }
            LastSetPartyUrl = serverShareUrl;
            _setPartyTcs.TrySetResult(serverShareUrl);
            return Task.CompletedTask;
        }

        public Task ClearPartyAsync(CancellationToken ct)
        {
            _clearPartyTcs.TrySetResult(true);
            return Task.CompletedTask;
        }

#pragma warning disable CS0067 // event required by IDiscordPresence; not driven by these tests
        public event EventHandler<JoinRequestedEventArgs>? JoinRequested;
#pragma warning restore CS0067
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public async Task<string?> WaitForSetPartyAsync(TimeSpan budget)
        {
            var done = await Task.WhenAny(_setPartyTcs.Task, Task.Delay(budget));
            return done == _setPartyTcs.Task ? _setPartyTcs.Task.Result : null;
        }

        public async Task<bool> WaitForClearPartyAsync(TimeSpan budget)
        {
            var done = await Task.WhenAny(_clearPartyTcs.Task, Task.Delay(budget));
            return done == _clearPartyTcs.Task && _clearPartyTcs.Task.Result;
        }
    }

    private sealed class StubApi : IRobloxApi
    {
        private readonly Func<string, Task<AuthTicket>> _ticket;
        public StubApi(Func<string, Task<AuthTicket>> ticket) { _ticket = ticket; }

        public Task<AuthTicket> GetAuthTicketAsync(string cookie) => _ticket(cookie);
        public Task<UserProfile> GetUserProfileAsync(string cookie) => throw new NotImplementedException();
        public Task<string> GetAvatarHeadshotUrlAsync(long userId) => throw new NotImplementedException();
        public Task<GameMetadata?> GetGameMetadataByPlaceIdAsync(long placeId) => throw new NotImplementedException();
        public Task<IReadOnlyList<GameSearchResult>> SearchGamesAsync(string query) => throw new NotImplementedException();
        public Task<IReadOnlyList<Friend>> GetFriendsAsync(string cookie, long userId) => throw new NotImplementedException();
        public Task<IReadOnlyList<UserPresence>> GetPresenceAsync(string cookie, IEnumerable<long> userIds) => throw new NotImplementedException();
        public Task<ShareLinkResolution?> ResolveShareLinkAsync(string cookie, string code, string linkType) => throw new NotImplementedException();
    }

    private sealed class InMemoryAppSettings : IAppSettings
    {
        public Task<string?> GetDefaultPlaceUrlAsync() => Task.FromResult<string?>(null);
        public Task SetDefaultPlaceUrlAsync(string url) => Task.CompletedTask;
        public Task<bool> GetLaunchMainOnStartupAsync() => Task.FromResult(false);
        public Task SetLaunchMainOnStartupAsync(bool enabled) => Task.CompletedTask;
        public Task<string?> GetActiveThemeIdAsync() => Task.FromResult<string?>(null);
        public Task SetActiveThemeIdAsync(string themeId) => Task.CompletedTask;
    }

    private sealed class ProcessStarterStub : IProcessStarter
    {
        private readonly Func<string, int> _behavior;
        public ProcessStarterStub(Func<string, int> behavior) { _behavior = behavior; }
        public int StartViaShell(string fileNameOrUri) => _behavior(fileNameOrUri);
    }
}
