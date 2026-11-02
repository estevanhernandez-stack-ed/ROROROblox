using System.Text.RegularExpressions;

namespace ROROROblox.Tests;

/// <summary>
/// v1.36 item 6 (spec 2.1, 11). Every element that carries <c>FactBadgeStyle</c> must name itself
/// with its value (<c>AutomationProperties.Name</c>), because a facts-line badge is a Border around
/// a TextBlock and <see cref="AccessibleNamingFenceTests"/> scans neither: it looks at interactive
/// controls only. Without this fence a sixth badge could ship announcing nothing, or announcing its
/// bare text ("1,240") with no hint what the number is.
/// <para>
/// The floor of five is the five facts on the row: Robux, membership, friends online, last played,
/// the note's first line. Membership counts even though it is collapsed until the probes run: it is
/// built, styled and named, and the fence should notice if a later edit drops its name.
/// </para>
/// </summary>
public class FactBadgeNamingFenceTests
{
    private const int BadgeFloor = 5;

    private static readonly Regex Element = new(
        @"<(?<tag>[A-Za-z:]+)(?=[\s/>])(?<attrs>[^>]*?FactBadgeStyle[^>]*?)/?>",
        RegexOptions.Singleline);

    private static (int Badges, List<string> Unnamed) Scan()
    {
        var badges = 0;
        var unnamed = new List<string>();
        foreach (var file in XamlStyleScanner.EnumerateAppXamlFiles())
        {
            var text = File.ReadAllText(file.FullPath);
            foreach (Match m in Element.Matches(text))
            {
                var attrs = m.Groups["attrs"].Value;

                // The style's own definition (x:Key="FactBadgeStyle") is not a use of it.
                if (attrs.Contains("x:Key=\"FactBadgeStyle\"", StringComparison.Ordinal)) continue;

                badges++;
                var named = Regex.IsMatch(attrs, @"AutomationProperties\.Name\s*=\s*""[^""]+""");
                if (!named)
                {
                    var line = text[..m.Index].Count(c => c == '\n') + 1;
                    unnamed.Add($"{file.Label}:{line} <{m.Groups["tag"].Value}>");
                }
            }
        }

        return (badges, unnamed);
    }

    [Fact]
    public void EveryFactBadge_NamesItselfWithItsValue()
    {
        var (_, unnamed) = Scan();

        Assert.True(unnamed.Count == 0,
            "These FactBadgeStyle elements set no AutomationProperties.Name, so a screen reader reads "
            + "the bare value with no hint what it is: " + string.Join("; ", unnamed));
    }

    [Fact]
    public void TheScanFindsTheFiveBadges_SoAMissingNameCannotPassVacuously()
    {
        var (badges, _) = Scan();

        Assert.True(badges >= BadgeFloor,
            $"Found {badges} FactBadgeStyle elements; the facts line has {BadgeFloor}. "
            + "Either a badge lost its style or the scan stopped matching.");
    }

    [Fact]
    public void TheBadgeNamesAreBoundToValueBearingProperties_NotLiterals()
    {
        // A literal name ("Robux") would name the badge and drop the value. The five names are
        // bindings to the summary's *AccessibleName properties, which carry the value.
        var named = XamlStyleScanner.EnumerateAppXamlFiles()
            .SelectMany(f => Element.Matches(File.ReadAllText(f.FullPath)).Select(m => m.Groups["attrs"].Value))
            .Where(a => !a.Contains("x:Key=", StringComparison.Ordinal))
            .ToList();

        Assert.All(named, attrs => Assert.Matches(@"AutomationProperties\.Name\s*=\s*""\{Binding \w+AccessibleName\}""", attrs));
    }
}
