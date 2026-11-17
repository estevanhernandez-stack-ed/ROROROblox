using System.IO;
using System.Security.Cryptography;
using System.Text;
using ROROROblox.App.Plugins;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
using ROROROblox.Core.Discord;
using ROROROblox.Core.KnownIssues;
using ROROROblox.Core.Notify;
using ROROROblox.Core.StreamerMode;
using ROROROblox.Core.Theming;

namespace ROROROblox.Tests;

/// <summary>
/// A store file that parses as JSON but has the wrong shape (<c>{"version":1}</c> leaves a null list in
/// three stores) used to throw something other than <see cref="System.Text.Json.JsonException"/> out of
/// load, and the app crashed on every start. Every JSON-backed store is run against every shape here and
/// must treat it the way it treats an unparseable file: no exception out of load, the corrupt-file
/// result back. One row per (store, shape) so a failing row names its store. Temp folders only, fake
/// values only; <c>accounts.dat</c> has its own table below because for it the corrupt-file result is
/// not "empty".
/// </summary>
public sealed class StoreWrongShapeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"rororo-shape-{Guid.NewGuid():N}");

    public StoreWrongShapeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string P(string name) => Path.Combine(_dir, name);

    private static byte[] Protect(string json) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(json), optionalEntropy: null, DataProtectionScope.CurrentUser);

    // The shapes every store sees. Store-specific shapes (a null row, a null field) are listed per store.
    private static readonly string[] Common =
    [
        "{}",
        """{"version":1}""",
        "null",
        "[]",
    ];

    private const string Guid1 = "00000000-0000-0000-0000-000000000001";

    private static readonly Dictionary<string, string[]> Specific = new()
    {
        ["favorites"] = [
            """{"version":1,"favorites":null}""",
            """{"version":1,"favorites":[null]}""",
            """{"version":1,"favorites":[{"placeId":5}]}""", // a row with no name
        ],
        ["private-servers"] = [
            """{"version":1,"servers":null}""",
            """{"version":1,"servers":[null]}""",
            """{"version":1,"servers":[{"placeId":5}]}""", // a row with no code
        ],
        ["session-history"] = [
            """{"version":1,"sessions":null}""",
            """{"version":1,"sessions":[null]}""",
            $$$"""{"version":1,"sessions":[{"id":"{{{Guid1}}}"}]}""", // a row with no account name
        ],
        ["session-stats"] = [
            """{"accounts":null}""",
            """{"days":null}""",
            $$$"""{"accounts":{"{{{Guid1}}}":null}}""",
            """{"games":{"1":null}}""",
        ],
        ["settings"] = [
            """{"edgeRemediationAnswers":null,"alertCadenceOverridesJson":null,"alertSound":null,"libraryView":null,"libraryFilters":null,"uiLanguage":null,"activeThemeId":null}""",
            """{"edgeRemediationAnswers":{"brand":null}}""",
            """{"mainWindowLeft":"left"}""",
        ],
        ["theme"] = [
            """{"name":"x"}""", // a required colour missing
            """{"name":null,"bg":null}""",
        ],
        ["game-icons"] = [
            """{"version":1,"games":null}""",
            """{"version":1,"games":{"1":null}}""",
        ],
        ["dismissals"] = [
            """{"dismissed":null}""",
            """{"dismissed":[null,"a"]}""",
            """{"dismissed":"a"}""",
        ],
        ["known-issues-cache"] = [
            """{"schemaVersion":1}""",
            """{"schemaVersion":1,"issues":null}""",
            """{"schemaVersion":1,"issues":[null]}""",
            """{"schemaVersion":1,"issues":[{}]}""",
        ],
        ["discord"] = [
            """{"droppedOutDestinations":null}""",
            """{"mutedAccountIds":null,"idleDestinations":null}""",
            """{"mutedAccountIds":[null]}""",
        ],
        ["phone"] = [
            """{"ntfyServerUrl":null}""",
            """{"provider":"ntfy"}""",
        ],
        ["streamer"] = [
            """{"a":{}}""",
            """{"a":null}""",
        ],
        ["consent"] = [
            "[null]",
            """[{"pluginId":null,"grantedCapabilities":[]}]""",
            """[{"pluginId":"p","grantedCapabilities":null}]""",
            """[{"pluginId":"p","grantedCapabilities":[]},{"pluginId":"p","grantedCapabilities":[]}]""",
            """[{"grantedCapabilities":[]}]""", // pluginId missing: a required member
        ],
        ["plugin-catalog"] = [
            "[null]",
            """[{"id":"a"}]""",
        ],
        ["plugin-manifest"] = [
            """{"schemaVersion":1,"id":"a.b","name":"n","version":"1","contractVersion":"1","publisher":"p","description":"d","capabilities":[null]}""",
            """{"schemaVersion":"one"}""",
        ],
        ["rating-ask"] = [
            """{"days":null}""",
            """{"days":[null]}""",
        ],
    };

    public static IEnumerable<object[]> Rows()
    {
        foreach (var (store, specific) in Specific)
        {
            foreach (var shape in Common.Concat(specific))
            {
                yield return [store, shape];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task NoStoreThrowsOutOfLoad_AndAWrongShapeReadsAsTheCorruptResult(string store, string shape)
    {
        switch (store)
        {
            case "favorites":
            {
                File.WriteAllText(P("favorites.json"), shape);
                using var s = new FavoriteGameStore(P("favorites.json"));
                var list = await s.ListAsync();
                // Empty, except the two rows below that are fine by themselves and have no such guard to trip.
                Assert.Empty(list);
                Assert.Null(await s.GetDefaultAsync());
                await s.AddAsync(1, 1, "n", "t");
                Assert.Single(await s.ListAsync());
                break;
            }
            case "private-servers":
            {
                File.WriteAllText(P("ps.json"), shape);
                using var s = new PrivateServerStore(P("ps.json"));
                Assert.Empty(await s.ListAsync());
                await s.AddAsync(1, "code", PrivateServerCodeKind.LinkCode, "n", "p", "t");
                Assert.Single(await s.ListAsync());
                break;
            }
            case "session-history":
            {
                File.WriteAllText(P("h.json"), shape);
                using var s = new SessionHistoryStore(P("h.json"));
                Assert.Empty(await s.ListAsync());
                await s.AddAsync(new LaunchSession(Guid.NewGuid(), Guid.NewGuid(), "Alt", null, null, null, false, DateTimeOffset.UtcNow, null, null));
                Assert.Single(await s.ListAsync());
                break;
            }
            case "session-stats":
            {
                File.WriteAllText(P("st.json"), shape);
                using var s = new SessionStatsStore(P("st.json"));
                var stats = await s.ReadAsync();
                Assert.Empty(stats.Accounts);
                await s.ApplyAsync(new StatsEvent.ConcurrencyObserved(3));
                Assert.Equal(3, (await s.ReadAsync()).PeakConcurrentAlts);
                break;
            }
            case "settings":
            {
                File.WriteAllText(P("settings.json"), shape);
                using var s = new AppSettings(P("settings.json"));
                Assert.False(await s.GetLaunchMainOnStartupAsync());
                _ = await s.GetAlertCadenceOverridesJsonAsync();
                _ = await s.GetAlertSoundAsync();
                _ = await s.GetLibraryViewAsync();
                _ = await s.GetLibraryFiltersAsync();
                _ = await s.GetUiLanguageAsync();
                _ = await s.GetActiveThemeIdAsync();
                _ = await s.GetMainWindowPlacementAsync();
                _ = await s.GetEdgeRemediationAnswerAsync("brand");
                _ = AppSettings.ReadUiLanguageFast(P("settings.json"));
                await s.SetLaunchWindowedAsync(false);
                break;
            }
            case "theme":
            {
                var folder = Path.Combine(_dir, "themes");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "mine.json"), shape);
                var themes = await new ThemeStore(folder).ListAsync();
                Assert.DoesNotContain(themes, t => t.Id == "mine");
                break;
            }
            case "game-icons":
            {
                File.WriteAllText(P("icons.json"), shape);
                var cache = new GameIconCache(new NoApi(), P("icons.json"));
                Assert.Null(cache.TryGet(1));
                break;
            }
            case "dismissals":
            {
                File.WriteAllText(P("d.json"), shape);
                var set = new KnownIssuesDismissals(P("d.json")).Load();
                Assert.NotNull(set);
                break;
            }
            case "known-issues-cache":
            {
                using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                var body = Encoding.UTF8.GetBytes(shape);
                var cacheDir = Path.Combine(_dir, "ki");
                Directory.CreateDirectory(cacheDir);
                File.WriteAllBytes(Path.Combine(cacheDir, KnownIssuesFeed.CacheFileName), body);
                File.WriteAllBytes(Path.Combine(cacheDir, KnownIssuesFeed.CacheFileName + ".sig"),
                    signer.SignData(body, HashAlgorithmName.SHA256, RobloxCompatSignature.Format));
                var feed = new KnownIssuesFeed(new HttpClient(), null, cacheDir, signer.ExportSubjectPublicKeyInfo());
                Assert.Null(feed.LoadCache());
                break;
            }
            case "discord":
            {
                File.WriteAllBytes(P("discord.dat"), Protect(shape));
                var config = await new DiscordConfigStore(P("discord.dat")).LoadAsync();
                foreach (var kind in Enum.GetValues<AlertKind>())
                {
                    _ = config.DestinationsFor(kind).Count;
                }
                Assert.Empty(config.MutedAccountIds);
                break;
            }
            case "phone":
            {
                File.WriteAllBytes(P("notify.dat"), Protect(shape));
                var config = await new PhoneNotifyConfigStore(P("notify.dat")).LoadAsync();
                Assert.False(config.IsConfigured);
                Assert.NotNull(config.NtfyServerUrl);
                break;
            }
            case "streamer":
            {
                File.WriteAllText(P("streamer.dat"), shape);
                var ids = new FileStreamerIdentityStore(P("streamer.dat"));
                var all = await ids.LoadAllAsync();
                Assert.All(all.Values, v => { Assert.NotNull(v.FakeName); Assert.NotNull(v.FakeAvatarId); });
                Assert.Empty(all);
                await ids.SaveAsync("k", new StreamerIdentity("Fake", "1"));
                Assert.Single(await ids.LoadAllAsync());
                break;
            }
            case "consent":
            {
                File.WriteAllBytes(P("consent.dat"), Protect(shape));
                Assert.Empty(await new ConsentStore(P("consent.dat")).ListAsync());
                break;
            }
            case "plugin-catalog":
            {
                var entries = PluginCatalogParser.Parse(shape);
                Assert.Empty(entries);
                break;
            }
            case "plugin-manifest":
            {
                // A manifest that is not valid is refused with PluginManifestException, which PluginRegistry
                // skips; anything else escaping would stop plugin discovery.
                var ex = Record.Exception(() => PluginManifest.Parse(shape));
                Assert.True(ex is null or PluginManifestException, $"escaped as {ex?.GetType().Name}");
                break;
            }
            case "rating-ask":
            {
                File.WriteAllText(P(RatingAsk.FileName), shape);
                var ask = new RatingAsk(_dir, InstallChannel.Store);
                Assert.False(ask.ShouldAsk);
                ask.RecordSuccessfulLaunch();
                break;
            }
            default:
                throw new InvalidOperationException(store);
        }
    }

    private sealed class NoApi : IRobloxApi
    {
        public Task<GameMetadata?> GetGameMetadataByPlaceIdAsync(long placeId) => throw new NotSupportedException();
        public Task<AuthTicket> GetAuthTicketAsync(string cookie) => throw new NotSupportedException();
        public Task<UserProfile> GetUserProfileAsync(string cookie) => throw new NotSupportedException();
        public Task<string> GetAvatarHeadshotUrlAsync(long userId) => throw new NotSupportedException();
        public Task<IReadOnlyList<GameSearchResult>> SearchGamesAsync(string query) => throw new NotSupportedException();
        public Task<IReadOnlyList<Friend>> GetFriendsAsync(string cookie, long userId) => throw new NotSupportedException();
        public Task<IReadOnlyList<UserPresence>> GetPresenceAsync(string cookie, IEnumerable<long> userIds) => throw new NotSupportedException();
        public Task<ShareLinkResolution?> ResolveShareLinkAsync(string cookie, string code, string linkType) => throw new NotSupportedException();
    }
}
