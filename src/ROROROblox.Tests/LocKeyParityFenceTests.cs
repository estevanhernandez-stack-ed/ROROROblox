using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ROROROblox.Tests;

/// <summary>
/// Every <c>{loc:Loc Key}</c> reference in XAML must resolve to a real <c>Strings.resx</c> key
/// (localization Phase D, 2026-09-07). The codemod moved the UI off <c>{x:Static loc:Strings.Key}</c>
/// — which bound to a compile-checked accessor property — to <c>{loc:Loc Key}</c>, a runtime
/// string-keyed lookup through <see cref="ROROROblox.App.Localization.TranslationSource"/>. A dangling
/// key no longer fails to compile; it silently renders the key name to the user. This fence restores
/// that safety by proving every key the XAML asks for exists in the catalog. It replaces the old
/// resx⇄accessor parity fence — the generated <c>Strings.cs</c> accessor (and
/// <c>scripts/gen-strings-accessor.py</c>) is retired now that lookups are by string key.
/// </summary>
public class LocKeyParityFenceTests
{
    private static readonly Regex LocRef =
        new(@"\{loc:Loc\s+(?:Key=)?([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    private static string AppRoot()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate ROROROblox.slnx.");
        return Path.Combine(root!, "src", "ROROROblox.App");
    }

    private static HashSet<string> ResxKeys() =>
        XDocument.Load(Path.Combine(AppRoot(), "Properties", "Strings.resx")).Root!
            .Elements("data")
            .Select(d => (string?)d.Attribute("name"))
            .Where(n => n is not null).Select(n => n!)
            .ToHashSet(StringComparer.Ordinal);

    private static List<(string file, string key)> LocRefs()
    {
        var refs = new List<(string, string)>();
        foreach (var xaml in Directory.EnumerateFiles(AppRoot(), "*.xaml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(xaml);
            foreach (Match m in LocRef.Matches(text))
                refs.Add((Path.GetFileName(xaml), m.Groups[1].Value));
        }
        return refs;
    }

    [Fact]
    public void EveryLocLocKeyExistsInTheResxCatalog()
    {
        var keys = ResxKeys();
        var refs = LocRefs();

        // Vacuity floors: the codemod produced 500+ references across ~480 keys. Two empty sets
        // must not read as "in parity".
        Assert.True(keys.Count >= 400, $"Only {keys.Count} resx keys — the load/parse broke, not the catalog.");
        Assert.True(refs.Count >= 400, $"Only {refs.Count} {{loc:Loc}} refs — the XAML scan broke, or the codemod regressed.");

        var dangling = refs.Where(r => !keys.Contains(r.key)).Distinct().OrderBy(r => r.key).ToList();
        Assert.True(dangling.Count == 0,
            "{loc:Loc Key} references with no Strings.resx key (would render the key name to users): "
            + string.Join(", ", dangling.Take(10).Select(r => $"{r.key} ({r.file})")));
    }
}
