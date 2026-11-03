using System.IO;
using System.Text.RegularExpressions;

namespace ROROROblox.Tests;

/// <summary>
/// v1.36 item 11 (spec 7.5, #232): the six translated listing sheets stay tied to the English one. A translated
/// long description that drops a bullet, or a features list that loses a line, used to reach Partner Center
/// unnoticed because nothing compared the sheets. Reads <c>docs/store/listing-copy.md</c> and every
/// <c>listing-copy-&lt;culture&gt;.md</c>, takes the first fenced block after each heading prefix (the prefix
/// match <c>scripts/export-listing-translations.py</c>'s <c>block_after_heading</c> uses, because the English
/// sheet's headings carry different suffixes), and asserts equal shape across all seven.
/// <para>
/// What's new is excluded: the English sheet points at <c>whats-new-&lt;version&gt;.md</c> instead of holding a
/// block. The floors stop a parser that silently stopped finding blocks from passing on zero.
/// </para>
/// </summary>
public class ListingSheetParityFenceTests
{
    private const int TranslatedSheetFloor = 6;
    private const int BulletFloor = 10;
    private const int FeatureFloor = 15;
    private const int ShortDescriptionMax = 200;

    private static readonly Regex Fenced = new(@"```\n(.*?)```", RegexOptions.Singleline);

    private sealed record Sheet(string Name, string Short, string Long, string Features);

    private static IReadOnlyList<Sheet> Sheets()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.GetFiles(dir, "ROROROblox.slnx").Any())
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        var store = Path.Combine(dir!, "docs", "store");
        return Directory.GetFiles(store, "listing-copy*.md")
            .Where(p => Regex.IsMatch(Path.GetFileName(p), @"^listing-copy(-[a-z]{2}(-[a-z]{2})?)?\.md$"))
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p =>
            {
                var text = File.ReadAllText(p).Replace("\r\n", "\n");
                var name = Path.GetFileName(p);
                return new Sheet(name,
                    BlockAfter(text, "## Short description", name),
                    BlockAfter(text, "## Long description", name),
                    BlockAfter(text, "## Product features", name));
            })
            .ToList();
    }

    private static string BlockAfter(string text, string prefix, string name)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith(prefix, StringComparison.Ordinal)) continue;
            var m = Fenced.Match(string.Join("\n", lines.Skip(i + 1)));
            Assert.True(m.Success, $"{name}: no fenced block under '{prefix}'");
            return m.Groups[1].Value.Trim();
        }

        Assert.Fail($"{name}: heading '{prefix}' not found");
        return "";
    }

    private static int Bullets(string block) => block.Split('\n').Count(l => l.TrimStart().StartsWith("• ", StringComparison.Ordinal));

    private static int FeatureLines(string block) => block.Split('\n').Count(l => l.Trim().Length > 0);

    [Fact]
    public void EveryTranslatedSheetIsFound()
    {
        var sheets = Sheets();
        Assert.Contains(sheets, s => s.Name == "listing-copy.md");
        Assert.True(sheets.Count - 1 >= TranslatedSheetFloor,
            $"Found {sheets.Count - 1} translated sheets, under the floor of {TranslatedSheetFloor}.");
    }

    [Fact]
    public void TheLongDescriptionHasTheSameBulletCountInEverySheet()
    {
        var sheets = Sheets();
        var english = Bullets(sheets.Single(s => s.Name == "listing-copy.md").Long);
        Assert.True(english >= BulletFloor, $"The English long description has {english} bullets, under the floor of {BulletFloor}.");
        var off = sheets.Where(s => Bullets(s.Long) != english).Select(s => $"{s.Name}: {Bullets(s.Long)}").ToList();
        Assert.True(off.Count == 0, $"English has {english} long-description bullets; these differ:\n" + string.Join("\n", off));
    }

    [Fact]
    public void TheFeaturesListHasTheSameLineCountInEverySheet()
    {
        var sheets = Sheets();
        var english = FeatureLines(sheets.Single(s => s.Name == "listing-copy.md").Features);
        Assert.True(english >= FeatureFloor, $"The English features list has {english} lines, under the floor of {FeatureFloor}.");
        var off = sheets.Where(s => FeatureLines(s.Features) != english).Select(s => $"{s.Name}: {FeatureLines(s.Features)}").ToList();
        Assert.True(off.Count == 0, $"English has {english} feature lines; these differ:\n" + string.Join("\n", off));
    }

    [Fact]
    public void EveryShortDescriptionFitsTheStoreLimit()
    {
        var off = Sheets()
            .Select(s => (s.Name, Length: s.Short.EnumerateRunes().Count()))
            .Where(x => x.Length > ShortDescriptionMax)
            .Select(x => $"{x.Name}: {x.Length}")
            .ToList();
        Assert.True(off.Count == 0, $"Short descriptions over {ShortDescriptionMax} characters:\n" + string.Join("\n", off));
    }
}
