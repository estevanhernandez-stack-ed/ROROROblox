using System.Xml.Linq;

namespace ROROROblox.Tests;

/// <summary>
/// Every key in the neutral <c>Strings.resx</c> must exist in every culture catalog (v1.33 item 6b).
/// <para>
/// THIS FENCE EXISTS BECAUSE THE SUITE WAS GREEN WHILE SIX LANGUAGES SHIPPED ENGLISH.
/// <see cref="LocKeyParityFenceTests"/> is named for parity and reads like it covers this, but it
/// asserts something else entirely: that every <c>{loc:Loc}</c> reference in XAML resolves in the
/// NEUTRAL catalog. A key present in <c>Strings.resx</c> and absent from all six cultures satisfies
/// it completely. v1.33 item 6 added 32 neutral keys for the consolidated Alerts section, and a
/// German, French, Russian, Portuguese, Polish or Spanish install rendered the whole section in
/// English with every gate passing. Measured 2026-10-07: neutral 1,107 keys, each culture 1,075,
/// all six missing the identical 32, no extras anywhere.
/// </para>
/// <para>
/// Cultures are DISCOVERED from the directory, not listed here. A hardcoded list is satisfiable by
/// forgetting to add the seventh language to it — the same shape of hole this fence was written to
/// close. The floor on the discovered count is what keeps a broken glob from reading as parity.
/// </para>
/// <para>
/// Product nouns are deliberately English, but they are VALUES, not keys (the pipeline's
/// <c>PRODUCT_NOUNS</c> guard holds them in place during translation), so they do not belong on the
/// allow-list below — the key must still be present in every culture, carrying the English noun.
/// </para>
/// </summary>
public class CultureKeyParityFenceTests
{
    /// <summary>
    /// Neutral keys deliberately absent from the culture catalogs. Empty, and it should stay that
    /// way: a key the UI can show is a key a translator must see. Add an entry only with a reason
    /// in the comment beside it, never to make a red build green.
    /// </summary>
    private static readonly HashSet<string> DeliberatelyNeutralOnly =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Six shipped in localization wave 1 (en plus fr/de/ru/pt-BR/pl/es). Asserted as a floor, so
    /// adding a seventh is free but losing one is loud.
    /// </summary>
    private const int ShippedCultureFloor = 6;

    /// <summary>
    /// The catalog was ~1,100 keys when this fence was written. A floor, not an equality: the
    /// catalog grows every cycle. It only has to be high enough that a failed parse cannot pass.
    /// </summary>
    private const int CatalogSizeFloor = 1000;

    private static string PropertiesDir()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate ROROROblox.slnx.");
        return Path.Combine(root!, "src", "ROROROblox.App", "Properties");
    }

    private static HashSet<string> KeysIn(string path) =>
        XDocument.Load(path).Root!
            .Elements("data")
            .Select(d => (string?)d.Attribute("name"))
            .Where(n => n is not null).Select(n => n!)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Culture catalogs as (culture, path), discovered from <c>Strings.&lt;culture&gt;.resx</c>.</summary>
    private static List<(string Culture, string Path)> CultureCatalogs() =>
        Directory.EnumerateFiles(PropertiesDir(), "Strings.*.resx")
            .Select(p => (Culture: Path.GetFileNameWithoutExtension(p)["Strings.".Length..], Path: p))
            .Where(t => t.Culture.Length > 0)
            .OrderBy(t => t.Culture, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void EveryCultureCarriesEveryNeutralKey()
    {
        var neutral = KeysIn(Path.Combine(PropertiesDir(), "Strings.resx"));
        var cultures = CultureCatalogs();

        Assert.True(neutral.Count >= CatalogSizeFloor,
            $"Only {neutral.Count} neutral keys — the load or parse broke, not the catalog.");
        Assert.True(cultures.Count >= ShippedCultureFloor,
            $"Found {cultures.Count} culture catalogs, expected at least {ShippedCultureFloor} "
            + $"({string.Join(", ", cultures.Select(c => c.Culture))}). The glob broke, or a language was lost.");

        var expected = neutral.Except(DeliberatelyNeutralOnly).ToHashSet(StringComparer.Ordinal);
        var failures = new List<string>();

        foreach (var (culture, path) in cultures)
        {
            var keys = KeysIn(path);
            Assert.True(keys.Count >= CatalogSizeFloor,
                $"Only {keys.Count} keys in {culture} — the load or parse broke, not the catalog.");

            var missing = expected.Except(keys).OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (missing.Count > 0)
            {
                failures.Add($"{culture} is missing {missing.Count}: "
                    + string.Join(", ", missing.Take(6))
                    + (missing.Count > 6 ? $", +{missing.Count - 6} more" : ""));
            }
        }

        Assert.True(failures.Count == 0,
            "Neutral keys absent from culture catalogs — those cultures render English to users:\n  "
            + string.Join("\n  ", failures)
            + "\nRun the catalog pipeline (scripts/export-ui-strings.py -> translate -> "
            + "scripts/gen-culture-resx.py). Do NOT add keys to DeliberatelyNeutralOnly to clear this.");
    }

    [Fact]
    public void NoCultureCarriesAKeyTheNeutralCatalogHasDropped()
    {
        // The other direction, and a slower rot: a key deleted from Strings.resx but left in six
        // culture files is dead weight that reads as coverage. Zero when this fence was written.
        var neutral = KeysIn(Path.Combine(PropertiesDir(), "Strings.resx"));
        var cultures = CultureCatalogs();

        Assert.True(neutral.Count >= CatalogSizeFloor,
            $"Only {neutral.Count} neutral keys — the load or parse broke, not the catalog.");
        Assert.True(cultures.Count >= ShippedCultureFloor,
            $"Found {cultures.Count} culture catalogs, expected at least {ShippedCultureFloor}.");

        var failures = new List<string>();
        foreach (var (culture, path) in cultures)
        {
            var stale = KeysIn(path).Except(neutral).OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (stale.Count > 0)
            {
                failures.Add($"{culture} carries {stale.Count} key(s) the neutral catalog does not: "
                    + string.Join(", ", stale.Take(6))
                    + (stale.Count > 6 ? $", +{stale.Count - 6} more" : ""));
            }
        }

        Assert.True(failures.Count == 0,
            "Culture catalogs carry keys the neutral catalog has dropped:\n  "
            + string.Join("\n  ", failures));
    }
}
