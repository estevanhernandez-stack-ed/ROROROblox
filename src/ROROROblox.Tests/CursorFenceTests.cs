using System.IO;
using System.Text.RegularExpressions;

namespace ROROROblox.Tests;

/// <summary>
/// v1.36 item 11 (spec 7.2, the fence half of #260): the cursor stays visible. The 2026-11-01 sweep found no
/// hidden or custom cursor anywhere in the app, so the reporter's missing cursor is environmental until the
/// evidence says otherwise. This keeps the sweep's finding true: no <c>None</c>, no override, no Win32 cursor
/// call, no custom cursor file, and every cursor the app does set is one of the stock ones the sweep found.
/// <para>
/// Scans <c>src/ROROROblox.App</c> XAML and C# as text. <c>CursorSitesFloor</c> is the vacuity floor: a
/// regex that silently stopped matching would otherwise pass on zero sites.
/// </para>
/// </summary>
public class CursorFenceTests
{
    /// <summary>22 sites on 2026-11-03 (spec counted 18 at 2026-11-01; the Library page and panel added more).</summary>
    private const int CursorSitesFloor = 15;

    private static readonly string[] Allowed = ["Hand", "SizeAll", "Arrow"];

    private static readonly (Regex Pattern, string Why)[] Forbidden =
    [
        (new Regex(@"Cursors\.None\b"), "hides the cursor"),
        (new Regex(@"Cursor\s*=\s*""None"""), "hides the cursor"),
        (new Regex(@"\bOverrideCursor\b"), "overrides every cursor in the app"),
        (new Regex(@"\bShowCursor\s*\("), "Win32 cursor visibility call"),
        (new Regex(@"\bSetCursor\s*\("), "Win32 cursor call"),
        (new Regex(@"\.cur"""), "custom cursor file"),
        (new Regex(@"\.ani"""), "custom animated cursor file"),
    ];

    private static readonly Regex XamlSite = new(@"\bCursor\s*=\s*""(?<v>[^""]*)""");
    private static readonly Regex CodeSite = new(@"\bCursors\.(?<v>[A-Za-z]+)");
    private static readonly Regex NullSite = new(@"\bCursor\s*=\s*null\b");

    private static string AppRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.GetFiles(dir, "ROROROblox.slnx").Any())
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!, "src", "ROROROblox.App");
    }

    private static IEnumerable<(string Label, string Line)> SourceLines()
    {
        var root = AppRoot();
        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(f =>
            {
                var rel = Path.GetRelativePath(root, f);
                return !rel.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !rel.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            });

        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(root, file);
            var n = 0;
            foreach (var line in File.ReadLines(file))
            {
                n++;
                yield return ($"{rel}:{n}", line);
            }
        }
    }

    [Fact]
    public void NoSourceHidesOverridesOrReplacesTheCursor()
    {
        var offenders = new List<string>();
        foreach (var (label, line) in SourceLines())
        {
            foreach (var (pattern, why) in Forbidden)
            {
                if (pattern.IsMatch(line)) offenders.Add($"{label} ({why}): {line.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0, "Cursor fence (#260):\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void EveryCursorTheAppSetsIsAStockOne_AndTheScanStillSeesThem()
    {
        var offenders = new List<string>();
        var sites = 0;
        foreach (var (label, line) in SourceLines())
        {
            foreach (Match m in XamlSite.Matches(line).Concat(CodeSite.Matches(line)))
            {
                sites++;
                var value = m.Groups["v"].Value;
                if (!Allowed.Contains(value, StringComparer.Ordinal)) offenders.Add($"{label}: {value}");
            }

            sites += NullSite.Matches(line).Count;
        }

        Assert.True(offenders.Count == 0,
            $"Only {string.Join(", ", Allowed)} or null may be set as a cursor (#260):\n" + string.Join("\n", offenders));
        Assert.True(sites >= CursorSitesFloor,
            $"The scan saw {sites} cursor sites, under the floor of {CursorSitesFloor}; the patterns have probably stopped matching.");
    }
}
