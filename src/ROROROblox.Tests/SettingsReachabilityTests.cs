using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ROROROblox.Tests;

/// <summary>
/// Every setting the app persists is reachable from a control, or is on a list that says why not.
/// <para>
/// WHAT BROKE. F-023. <c>MemoryWatchdogEnabled</c>, <c>MemoryReserveMb</c>, <c>MemoryCapMb</c> and
/// <c>ProjectionWarnMinutes</c> have been persisted since v1.13 and referenced by zero
/// <c>.xaml</c>. A page named "Alerts &amp; memory" shipped with no memory controls on it, and the
/// only way to change any of the four was Notepad. Nothing connected <i>persisted</i> to
/// <i>reachable</i>, so the gap could not fail anything — it waited for a human audit, and the
/// audit came two minor versions later (spec §4.2, prd.md &gt; What we'd add).
/// </para>
/// <para>
/// THE RULE. Every property on the persisted settings record is either referenced from the App's
/// view layer, or appears in <see cref="UnreachableByDesign"/> carrying its reason inline. Same
/// rule the v1.17 XAML literal fence ships: an exemption names <b>why</b>, so an unreachable
/// setting is a decision rather than an oversight.
/// </para>
/// <para>
/// THIS IS A FENCE, NOT A GATE. It proves a setting's name is spoken by something that can put it
/// on screen. It does not prove the control works, that it validates, or that it is placed
/// sensibly. A green run here is not "the memory settings are editable" — it is "nothing persists
/// in silence." Whether the control refuses <c>-1</c> visibly is `prd.md &gt; Story 1.1`'s
/// criterion and lives in the manual pass.
/// </para>
/// </summary>
public class SettingsReachabilityTests
{
    /// <summary>
    /// The composition root, excluded from the view-layer walk, and the single judgement call in
    /// this file — so it is stated rather than buried.
    /// <para>
    /// <c>App.xaml.cs</c> reads settings to CONFIGURE SERVICES at startup: it is where all four of
    /// F-023's settings are consumed (<c>App.xaml.cs:1162-1175</c>), and it names three more of
    /// them in doc comments. A setting read there is being obeyed, not offered. Counting that as
    /// reachability would make this fence green on the exact defect it exists to catch — F-023's
    /// four are read at startup and have been all along; that was never the complaint.
    /// </para>
    /// <para>
    /// The exclusion is tied to a fact about the file rather than to its name:
    /// <c>App.xaml</c>'s root element is <c>&lt;Application&gt;</c>, so its code-behind owns no
    /// controls and cannot make anything reachable.
    /// <see cref="TheFenceSeesTheSettingsItClaimsTo"/> asserts that, so the day App.xaml grows a
    /// window this exclusion stops being free.
    /// </para>
    /// </summary>
    private const string CompositionRoot = "src/ROROROblox.App/App.xaml.cs";

    /// <summary>Where the persisted record lives. Core, not App — it has no UI dependencies.</summary>
    private const string SettingsSourcePath = "src/ROROROblox.Core/AppSettings.cs";

    /// <summary>
    /// The persisted record itself, reached by reflection rather than by parsing the source.
    /// <para>
    /// REFLECTION, DELIBERATELY. <c>SettingsBlob</c> is the type
    /// <c>JsonSerializer.SerializeToUtf8Bytes</c> is handed, so its public instance properties ARE
    /// the keys in <c>settings.json</c> — one indirection from the file, and no regex between this
    /// fence and the truth. A source scan would describe what a human reading the record would see;
    /// reflection describes what actually persists, and "persisted" is the half of the rule that
    /// has to be exact. It is <c>private sealed</c>, hence <see cref="BindingFlags.NonPublic"/> —
    /// metadata is readable regardless of accessibility, so no InternalsVisibleTo is involved.
    /// </para>
    /// </summary>
    private static readonly Type? SettingsRecord =
        typeof(ROROROblox.Core.AppSettings).GetNestedType("SettingsBlob", BindingFlags.NonPublic);

    private static IReadOnlyList<string> PersistedProperties() =>
        SettingsRecord is null
            ? []
            : SettingsRecord
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                // A record's compiler-generated EqualityContract is not public on a sealed record,
                // so this filter is belt-and-braces rather than load-bearing. It stays because
                // un-sealing the record would otherwise add a phantom "setting" nobody wrote.
                .Where(p => p.Name != "EqualityContract")
                .Select(p => p.Name)
                .ToList();

    /// <summary>
    /// A persisted property with no control, and the reason that is correct rather than an
    /// oversight. Every entry quotes the doc comment or the code that establishes it.
    /// </summary>
    private sealed record UnreachableByDesign(string Property, string Reason);

    private static readonly UnreachableByDesign[] Exemptions =
    [
        new("Version",
            "Not a preference — the blob's schema discriminator, written as 1 by every LoadAsync "
            + "fallback in AppSettings.cs and never read back by a control. Listed rather than left "
            + "to the matcher because it would otherwise pass BY ACCIDENT: 'VersionText' in "
            + "AboutWindow.xaml and '{Binding Version}' in PluginsWindow.xaml are the app's own "
            + "version and a plugin's, neither of which is this field. A silent accidental pass is "
            + "the same shape as the defect this fence exists for."),

        new("DefaultPlaceUrl",
            "Legacy, and the code says so twice. RobloxLauncher.cs:246 lists it as '(legacy "
            + "single-URL setting)' third in a fallback chain, and :353 calls it 'vestigial'; "
            + "RobloxLauncherTests.cs:362 pins that it 'must be ignored'. Per-account favourites "
            + "replaced it. Zero App references — no window reads it, no window writes it. It is "
            + "kept so an old settings.json still deserializes, not so anyone can set it. If it is "
            + "ever genuinely dead, the fix is deleting the field, not adding a control."),

        new("EdgeRemediationAnswers",
            "A ledger, not a setting. IAppSettings documents it as 'a theme author's answer to \"may "
            + "we raise this theme's interactive-control edge...\", keyed by theme id... the question "
            + "is put once per theme and never again.' The answer IS reachable — "
            + "EdgeRemediationWindow asks it and ThemeService.cs:104,134,164 round-trips it through "
            + "Get/SetEdgeRemediationAnswerAsync, SINGULAR, one theme at a time. The plural "
            + "dictionary is the accumulated record of those answers and has no control of its own, "
            + "which is also why the accessor edge below cannot see it: the property is "
            + "'EdgeRemediationAnswers' and the accessor is 'EdgeRemediationAnswer'. A control that "
            + "edits the whole map would be a settings editor, which is the thing this cycle is "
            + "arguing against."),
    ];

    /// <summary>
    /// THE PRIMARY EDGE: the sanctioned accessor. Nothing outside <c>AppSettings.cs</c> touches a
    /// <c>SettingsBlob</c> property directly — every reader and writer goes through
    /// <c>IAppSettings.Get&lt;Name&gt;Async</c> / <c>Set&lt;Name&gt;Async</c>, which is the
    /// checklist's own "no second source of truth" rule restated as a lexical fact. So the fence's
    /// first question is whether the view layer calls the accessor at all.
    /// </summary>
    private static Regex AccessorEdge(string property) =>
        new($@"(?<![A-Za-z0-9_])(?:Get|Set){Regex.Escape(property)}Async(?![A-Za-z0-9_])",
            RegexOptions.Compiled);

    /// <summary>
    /// THE SECONDARY EDGE: a control named for the setting. Needed because exactly one shipped
    /// setting reaches its UI without the App ever naming its accessor — <c>StreamerMode</c> is
    /// read and written by <c>Core/StreamerMode/StreamerIdentityProvider.cs</c>, and what the App
    /// holds instead is <c>x:Name="StreamerModeToggle"</c> in <c>PreferencesWindow.xaml</c> and
    /// <c>MainViewModel.StreamerModeOn</c>. An accessor-only fence would call that unreachable and
    /// be wrong.
    /// <para>
    /// HOW SUBSTRING FALSE POSITIVES ARE HANDLED, because a bare prefix match would be worse than
    /// no secondary edge at all. Three constraints, all visible in the pattern:
    /// </para>
    /// <list type="number">
    /// <item>The match starts at an identifier boundary, so <c>SomethingMemoryCapMb</c> is not a
    /// hit.</item>
    /// <item>It may not be preceded by <c>.</c> — a member read on some other object is a CONSUMER,
    /// not a control. This is the constraint that keeps
    /// <c>_memoryWatchdog.ProjectionWarnMinutes</c> (MainViewModel.cs:339,3061) from turning
    /// F-023's fourth setting green: the view-model reads the watchdog's live value to decide
    /// whether to warn, which is downstream of the setting and not a way to change it.</item>
    /// <item>What follows the name must be nothing, or a suffix from <see cref="ControlSuffixes"/>.
    /// A bare prefix match would accept <c>MemoryCapMbDefault</c>, and a derived default is not a
    /// control.</item>
    /// </list>
    /// </summary>
    private static Regex ControlEdge(string property) =>
        new($@"(?<![A-Za-z0-9_.]){Regex.Escape(property)}(?:{string.Join('|', ControlSuffixes)})?(?![A-Za-z0-9_])",
            RegexOptions.Compiled);

    /// <summary>
    /// What a control named after a setting is allowed to be called. Enumerated rather than open
    /// so the secondary edge stays a control-name rule instead of decaying into "starts with."
    /// Longest-first because .NET alternation is leftmost-first, and <c>Toggle</c> must not win
    /// over <c>ToggleButton</c> before the trailing boundary check runs.
    /// </summary>
    private static readonly string[] ControlSuffixes =
    [
        "ToggleButton", "ToggleSwitch", "CheckBox", "ComboBox", "NumberBox", "TextBox", "Toggle",
        "Slider", "Picker", "Button", "Section", "Enabled", "Header", "Switch", "Field", "Input",
        "Label", "Panel", "Check", "Combo", "Value", "Card", "Hint", "Text", "Box", "Row", "On",
        "Off",
    ];

    /// <summary>
    /// Every App source and markup file, obj/ and bin/ excluded, composition root excluded (see
    /// <see cref="CompositionRoot"/>). Off <c>XamlStyleScanner</c>'s repo-root discovery, the same
    /// one <c>ThemedStatusColourTests</c> and <c>ContrastPairGateTests</c> use, so there is one way
    /// to find the source tree and not four. No absolute path appears in this file.
    /// </summary>
    private static IEnumerable<(string RelativePath, string[] Lines)> ViewLayer()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        var appDir = XamlStyleScanner.AppSourceDirectory();
        if (root is null || appDir is null) yield break;

        foreach (var path in Directory.EnumerateFiles(appDir, "*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(path);
            if (!extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase)) continue;

            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;

            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative.Equals(CompositionRoot, StringComparison.OrdinalIgnoreCase)) continue;

            yield return (relative, File.ReadAllLines(path));
        }
    }

    /// <summary>First view-layer site that reaches the setting, as file:line, or null.</summary>
    private static string? ReachedAt(string property, IReadOnlyList<(string RelativePath, string[] Lines)> files)
    {
        var accessor = AccessorEdge(property);
        var control = ControlEdge(property);

        foreach (var (relativePath, lines) in files)
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (accessor.IsMatch(lines[i]) || control.IsMatch(lines[i]))
                {
                    return $"{relativePath}:{i + 1}";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Where the property is declared, so a failure names a file and a line rather than a bare
    /// identifier. Reads the positional record header in <c>AppSettings.cs</c>; returns -1 when the
    /// header cannot be found, which <see cref="TheFenceSeesTheSettingsItClaimsTo"/> refuses to
    /// tolerate.
    /// </summary>
    private static int DeclarationLine(string[] settingsSource, string property)
    {
        var header = Array.FindIndex(settingsSource,
            l => l.Contains("record SettingsBlob(", StringComparison.Ordinal));
        if (header < 0) return -1;

        var name = new Regex($@"(?<![A-Za-z0-9_]){Regex.Escape(property)}(?![A-Za-z0-9_])");

        for (var i = header; i < settingsSource.Length; i++)
        {
            if (name.IsMatch(settingsSource[i])) return i + 1;
            if (i > header && settingsSource[i].Contains(");", StringComparison.Ordinal)) break;
        }

        return -1;
    }

    private static string[] SettingsSource()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        if (root is null) return [];

        var path = Path.Combine(root, SettingsSourcePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? File.ReadAllLines(path) : [];
    }

    /// <summary>
    /// The fence. A persisted setting with no control and no stated reason fails here.
    /// <para>
    /// SKIPPED ON PURPOSE, FOR EXACTLY ONE ITEM. This was written BEFORE the memory controls, which
    /// is the whole proof: run un-skipped on 2026-08-10 it named MemoryWatchdogEnabled,
    /// MemoryReserveMb, MemoryCapMb and ProjectionWarnMinutes at AppSettings.cs:483-486 and nothing
    /// else. A fence written after the fix proves nothing — v1.17 learned that three times, most
    /// sharply when a gate built to its own design spec could not fail the test the design
    /// nominated to prove it. <b>Item 3 of the v1.18 checklist deletes this Skip.</b>
    /// </para>
    /// <para>
    /// WHY A SKIP AND NOT FOUR TEMPORARY EXEMPTIONS. The other route was putting the four on
    /// <see cref="Exemptions"/> with "item 3 removes this" as the reason. That would report green,
    /// and it would put a one-item IOU on the same list that holds three permanent rulings — where
    /// a forgotten IOU is indistinguishable from a decision, which is the exemption-outlives-its-
    /// finding defect <c>ThemedStatusColourTests</c> already names one layer up. A skip leaves the
    /// assertion un-doctored: the fence still knows the four are offenders, the runner declines to
    /// ask, and the count says "1 skipped" out loud on every run instead of saying nothing.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryPersistedSettingIsReachableOrExemptedWithAReason()
    {
        var properties = PersistedProperties();

        // This clause's own vacuity floor, ahead of the assertion it protects. A scan that
        // enumerates nothing passes every check below while checking nothing — the `--filter "Foo*"`
        // lesson in a different costume, and the reason both sibling fences in this project carry a
        // floor. 16 properties on the record today; 10 leaves room for churn without leaving room
        // for a dead reflection call.
        Assert.True(properties.Count >= 10,
            $"This fence enumerated only {properties.Count} persisted settings. It should be seeing "
            + "sixteen — AppSettings.SettingsBlob was renamed, un-nested or made public, so the "
            + "reflection lookup is broken. That is a broken fence, not an app with no settings.");

        var files = ViewLayer().ToList();
        var settingsSource = SettingsSource();
        var offenders = new List<string>();

        foreach (var property in properties)
        {
            if (Exemptions.Any(e => e.Property == property)) continue;
            if (ReachedAt(property, files) is not null) continue;

            var line = DeclarationLine(settingsSource, property);
            offenders.Add($"{SettingsSourcePath}:{(line > 0 ? line.ToString() : "?")}  {property}");
        }

        Assert.True(offenders.Count == 0,
            "These settings are written to settings.json and no control in the app can change "
            + "them. A user who wants one edits JSON in Notepad, which is F-023 and the reason this "
            + "cycle exists (spec §4.2). Reachable means the App view layer either calls "
            + "IAppSettings.Get<Name>Async / Set<Name>Async, or names a control after the setting "
            + $"(one of: {string.Join(", ", ControlSuffixes)}). The composition root "
            + $"{CompositionRoot} does not count — it obeys settings, it does not offer them.\n"
            + "Add the control, or add an entry to UnreachableByDesign with the reason inline the "
            + "way Version, DefaultPlaceUrl and EdgeRemediationAnswers are listed. An exemption "
            + "names why, so an unreachable setting is a decision rather than an oversight:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Backstops everything the fence above depends on, and stays running when that one does not.
    /// Each assertion here fails in the direction a broken scan would otherwise pass.
    /// </summary>
    [Fact]
    public void TheFenceSeesTheSettingsItClaimsTo()
    {
        Assert.True(SettingsRecord is not null,
            "AppSettings.SettingsBlob did not resolve by reflection. Everything in this file scans "
            + "an empty list until it does.");

        var properties = PersistedProperties();
        Assert.True(properties.Count >= 10,
            $"Reflection found {properties.Count} persisted settings; the record declares sixteen.");

        var files = ViewLayer().ToList();
        Assert.True(files.Count >= 60,
            $"The view-layer walk found only {files.Count} App .cs/.xaml files. It should be seeing "
            + "roughly 155 — the repo-root walk is broken, not the app.");

        // The matcher's own floor, and the tighter of the two. Counting settings this fence proves
        // REACHABLE catches a regex that stopped firing, which a file count never would: a dead
        // matcher reports every setting unreachable, which reads like a real finding.
        var reachable = properties.Count(p => ReachedAt(p, files) is not null);
        Assert.True(reachable >= 6,
            $"Only {reachable} settings resolved to a view-layer site. Nine do today — under six "
            + "means the accessor or control pattern stopped matching, not that the app lost its "
            + "Settings window.");

        // An exemption that outlives the property it names is the defect one layer up: a list entry
        // nobody can trace to a field is how a stale permission survives a rename.
        var stale = Exemptions.Where(e => !properties.Contains(e.Property)).Select(e => e.Property).ToList();
        Assert.True(stale.Count == 0,
            "UnreachableByDesign names settings the record no longer has: "
            + string.Join(", ", stale)
            + ". Delete the entry — a permission for a field that does not exist grants nothing and "
            + "hides the next rename.");

        // The failure message promises a file and a line. Prove it can produce one.
        var settingsSource = SettingsSource();
        var unlocatable = properties.Where(p => DeclarationLine(settingsSource, p) <= 0).ToList();
        Assert.True(unlocatable.Count == 0,
            $"These settings could not be located in {SettingsSourcePath}: "
            + string.Join(", ", unlocatable)
            + ". The fence would report them without a line, which is the half of the message that "
            + "makes it actionable.");

        // Pins the one judgement call in this file. App.xaml.cs is excluded because App.xaml
        // declares no controls; if that ever stops being true the exclusion stops being free.
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.True(root is not null, "Repo-root discovery failed; nothing in this file scanned anything.");

        var appXaml = Path.Combine(root!, "src", "ROROROblox.App", "App.xaml");
        Assert.True(File.Exists(appXaml), $"{CompositionRoot}'s markup is missing; the exclusion is unverifiable.");
        Assert.Equal("Application", XDocument.Load(appXaml).Root!.Name.LocalName);
    }
}
