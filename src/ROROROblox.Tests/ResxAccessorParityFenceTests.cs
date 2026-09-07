using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ROROROblox.Tests;

/// <summary>
/// The generated <c>Strings</c> accessor and <c>Strings.resx</c> must never drift (localization,
/// 2026-09-06). Every XAML <c>{x:Static loc:Strings.Key}</c> reference binds to a public static
/// property on the hand-generated accessor; that accessor is produced from the resx keys by
/// <c>scripts/gen-strings-accessor.py</c>. If someone edits the resx (adds/removes a string)
/// without re-running the generator, a reference compiles against a stale property — or a
/// property points at a key the catalog no longer has, resolving to the key name at runtime. This
/// fence proves the two sets are identical, so "re-run the generator" is enforced, not just asked.
/// </summary>
public class ResxAccessorParityFenceTests
{
    private static string PropsRoot()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate ROROROblox.slnx.");
        return Path.Combine(root!, "src", "ROROROblox.App", "Properties");
    }

    private static HashSet<string> ResxKeys() =>
        XDocument.Load(Path.Combine(PropsRoot(), "Strings.resx")).Root!
            .Elements("data")
            .Select(d => (string?)d.Attribute("name"))
            .Where(n => n is not null)
            .Select(n => n!)
            .ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> AccessorProperties() =>
        Regex.Matches(
                File.ReadAllText(Path.Combine(PropsRoot(), "Strings.cs")),
                @"public static string ([A-Za-z_][A-Za-z0-9_]*) =>")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void EveryResxKeyHasAnAccessorPropertyAndViceVersa()
    {
        var keys = ResxKeys();
        var props = AccessorProperties();

        // Vacuity floor: the extraction produced hundreds. A regex that stopped matching would
        // pass two empty sets as "in parity".
        Assert.True(keys.Count >= 400,
            $"Only {keys.Count} resx keys — the resx load or the parse broke, not the catalog.");
        Assert.True(props.Count >= 400,
            $"Only {props.Count} accessor properties — did scripts/gen-strings-accessor.py run?");

        var missingProps = keys.Except(props).OrderBy(k => k).ToList();
        var orphanProps = props.Except(keys).OrderBy(k => k).ToList();

        Assert.True(missingProps.Count == 0,
            "Strings.resx has keys with no accessor property (re-run scripts/gen-strings-accessor.py): "
            + string.Join(", ", missingProps.Take(10)));
        Assert.True(orphanProps.Count == 0,
            "Strings.cs has properties with no resx key (re-run scripts/gen-strings-accessor.py): "
            + string.Join(", ", orphanProps.Take(10)));
    }
}
