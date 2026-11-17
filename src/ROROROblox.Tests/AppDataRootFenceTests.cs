using System.IO;
using System.Text.RegularExpressions;

namespace ROROROblox.Tests;

/// <summary>
/// One place decides where RoRoRo's data lives (<c>ROROROblox.Core.AppDataRoot</c>), and this fence
/// keeps it one place.
/// <para>
/// Before the move, 33 call sites in <c>ROROROblox.App</c> and <c>ROROROblox.Core</c> resolved
/// <c>SpecialFolder.LocalApplicationData</c> themselves: 23 to build a path under
/// <c>ROROROblox</c> (RoRoRo's own data) and 10 for the Roblox client's folders
/// (<c>%LOCALAPPDATA%\Roblox\...</c>, <c>%LOCALAPPDATA%\Packages</c>). A data-folder override only
/// works if the 23 cannot multiply, so the rule is a source fence: the API may appear in the helper
/// and in the exact, counted list of Roblox-folder sites below, and nowhere else.
/// </para>
/// <para>
/// The list is asserted as EQUALITY, per file and in total. A new RoRoRo site fails it (an
/// unlisted file, or a listed file with a higher count). A Roblox site that goes away, or moves
/// onto the helper by mistake, fails it too, because the list would then name something that no
/// longer exists. Comments are stripped before counting (<c>SettingsReachabilityTests.WithoutComments</c>),
/// so prose that names the API does not count.
/// </para>
/// </summary>
public class AppDataRootFenceTests
{
    /// <summary>The Roblox client's own folders: not RoRoRo's data, so they do not move. File to count.</summary>
    private static readonly IReadOnlyDictionary<string, int> RobloxFolderSites = new Dictionary<string, int>
    {
        ["src/ROROROblox.App/Diagnostics/AppStorageDefender.cs"] = 1,
        ["src/ROROROblox.Core/ClientAppSettingsWriter.cs"] = 2,
        ["src/ROROROblox.Core/Diagnostics/DiagnosticsCollector.cs"] = 1,
        ["src/ROROROblox.Core/GlobalBasicSettingsProbe.cs"] = 1,
        ["src/ROROROblox.Core/GlobalBasicSettingsWriter.cs"] = 1,
        ["src/ROROROblox.Core/RobloxCompatChecker.cs"] = 2,
        ["src/ROROROblox.Core/RobloxInstallLocator.cs"] = 2,
    };

    private const string HelperFile = "src/ROROROblox.Core/AppDataRoot.cs";

    /// <summary>The API's own name, as a whole token, so a using-static spelling cannot slip past.</summary>
    private static readonly Regex LocalAppDataToken = new(@"\bLocalApplicationData\b", RegexOptions.Compiled);

    /// <summary>The environment-variable spellings of the same folder, in code.</summary>
    private static readonly Regex LocalAppDataEnvironment = new(@"%LOCALAPPDATA%|""LOCALAPPDATA""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string RepoRoot()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate the repo root from the test bin directory.");
        return root!;
    }

    /// <summary>Every .cs file in the App and Core projects, bin and obj excluded, with repo-relative forward-slash names.</summary>
    private static IEnumerable<(string Relative, string FullPath)> ProductionSources(string root)
    {
        foreach (var project in new[] { "ROROROblox.App", "ROROROblox.Core" })
        {
            var projectDir = Path.Combine(root, "src", project);
            foreach (var path in Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories))
            {
                var underProject = Path.GetRelativePath(projectDir, path).Replace('\\', '/');
                if (underProject.StartsWith("obj/", StringComparison.Ordinal) || underProject.StartsWith("bin/", StringComparison.Ordinal))
                {
                    continue;
                }
                yield return ($"src/{project}/{underProject}", path);
            }
        }
    }

    /// <summary>How many lines of real code (comments blanked) carry the pattern.</summary>
    internal static int CountInCode(string relativePath, string[] lines, Regex pattern) =>
        SettingsReachabilityTests.WithoutComments(relativePath, lines).Sum(l => pattern.Matches(l).Count);

    [Fact]
    public void LocalApplicationData_AppearsOnlyInTheHelperAndTheRobloxFolderSites()
    {
        var root = RepoRoot();
        var actual = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var scanned = 0;

        foreach (var (relative, fullPath) in ProductionSources(root))
        {
            scanned++;
            if (relative == HelperFile)
            {
                continue;
            }
            var count = CountInCode(relative, File.ReadAllLines(fullPath), LocalAppDataToken);
            if (count > 0)
            {
                actual[relative] = count;
            }
        }

        // Vacuity floor: the walk found the projects. A wrong root or a moved folder would find
        // nothing, and an empty scan would "match" an empty list only if the list were empty too.
        Assert.True(scanned > 200, $"Scanned only {scanned} source files; the walk is broken, so a pass would mean nothing.");

        var unlisted = actual.Where(kv => !RobloxFolderSites.ContainsKey(kv.Key)).ToList();
        var wrongCount = actual.Where(kv => RobloxFolderSites.TryGetValue(kv.Key, out var want) && want != kv.Value).ToList();
        var gone = RobloxFolderSites.Where(kv => !actual.ContainsKey(kv.Key)).ToList();

        var problems = new List<string>();
        problems.AddRange(unlisted.Select(kv =>
            $"{kv.Key}: {kv.Value} un-allow-listed use(s) of LocalApplicationData. If this is RoRoRo's data, use AppDataRoot.Folder / AppDataRoot.Combine. "
            + "If it is the Roblox client's folder, add the file and count to RobloxFolderSites."));
        problems.AddRange(wrongCount.Select(kv =>
            $"{kv.Key}: allow-listed for {RobloxFolderSites[kv.Key]} but found {kv.Value}. A Roblox-folder site was added or removed here."));
        problems.AddRange(gone.Select(kv =>
            $"{kv.Key}: allow-listed for {kv.Value} but found none. The Roblox-folder site moved or was removed; update RobloxFolderSites."));

        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.Equal(10, actual.Values.Sum());
        Assert.Equal(RobloxFolderSites.Values.Sum(), actual.Values.Sum());
    }

    [Fact]
    public void TheHelperHoldsTheOnlyRoRoRoResolution()
    {
        var root = RepoRoot();
        var path = Path.Combine(root, HelperFile.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"{HelperFile} is gone: the allow-list fence names it, so update both together.");

        // Exactly one call: Folder. If the helper grows a second resolution it is deciding twice.
        Assert.Equal(1, CountInCode(HelperFile, File.ReadAllLines(path), LocalAppDataToken));
    }

    [Fact]
    public void NoCodeReadsLocalAppDataFromTheEnvironment()
    {
        // The other way to the same folder: %LOCALAPPDATA% in a string, or the variable by name.
        // Comments and prose are stripped, so the doc comments that say where a file lives do not count.
        var root = RepoRoot();
        var hits = new List<string>();
        foreach (var (relative, fullPath) in ProductionSources(root))
        {
            var code = SettingsReachabilityTests.WithoutComments(relative, File.ReadAllLines(fullPath));
            for (var i = 0; i < code.Length; i++)
            {
                if (LocalAppDataEnvironment.IsMatch(code[i]))
                {
                    hits.Add($"{relative}:{i + 1}: {code[i].Trim()}");
                }
            }
        }

        Assert.True(hits.Count == 0, "LOCALAPPDATA read from the environment; go through AppDataRoot or Environment.GetFolderPath in an allow-listed Roblox site:\n" + string.Join("\n", hits));
    }

    [Fact]
    public void ProseNamingTheApiDoesNotCountAsAUse()
    {
        // Holds the counter honest: comments are blanked, real code still counts, and a trailing
        // comment on a real line does not hide the real call.
        Assert.Equal(0, CountInCode("x.cs", ["// Environment.SpecialFolder.LocalApplicationData is the folder."], LocalAppDataToken));
        Assert.Equal(0, CountInCode("x.cs", ["/// <c>SpecialFolder.LocalApplicationData</c> names it."], LocalAppDataToken));
        Assert.Equal(0, CountInCode("x.cs", ["/* spans", "   LocalApplicationData", "   lines */"], LocalAppDataToken));
        Assert.Equal(1, CountInCode("x.cs", ["var f = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);"], LocalAppDataToken));
        Assert.Equal(1, CountInCode("x.cs", ["var f = Get(SpecialFolder.LocalApplicationData); // note"], LocalAppDataToken));
        Assert.Equal(2, CountInCode("x.cs", ["Get(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.LocalApplicationData);"], LocalAppDataToken));
    }
}
