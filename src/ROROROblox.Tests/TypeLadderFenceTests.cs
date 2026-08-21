using System.Text.RegularExpressions;

namespace ROROROblox.Tests;

/// <summary>
/// F-057 and F-044 — the type ladder, made enforceable.
/// <para>
/// Before this the app had <b>eleven distinct font sizes across 326 declarations and no size
/// resource of any kind</b>. That is cause and effect, not coincidence: with nothing to inherit
/// from, every new control picked a number that looked right beside its neighbour, and eleven is
/// where that lands. Four steps now live in <c>ControlStyles.xaml</c> — Display 22, Heading 14, Body 12,
/// Meta 11 — with roles rather than just numbers, because a ladder whose steps mean nothing is
/// just a shorter list of magic numbers.
/// </para>
/// <para>
/// TWO RULES, and the floor is the one that matters to a reader. Nothing may be smaller than the
/// meta step: 38 declarations sat at 8, 9 and 10px, including two 8px pills, and F-044 recorded
/// that as "six places". The second rule caps how many raw sizes may remain, so the migration can
/// only go one way.
/// </para>
/// <para>
/// WHAT THIS DOES NOT CLAIM. 163 declarations sit at 11px and most are prose that belongs at Body.
/// Sorting them is per-site judgement across 22 files and is tracked as F-118, so the ceiling below
/// is deliberately set at today's measured number rather than at four. It ratchets down as that row
/// is worked; it cannot ratchet up.
/// </para>
/// </summary>
public class TypeLadderFenceTests
{
    private static readonly Regex FontSizeLiteral = new(@"FontSize=""(\d+(?:\.\d+)?)""", RegexOptions.Compiled);

    /// <summary>The meta step, and the floor for everything. Mirrors <c>MetaFontSize</c> in ControlStyles.xaml.</summary>
    private const double MetaStep = 11;

    /// <summary>
    /// Distinct raw sizes still typed into markup. **3** as measured on 2026-08-21 — 18, 20 and 24,
    /// all display-tier and all in hero chrome. Was 11 before the ladder, 8 after the floor raise,
    /// 3 once F-118 migrated the prose. A CEILING: a rise means somebody invented a step instead of
    /// using one.
    /// <para>
    /// The three that remain are a design decision, not an oversight: 18, 20 and 24 sit between
    /// Heading 14 and Display 22 in About's and Welcome's hero blocks, where the exact value carries
    /// brand identity rather than a role. Collapsing them is a judgement about the mark, which is
    /// not something a type sweep gets to make. Recorded in F-118 rather than forced.
    /// </para>
    /// </summary>
    private const int DistinctRawSizeCeiling = 3;

    /// <summary>
    /// Declarations that reference a ladder step rather than a number. 304 of 320 as measured on
    /// 2026-08-21. A FLOOR: it may rise and must not fall, so a migrated site cannot quietly go
    /// back to a literal — which is the direction this whole row drifted in for years.
    /// </summary>
    private const int LadderReferenceFloor = 300;

    [Fact]
    public void NothingIsSmallerThanTheMetaStep()
    {
        var offenders = new List<string>();

        foreach (var (label, size, line) in Literals())
        {
            if (size < MetaStep)
            {
                offenders.Add($"{label}:{line} FontSize=\"{size:0.##}\"");
            }
        }

        Assert.True(offenders.Count == 0,
            $"Text below the {MetaStep}px meta step. The smallest step is for short, uppercase, "
            + "tracked labels — it is not a smaller body size, and there is nothing under it. Use "
            + "{DynamicResource MetaFontSize}, or its larger siblings for anything a "
            + "person reads as a sentence (F-044):" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TheNumberOfRawSizesOnlyEverFalls()
    {
        var distinct = Literals().Select(x => x.Size).Distinct().OrderBy(x => x).ToList();

        // Vacuity guard. It counts EVERY FontSize declaration, literal or resource-bound, because
        // counting literals stopped proving anything the moment literals became the exception —
        // sixteen is now a healthy number and would have read as a broken walk.
        var allDeclarations = AllFontSizeAttributes();
        Assert.True(allDeclarations >= 250,
            $"Found only {allDeclarations} FontSize declarations of any kind. That is the scan breaking.");

        Assert.True(distinct.Count <= DistinctRawSizeCeiling,
            $"The app now uses {distinct.Count} raw font sizes ({string.Join(", ", distinct)}), up "
            + $"from the {DistinctRawSizeCeiling} measured on 2026-08-21. A new number means a step "
            + "was invented rather than used — the ladder is Display 22 / Heading 14 / Body 12 / "
            + "Meta 11 in ControlStyles.xaml. If a step genuinely needs adding, add it there with its role and "
            + "move this constant deliberately.");
    }

    [Fact]
    public void TheLadderItselfIsDeclared()
    {
        // The rules above are meaningless if the resources they point at do not exist — a reader
        // told to "use MetaFontSize" needs MetaFontSize to resolve.
        // ControlStyles.xaml, NOT App.xaml. The ladder was written into App.xaml first and arm64 CI
        // caught it: App.xaml is the Application, not a mergeable dictionary, so a render gate
        // parsing a real subtree against ThemesDictionary + ControlsDictionary + ControlStyles.xaml
        // could not resolve the keys and threw. A shared token has to live where consumers merge.
        var dictionary = XamlStyleScanner.EnumerateAppXamlFiles()
            .First(f => f.FullPath.EndsWith("ControlStyles.xaml", StringComparison.OrdinalIgnoreCase));
        var text = File.ReadAllText(dictionary.FullPath);

        foreach (var key in new[] { "DisplayFontSize", "HeadingFontSize", "BodyFontSize", "MetaFontSize" })
        {
            Assert.Contains($"x:Key=\"{key}\"", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheLadderIsActuallyUsed()
    {
        // The point of the other two rules is that the ladder REPLACED the numbers. Without this,
        // an app could satisfy both by having three literals and no ladder references at all.
        var references = AllFontSizeAttributes() - Literals().Count();

        Assert.True(references >= LadderReferenceFloor,
            $"Only {references} FontSize declarations reference a ladder step, below the "
            + $"{LadderReferenceFloor} floor. A site that went back to a literal is the drift this "
            + "row spent four waves undoing — 326 declarations in 11 sizes is what happens when "
            + "there is nothing to inherit from.");
    }

    [Fact]
    public void NoFontFamilyIsSpelledOutInMarkup()
    {
        // F-067, the same defect one axis over. The mono role was declared two ways — the full
        // fallback stack at 18 sites and a bare "Consolas" at 7 — so seven meta labels skipped
        // JetBrains Mono wherever it was installed. It survived because on a machine WITHOUT
        // JetBrains Mono both recipes fall through to Consolas and render identically: the defect
        // is invisible precisely where it is being written, which no amount of care catches.
        var offenders = new List<string>();

        foreach (var file in XamlStyleScanner.EnumerateAppXamlFiles())
        {
            var lines = File.ReadAllLines(file.FullPath);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match m in Regex.Matches(lines[i], @"FontFamily=""([^""{][^""]*)"""))
                {
                    offenders.Add($"{file.Label}:{i + 1} FontFamily=\"{m.Groups[1].Value}\"");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "A font family spelled out in markup is a second declaration of a role that already has "
            + "one. Use {DynamicResource MonoFontFamily} or {DynamicResource DisplayFontFamily} — "
            + "and if a genuinely new role is needed, declare it in ControlStyles.xaml beside the "
            + "others so the next site inherits it instead of inventing a stack:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void BothFamilyRolesAreDeclared()
    {
        var dictionary = XamlStyleScanner.EnumerateAppXamlFiles()
            .First(f => f.FullPath.EndsWith("ControlStyles.xaml", StringComparison.OrdinalIgnoreCase));
        var text = File.ReadAllText(dictionary.FullPath);

        foreach (var key in new[] { "MonoFontFamily", "DisplayFontFamily" })
        {
            Assert.Contains($"x:Key=\"{key}\"", text, StringComparison.Ordinal);
        }
    }

    /// <summary>Every <c>FontSize=</c> attribute, whether it holds a number or a resource reference.</summary>
    private static int AllFontSizeAttributes() =>
        XamlStyleScanner.EnumerateAppXamlFiles()
            .Sum(f => Regex.Matches(File.ReadAllText(f.FullPath), @"FontSize=""").Count);

    private static IEnumerable<(string Label, double Size, int Line)> Literals()
    {
        foreach (var file in XamlStyleScanner.EnumerateAppXamlFiles())
        {
            var lines = File.ReadAllLines(file.FullPath);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match m in FontSizeLiteral.Matches(lines[i]))
                {
                    if (double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var size))
                    {
                        yield return (file.Label, size, i + 1);
                    }
                }
            }
        }
    }
}
