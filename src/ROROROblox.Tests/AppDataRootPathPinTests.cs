using System.IO;
using System.Net.Http;
using System.Reflection;
using ROROROblox.App.About;
using ROROROblox.App.Localization;
using ROROROblox.App.Logging;
using ROROROblox.App.Modals;
using ROROROblox.App.Plugins;
using ROROROblox.App.Tray;
using ROROROblox.App.Updates;
using ROROROblox.Core;
using ROROROblox.Core.KnownIssues;
using ROROROblox.Core.StreamerMode;
using ROROROblox.Core.Theming;

namespace ROROROblox.Tests;

/// <summary>
/// With nothing configured, every path RoRoRo keeps under its data folder is byte-for-byte what it
/// was before <c>AppDataRoot</c> existed: <c>%LOCALAPPDATA%\ROROROblox\&lt;name&gt;</c>. One row per
/// moved site, each expected value computed the OLD way (<c>Environment.GetFolderPath</c> plus the
/// literal folder name) so a typo in a file name, a dropped segment, or a changed folder fails here
/// and not on a user's disk.
/// <para>
/// Nineteen sites expose their path through the class (a public or internal member, or a private
/// field read by reflection where the class cannot be built without WPF). Four have no seam: three
/// registrations in <c>App.xaml.cs</c> (the webview2-data folder, the data-dir handed to the
/// recorder and the Discord and phone-notify stores, and <c>consent.dat</c>) and the Diagnostics
/// page's Open data folder button. Those are pinned by a comment-stripped source check on the exact
/// statement, and <c>AppDataRootFenceTests</c> proves nothing else in those files resolves the
/// folder.
/// </para>
/// </summary>
public class AppDataRootPathPinTests
{
    /// <summary>The old way, written out, so this file does not depend on the helper it checks.</summary>
    private static string Old(params string[] parts) => Path.Combine(
        [Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ROROROblox", .. parts]);

    /// <summary>
    /// The one table of the eighteen sites that expose their path: name, how to resolve it, and the
    /// parts under the data folder it must equal. <see cref="AccessibleSites"/> (the default-path pins)
    /// and <c>AppDataRootOverrideTests</c> (the same sites under an override) both read it, so the two
    /// cannot drift apart: a site added or renamed here is checked both ways.
    /// </summary>
    internal static IReadOnlyList<(string Site, Func<string> Resolve, string[] Parts)> SiteTable { get; } = BuildSiteTable();

    private static IReadOnlyList<(string Site, Func<string> Resolve, string[] Parts)> BuildSiteTable()
    {
        var rows = new List<(string, Func<string>, string[])>();
        void Row(string site, Func<string> resolve, params string[] parts) => rows.Add((site, resolve, parts));

        // App
        Row("WelcomeWindow.SentinelPath", () => WelcomeWindow.SentinelPath, ".welcome-shown");
        Row("LanguageNoticeSentinel.SentinelPath", () => LanguageNoticeSentinel.SentinelPath, ".language-notice-shown");
        Row("AppLogging.DefaultLogDirectory", () => AppLogging.DefaultLogDirectory, "logs");
        Row("PluginRegistry.DefaultPluginsRoot", () => PluginRegistry.DefaultPluginsRoot, "plugins");
        Row("MainAvatarTrayPainter.DefaultCacheDirectory", () => MainAvatarTrayPainter.DefaultCacheDirectory, "tray-cache");
        Row("UpdateChecker.DebounceStampPath", () => UpdateChecker.DebounceStampPath, "last-update-check.txt");

        // Core
        Row("AccountStore.DefaultPath", () => AccountStore.DefaultPath(), "accounts.dat");
        Row("AppSettings.DefaultPath", () => AppSettings.DefaultPath(), "settings.json");
        Row("FavoriteGameStore.DefaultPath", () => FavoriteGameStore.DefaultPath(), "favorites.json");
        Row("KnownIssuesDismissals.DefaultPath", () => KnownIssuesDismissals.DefaultPath, "known-issues-dismissed.json");
        Row("KnownIssuesFeed (default cache directory)", KnownIssuesFeedCacheDirectory);
        Row("GameIconCache.DefaultPath", () => GameIconCache.DefaultPath(), "game-icons.json");
        Row("PrivateServerStore.DefaultPath", () => PrivateServerStore.DefaultPath(), "private-servers.json");
        Row("RobloxCompatChecker.LastKnownMutexPath", () => RobloxCompatChecker.LastKnownMutexPath, "last-known-mutex.txt");
        Row("SessionHistoryStore.DefaultPath", () => SessionHistoryStore.DefaultPath(), "session-history.json");
        Row("SessionStatsStore.DefaultPath", () => SessionStatsStore.DefaultPath(), "session-stats.json");
        Row("FileStreamerIdentityStore.DefaultPath", () => FileStreamerIdentityStore.DefaultPath(), "streamer-identities.dat");
        Row("ThemeStore.DefaultUserThemesFolder", () => ThemeStore.DefaultUserThemesFolder(), "themes");

        return rows.Select(r => (r.Item1, r.Item2, r.Item3)).ToList();
    }

    public static TheoryData<string, string, string> AccessibleSites()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (site, resolve, parts) in SiteTable)
        {
            data.Add(site, resolve(), Old(parts));
        }
        return data;
    }

    /// <summary>The cache directory a <c>KnownIssuesFeed</c> built with no override picks. Private field, no WPF needed.</summary>
    internal static string KnownIssuesFeedCacheDirectory()
    {
        using var http = new HttpClient();
        var feed = new KnownIssuesFeed(http);
        var field = typeof(KnownIssuesFeed).GetField("_cacheDirectory", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(field is not null, "KnownIssuesFeed._cacheDirectory was renamed; update this pin.");
        return (string)field!.GetValue(feed)!;
    }

    [Theory]
    [MemberData(nameof(AccessibleSites))]
    public void DefaultPath_IsTheOldLiteral(string site, string actual, string expected) =>
        Assert.True(string.Equals(expected, actual, StringComparison.Ordinal),
            $"{site}: expected '{expected}' but resolved '{actual}'.");

    /// <summary>The sites with no seam: (file under src, the statement that must be there, why).</summary>
    public static TheoryData<string, string> SourceCheckedSites() => new()
    {
        { "src/ROROROblox.App/App.xaml.cs", "ROROROblox.Core.AppDataRoot.Combine(\"webview2-data\")" },
        { "src/ROROROblox.App/App.xaml.cs", "var dataDir = ROROROblox.Core.AppDataRoot.Folder;" },
        { "src/ROROROblox.App/App.xaml.cs", "var consentPath = ROROROblox.Core.AppDataRoot.Combine(\"consent.dat\");" },
        { "src/ROROROblox.App/Diagnostics/DiagnosticsPage.xaml.cs", "OpenInExplorer(AppDataRoot.Folder);" },
    };

    [Theory]
    [MemberData(nameof(SourceCheckedSites))]
    public void SiteWithoutASeam_IsBuiltFromTheHelper(string relativePath, string statement)
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate the repo root from the test bin directory.");
        var lines = File.ReadAllLines(Path.Combine(root!, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var code = string.Join("\n", SettingsReachabilityTests.WithoutComments(relativePath, lines).Select(l => l.Trim()));

        Assert.True(code.Contains(statement, StringComparison.Ordinal),
            $"{relativePath} no longer contains `{statement}` in code. Re-pin it if the site moved.");
    }

    [Fact]
    public void Helper_Folder_IsTheOldRoot() =>
        Assert.Equal(Old(), AppDataRoot.Folder);

    [Fact]
    public void Helper_Combine_JoinsPartsUnderTheFolder()
    {
        Assert.Equal(Old("a.json"), AppDataRoot.Combine("a.json"));
        Assert.Equal(Old("plugins", "ur-task", "manifest.json"), AppDataRoot.Combine("plugins", "ur-task", "manifest.json"));
        Assert.Equal(AppDataRoot.Folder, AppDataRoot.Combine());
    }
}
