using System.Globalization;
using System.IO;
using System.Xml.Linq;
using ROROROblox.App.Preferences;
using ROROROblox.App.Theming;
using ROROROblox.Core;
using ROROROblox.Core.Theming;

namespace ROROROblox.Tests;

/// <summary>
/// The Appearance page's two silent failures now speak, and they speak in the warning voice.
/// <para>
/// WHAT WAS SILENT. A theme that could not be written down still went on screen, so a failed save
/// was indistinguishable from a successful one until the next restart put the old theme back
/// (<c>prd.md &gt; Story 3.1</c>). A theme file the store could not read was dropped without a word
/// (<c>prd.md &gt; Story 3.2</c>). Neither had anything to fail on, which is why both survived to
/// v1.17.
/// </para>
/// <para>
/// NO WINDOW IS CONSTRUCTED HERE, following <see cref="AutomaticMemorySummaryTests"/> and
/// <c>MutedAccountsSummaryTests</c>. The suite is headless — no STA thread, no
/// <c>Application</c> — so the copy and the outcome that selects it live outside the click handler
/// where a test can reach them. What that leaves to a human at C2 is named at the bottom of this
/// file.
/// </para>
/// </summary>
public class ThemeStatusSummaryTests : IDisposable
{
    private readonly string _tempDir;

    public ThemeStatusSummaryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"rororoblox-theme-status-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>A theme file the store will accept, so "the good ones still load" has good ones.</summary>
    private static string ValidThemeJson(string name) => $$"""
    {
      "name": "{{name}}",
      "bg": "#101020",
      "cyan": "#11aacc",
      "magenta": "#cc2299",
      "white": "#fafafa",
      "muted_text": "#7a8090",
      "divider": "#1a2030",
      "row_bg": "#152535",
      "row_expired_bg": "#3a2d14",
      "row_expired_accent": "#f1b232",
      "navy": "#0a1320"
    }
    """;

    // ---------------------------------------------------------------------------------------
    // (a) A failed persist says so. A successful one does not.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The whole point of the item, in one assertion: the message says the theme IS on before it
    /// says anything went wrong, because <c>prd.md &gt; Story 3.1</c> requires the session not to be
    /// degraded and a user who reads only the first clause has read the true part.
    /// </summary>
    [Fact]
    public void APersistFailureSaysTheThemeIsOnAndThatItWasNotRemembered()
    {
        var line = ThemeStatusSummary.ForThemeChange(
            "Midnight", ThemeChange.AppliedButNotSaved("Access to the path 'settings.json' is denied."));

        Assert.True(line.Any);
        Assert.Equal(
            "▲ Midnight is on now, but RoRoRo couldn't remember it: Access to the path "
            + "'settings.json' is denied. You'll be back on your old theme the next time you start.",
            line.Text);
    }

    /// <summary>
    /// Success stays silent (<c>prd.md &gt; Story 3.1</c>). A status line that speaks on every save
    /// is noise, and noise is how the one message that matters gets skipped.
    /// </summary>
    [Fact]
    public void ASuccessfulPersistSaysNothingAtAll()
    {
        var line = ThemeStatusSummary.ForThemeChange("Midnight", ThemeChange.Saved);

        Assert.False(line.Any);
        Assert.Equal(string.Empty, line.Text);
    }

    /// <summary>
    /// A theme id with nothing behind it — reachable by deleting a theme file while Settings is
    /// open. Nothing was applied, so the sentence must not claim anything is on. This is the state
    /// a single "did it save?" boolean would have collapsed into the failure message and lied about.
    /// </summary>
    [Fact]
    public void AThemeThatIsNoLongerThereIsNotDescribedAsBeingOn()
    {
        var line = ThemeStatusSummary.ForThemeChange("Sunset", ThemeChange.Missing);

        Assert.True(line.Any);
        Assert.Equal(
            "▲ Sunset isn't in your themes folder any more, so nothing changed. Close and reopen "
            + "Settings to see what's there now.",
            line.Text);
        Assert.DoesNotContain("is on now", line.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// An exception message is not guaranteed to end in punctuation and this one lands mid-sentence
    /// with a clause behind it. Without the terminator the line reads "…disk is full You'll be…".
    /// </summary>
    [Fact]
    public void AnUnpunctuatedErrorDoesNotRunIntoTheNextSentence()
    {
        var line = ThemeStatusSummary.ForThemeChange("Brand", ThemeChange.AppliedButNotSaved("The disk is full"));

        Assert.Contains("couldn't remember it: The disk is full. You'll be back", line.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A failure with no message attached still says the useful half. The alternative is a dangling
    /// colon, which reads as a truncated app rather than a failed write.
    /// </summary>
    [Fact]
    public void AFailureWithNoMessageStillReadsAsASentence()
    {
        var line = ThemeStatusSummary.ForThemeChange("Brand", ThemeChange.AppliedButNotSaved("   "));

        Assert.Equal(
            "▲ Brand is on now, but RoRoRo couldn't remember it. You'll be back on your old theme "
            + "the next time you start.",
            line.Text);
    }

    // ---------------------------------------------------------------------------------------
    // (a) again, one level down: the outcome that selects the message is the real one.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// THE COPY IS ONLY HALF OF IT. A perfect sentence selected by an outcome nothing produces is
    /// still a silent failure, so this drives a REAL <see cref="ThemeService"/> over a REAL
    /// <see cref="AppSettings"/> whose write genuinely cannot land, and asserts the outcome it
    /// returns. <c>SetActiveThemeIdAsync</c> ends in <c>SaveAsync</c>, which calls
    /// <c>Directory.CreateDirectory</c> on the settings file's parent — pointing that at an existing
    /// FILE is a real IOException from the real code path, not a stub throwing on demand.
    /// <para>
    /// No <c>Application</c> exists in this suite, so <c>ThemeService.ApplyToResources</c> returns
    /// at its null-dispatcher guard. That is the one half of "applies live" this file cannot prove
    /// and C2 owns.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AWriteThatCannotLandComesBackAsAPersistFailure()
    {
        var blocker = Path.Combine(_tempDir, "not-a-folder");
        await File.WriteAllTextAsync(blocker, "this is a file, so nothing can be created inside it");

        using var settings = new AppSettings(Path.Combine(blocker, "settings.json"));
        var service = new ThemeService(new ThemeStore(Path.Combine(_tempDir, "themes")), settings);

        var change = await service.SetActiveAsync("midnight");

        Assert.True(change.Found);
        Assert.False(change.Persisted);
        Assert.False(string.IsNullOrWhiteSpace(change.PersistError));

        // And it reaches a message rather than stopping at a flag nobody renders.
        Assert.True(ThemeStatusSummary.ForThemeChange("Midnight", change).Any);
    }

    /// <summary>
    /// The other half of the same claim, and the one that stops this becoming a line that always
    /// speaks: the identical call over a writable path returns Persisted and produces nothing.
    /// </summary>
    [Fact]
    public async Task AWriteThatLandsComesBackSilent()
    {
        using var settings = new AppSettings(Path.Combine(_tempDir, "settings.json"));
        var service = new ThemeService(new ThemeStore(Path.Combine(_tempDir, "themes")), settings);

        var change = await service.SetActiveAsync("midnight");

        Assert.True(change.Persisted);
        Assert.Equal("midnight", await settings.GetActiveThemeIdAsync());
        Assert.False(ThemeStatusSummary.ForThemeChange("Midnight", change).Any);
    }

    /// <summary>An id no theme answers to comes back as Missing rather than as a failed save.</summary>
    [Fact]
    public async Task AnIdWithNoThemeBehindItComesBackMissing()
    {
        using var settings = new AppSettings(Path.Combine(_tempDir, "settings.json"));
        var service = new ThemeService(new ThemeStore(Path.Combine(_tempDir, "themes")), settings);

        var change = await service.SetActiveAsync("no-such-theme");

        Assert.False(change.Found);
        Assert.False(change.Persisted);
    }

    // ---------------------------------------------------------------------------------------
    // (b) An unreadable file is named, and the good ones in the same folder still load.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// <c>prd.md &gt; Story 3.2</c>, both clauses at once, and driven through a REAL
    /// <see cref="ThemeStore"/> rather than a hand-built list. That matters more here than anywhere
    /// else in this file: the report infers "could not be read" from a file being on disk while its
    /// id is absent from the store's output, which means it holds a second copy of the store's
    /// filename-to-id rule. Feeding the real store's real output through it is what stops that copy
    /// drifting from <c>ThemeStore.cs:102</c> without a red build.
    /// </summary>
    [Fact]
    public async Task AnUnreadableFileIsNamedAndTheGoodOnesStillLoad()
    {
        var themesDir = Path.Combine(_tempDir, "themes");
        Directory.CreateDirectory(themesDir);
        await File.WriteAllTextAsync(Path.Combine(themesDir, "broken.json"), "{ this is not valid json");
        await File.WriteAllTextAsync(Path.Combine(themesDir, "sunset.json"), ValidThemeJson("Sunset"));

        var store = new ThemeStore(themesDir);
        var loaded = await store.ListAsync();

        // The good one still loads. A folder with one bad file among good ones is the acceptance
        // criterion, not an aside.
        Assert.Contains(loaded, t => t.Id == "sunset" && !t.IsBuiltIn);
        Assert.DoesNotContain(loaded, t => t.Id == "broken");

        var line = ThemeStatusSummary.ForFolder(loaded, FileNamesIn(themesDir));

        Assert.True(line.Any);
        Assert.Equal(
            "▲ RoRoRo couldn't read broken.json, so it isn't in the list. Check it for a typo or a "
            + "missing line, then close and reopen Settings.",
            line.Text);

        // It names the file that failed and no other. A report that also named the good file would
        // send somebody to edit a theme that works.
        Assert.DoesNotContain("sunset.json", line.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A missing required field is the other way <see cref="ThemeStore"/> drops a file, and it is
    /// the likelier one — pasted JSON parses fine and is short a line. Same report, because it is
    /// the same thing to fix.
    /// </summary>
    [Fact]
    public async Task AFileMissingAColourIsReportedTheSameWay()
    {
        var themesDir = Path.Combine(_tempDir, "themes");
        Directory.CreateDirectory(themesDir);
        await File.WriteAllTextAsync(
            Path.Combine(themesDir, "incomplete.json"),
            ValidThemeJson("Incomplete").Replace("\"magenta\": \"#cc2299\",", "", StringComparison.Ordinal));

        var store = new ThemeStore(themesDir);
        var line = ThemeStatusSummary.ForFolder(await store.ListAsync(), FileNamesIn(themesDir));

        Assert.True(line.Any);
        Assert.Contains("couldn't read incomplete.json", line.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// An intact folder says nothing. This is the clause that keeps the line from becoming
    /// wallpaper — a warning that is always on screen is a warning nobody reads.
    /// </summary>
    [Fact]
    public async Task AFolderWhereEverythingLoadedSaysNothing()
    {
        var themesDir = Path.Combine(_tempDir, "themes");
        Directory.CreateDirectory(themesDir);
        await File.WriteAllTextAsync(Path.Combine(themesDir, "sunset.json"), ValidThemeJson("Sunset"));
        await File.WriteAllTextAsync(Path.Combine(themesDir, "dawn.json"), ValidThemeJson("Dawn"));

        var store = new ThemeStore(themesDir);
        var line = ThemeStatusSummary.ForFolder(await store.ListAsync(), FileNamesIn(themesDir));

        Assert.False(line.Any);
        Assert.Equal(string.Empty, line.Text);
    }

    /// <summary>An empty folder, and no folder at all, are both nothing to report.</summary>
    [Fact]
    public async Task AnEmptyFolderSaysNothing()
    {
        var store = new ThemeStore(Path.Combine(_tempDir, "themes"));
        var loaded = await store.ListAsync();

        Assert.False(ThemeStatusSummary.ForFolder(loaded, []).Any);
        Assert.False(ThemeStatusSummary.ForFolder(loaded, null).Any);
    }

    /// <summary>
    /// THE FALSE-REPORT CLAUSE, and the reason the report distinguishes two causes instead of one.
    /// <see cref="ThemeStore"/> drops a user file whose id a built-in already owns
    /// (<c>ThemeStore.cs:73-76</c>) — the file is perfectly readable and the JSON is fine. Reporting
    /// it as unreadable would send its author hunting for a syntax error that is not there, and
    /// "check it for a missing comma" is advice that could never work. A rename is the only thing
    /// that fixes it, so a rename is what it says.
    /// </summary>
    [Fact]
    public async Task AFileNamedAfterABuiltInIsNotReportedAsUnreadable()
    {
        var themesDir = Path.Combine(_tempDir, "themes");
        Directory.CreateDirectory(themesDir);
        await File.WriteAllTextAsync(Path.Combine(themesDir, "brand.json"), ValidThemeJson("Hijacked Brand"));

        var store = new ThemeStore(themesDir);
        var loaded = await store.ListAsync();

        // Precondition, stated rather than assumed: the built-in won and the file is not in the list
        // under its own name. Both halves of what the report has to reason about.
        Assert.True(loaded.Single(t => t.Id == "brand").IsBuiltIn);

        var line = ThemeStatusSummary.ForFolder(loaded, FileNamesIn(themesDir));

        Assert.True(line.Any);
        Assert.Equal(
            "▲ brand.json has the same name as a built-in theme, so RoRoRo kept the built-in. "
            + "Rename the file to use yours.",
            line.Text);
        Assert.DoesNotContain("couldn't read", line.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both failures at once, each in its own sentence. Two bad files and one shadowed file is not
    /// exotic — it is what a folder looks like after somebody pastes three themes from a chat.
    /// </summary>
    [Fact]
    public async Task BothKindsOfProblemAreReportedTogether()
    {
        var themesDir = Path.Combine(_tempDir, "themes");
        Directory.CreateDirectory(themesDir);
        await File.WriteAllTextAsync(Path.Combine(themesDir, "aa-broken.json"), "{ nope");
        await File.WriteAllTextAsync(Path.Combine(themesDir, "bb-broken.json"), "also nope");
        await File.WriteAllTextAsync(Path.Combine(themesDir, "flatline.json"), ValidThemeJson("Hijacked Flatline"));
        await File.WriteAllTextAsync(Path.Combine(themesDir, "sunset.json"), ValidThemeJson("Sunset"));

        var store = new ThemeStore(themesDir);
        var line = ThemeStatusSummary.ForFolder(await store.ListAsync(), FileNamesIn(themesDir));

        Assert.Equal(
            "▲ RoRoRo couldn't read 2 files in your themes folder, so they aren't in the list: "
            + "aa-broken.json, bb-broken.json. Check each one for a typo or a missing line, then "
            + "close and reopen Settings. flatline.json has the same name as a built-in theme, so "
            + "RoRoRo kept the built-in. Rename the file to use yours.",
            line.Text);
    }

    /// <summary>
    /// A wrapping TextBlock holding forty filenames pushes the card off the page and the fortieth
    /// name helps nobody. Five, then a count.
    /// </summary>
    [Fact]
    public void AVeryBrokenFolderNamesFiveAndCountsTheRest()
    {
        var files = Enumerable.Range(1, 8)
            .Select(i => $"broken-{i.ToString(CultureInfo.InvariantCulture)}.json")
            .ToList();

        var line = ThemeStatusSummary.ForFolder([], files);

        Assert.Contains(
            "broken-1.json, broken-2.json, broken-3.json, broken-4.json, broken-5.json, and 3 more",
            line.Text,
            StringComparison.Ordinal);
        Assert.DoesNotContain("broken-6.json", line.Text, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------
    // The glyph. Same codepoint as every other warning surface, and it survived being written.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// U+25B2 BLACK UP-POINTING TRIANGLE, pinned by codepoint rather than by eye. The failure mode
    /// is an encoding accident, and mojibake still renders SOMETHING — a human proofing a screenshot
    /// can miss it, this cannot. Emoji_Presentation=No, so it is a geometric glyph and not emoji,
    /// which is what lets it through <c>CLAUDE.md</c>'s no-emoji rule.
    /// </summary>
    [Fact]
    public void TheWarningGlyphIsTheCodepointTheRestOfTheAppUses()
    {
        Assert.True(ThemeStatusSummary.WarnGlyph.Length == 1,
            $"The warning prefix is {ThemeStatusSummary.WarnGlyph.Length} chars. U+25B2 is one — a "
            + "longer value means it was re-encoded, and mojibake renders something.");
        Assert.Equal(0x25B2, ThemeStatusSummary.WarnGlyph[0]);
        Assert.Equal(
            System.Globalization.UnicodeCategory.OtherSymbol,
            char.GetUnicodeCategory(ThemeStatusSummary.WarnGlyph[0]));
    }

    /// <summary>
    /// Every message this class produces carries the glyph in its TEXT, not only in its brush. The
    /// warning has to survive colour being taken away — the rule flatline made non-negotiable, and
    /// the reason the five warning surfaces that came before this one prefix it too: expired rows,
    /// idle chips, memory chips, the compat banner and item 3's memory-settings line.
    /// </summary>
    [Fact]
    public void EveryMessageCarriesTheGlyphInTheTextItself()
    {
        var messages = new[]
        {
            ThemeStatusSummary.ForThemeChange("Midnight", ThemeChange.AppliedButNotSaved("nope")),
            ThemeStatusSummary.ForThemeChange("Midnight", ThemeChange.Missing),
            ThemeStatusSummary.ForFolder([], ["broken.json"]),
        };

        Assert.All(messages, line =>
        {
            Assert.True(line.Any);
            Assert.Equal(0x25B2, line.Text[0]);
            Assert.Equal("▲ ", line.Text[..2]);
        });
    }

    /// <summary>
    /// THE ENCODING CLAUSE, and it reads the files rather than the compiled constants. A source file
    /// re-saved in the wrong codepage turns the glyph into mojibake that still compiles and still
    /// renders — so this asserts the same codepoint is physically present in this cycle's new
    /// surface AND in the one item 3 shipped, which is the surface this one was told to match.
    /// </summary>
    [Theory]
    [InlineData("src/ROROROblox.App/Preferences/ThemeStatusSummary.cs")]
    [InlineData("src/ROROROblox.App/Preferences/SettingsPage.xaml.cs")]
    [InlineData("src/ROROROblox.App/ViewModels/MemoryChipFormatter.cs")]
    [InlineData("src/ROROROblox.App/ViewModels/AccountSummary.cs")]
    public void TheGlyphSurvivedBeingWrittenToDisk(string relativePath)
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.NotNull(root);

        var path = Path.Combine(root!, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"{path} is missing; this test scanned nothing.");

        var text = File.ReadAllText(path);
        Assert.Contains(ThemeStatusSummary.WarnGlyph, text, StringComparison.Ordinal);

        // Mojibake's signature: U+25B2 read back through a single-byte codepage becomes "â–²".
        // It compiles, it renders, and it is not a triangle.
        Assert.DoesNotContain("â–", text, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------
    // The tooltip that used to promise a restart.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The tooltip said "after restart" and the code has never needed one:
    /// <c>ThemeStore.ListAsync</c> re-enumerates the folder on every call and caches only the
    /// built-ins, <c>PreferencesWindow.OnLoaded</c> calls it on every open, and
    /// <c>App.BuildPreferencesWindow</c> constructs a fresh window per open. Reopening Settings is
    /// enough.
    /// <para>
    /// IT SAYS "REOPEN SETTINGS", NOT "REOPEN THIS PAGE". The rail's five pages are one window with
    /// five StackPanels whose Visibility is toggled (<c>PreferencesWindow.xaml.cs</c>'s nav
    /// handler); clicking Appearance re-lists nothing. "Reopen this page" would have been a second
    /// false promise in place of the first.
    /// </para>
    /// </summary>
    [Fact]
    public void TheThemesFolderTooltipDoesNotPromiseARestart()
    {
        var file = XamlStyleScanner.EnumerateAppXamlFiles()
            .FirstOrDefault(f => Path.GetFileName(f.FullPath) == "SettingsPage.xaml");
        Assert.NotNull(file.FullPath);

        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var button = XDocument.Load(file.FullPath)
            .Descendants()
            .SingleOrDefault(e => e.Attribute(x + "Name")?.Value == "OpenThemesFolderButton");

        Assert.True(button is not null,
            "OpenThemesFolderButton was not found in SettingsPage.xaml, so this test asserted "
            + "nothing about its tooltip.");

        var tooltip = button!.Attribute("ToolTip")?.Value ?? "";

        Assert.False(string.IsNullOrWhiteSpace(tooltip),
            "The themes-folder button lost its tooltip. The claim it makes is the point of this "
            + "test; an absent tooltip passes a 'does not say restart' check while telling the user "
            + "nothing.");
        Assert.DoesNotContain("restart", tooltip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reopen Settings", tooltip, StringComparison.Ordinal);
    }

    /// <summary>
    /// The mechanism behind the tooltip's new claim, asserted rather than asserted-about: a file
    /// dropped into the folder after the store has already listed once shows up on the next list,
    /// with no new store and no process restart. If this ever goes red the tooltip is a lie again.
    /// </summary>
    [Fact]
    public async Task AFileDroppedAfterTheFirstListShowsUpOnTheNextOne()
    {
        var themesDir = Path.Combine(_tempDir, "themes");
        Directory.CreateDirectory(themesDir);

        var store = new ThemeStore(themesDir);
        Assert.DoesNotContain(await store.ListAsync(), t => t.Id == "sunset");

        await File.WriteAllTextAsync(Path.Combine(themesDir, "sunset.json"), ValidThemeJson("Sunset"));

        Assert.Contains(await store.ListAsync(), t => t.Id == "sunset");
    }

    /// <summary>
    /// The same enumeration <c>PreferencesWindow.ThemeFolderFileNames</c> performs — same pattern,
    /// same scope, same shape as <c>ThemeStore.ListAsync</c>'s own walk.
    /// </summary>
    private static IReadOnlyList<string> FileNamesIn(string folder) =>
        Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
