using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ROROROblox.Tests;

/// <summary>
/// The window-title rule, made enforceable (F-004, F-005).
/// <para>
/// The app had seven competing title conventions across 25 windows, because no rule was ever
/// written down. The most common invention — prefixing the product name — is goal 4's complaint
/// literally: Windows already prints the app name in the taskbar and Alt-Tab, so repeating it
/// inside the title spends the most valuable words in the window on something the OS said first.
/// </para>
/// <para>
/// THE RULE. The title bar names the destination and nothing else. Two windows are exempt, because
/// the product IS their subject: the main window, and About/Welcome.
/// </para>
/// <para>
/// A convention nobody can check is a convention that rots — this one already had, in a way no
/// reviewer caught for months: <c>FriendFollowWindow</c> built its title in code out of the REPO
/// name, so the one window assembling a title at runtime shipped "ROROROblox" to users. That is
/// what the second test exists to prevent recurring.
/// </para>
/// </summary>
public class WindowTitleConventionTests
{
    /// <summary>
    /// Windows whose subject genuinely is the product. MainWindow is the app itself; About and
    /// Welcome are about the app. Everywhere else, the product name is noise the OS already showed.
    /// </summary>
    private static readonly string[] ProductNameAllowed =
    [
        "MainWindow.xaml",
        "AboutWindow.xaml",
        "WelcomeWindow.xaml",
    ];

    [Fact]
    public void NoWindowTitleRepeatsTheProductName_ExceptTheOnesAboutTheProduct()
    {
        var offenders = new List<string>();

        foreach (var file in XamlStyleScanner.EnumerateAppXamlFiles())
        {
            var name = Path.GetFileName(file.FullPath);
            if (ProductNameAllowed.Contains(name)) continue;

            foreach (var title in TitlesIn(file.FullPath))
            {
                if (title.Contains("RoRoRo", StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add($"{file.Label}: Title=\"{title}\"");
                }
            }
        }

        // F-098: this rule read XAML only, while its sibling below read XAML AND code-behind. The
        // instrument already contained the fix and applied it to one of its two assertions — so a
        // Title = "RoRoRo — Settings" written in code-behind passed the primary rule of the whole
        // convention. The exempt list is by file name, and a code-behind file is <Window>.xaml.cs,
        // so the same three exemptions resolve for both halves.
        foreach (var (label, literal) in RuntimeTitleAssignments())
        {
            if (ProductNameAllowed.Any(a => label.Equals(a + ".cs", StringComparison.OrdinalIgnoreCase))) continue;
            if (literal.Contains("RoRoRo", StringComparison.OrdinalIgnoreCase))
            {
                offenders.Add($"{label}: Title = {literal}");
            }
        }

        Assert.True(offenders.Count == 0,
            "The title bar names the destination, not the product — Windows already shows the app "
            + "name in the taskbar and Alt-Tab:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void NoUserFacingTitleUsesTheRepoName()
    {
        // ROROROblox is a code identifier. The user-facing brand is RoRoRo — including in a title
        // assembled at runtime, which is exactly where it went unnoticed.
        var offenders = new List<string>();

        foreach (var file in XamlStyleScanner.EnumerateAppXamlFiles())
        {
            foreach (var title in TitlesIn(file.FullPath))
            {
                if (title.Contains("ROROROblox", StringComparison.Ordinal))
                {
                    offenders.Add($"{file.Label}: Title=\"{title}\"");
                }
            }
        }

        foreach (var (label, literal) in RuntimeTitleAssignments())
        {
            if (literal.Contains("ROROROblox", StringComparison.Ordinal))
            {
                offenders.Add($"{label}: Title = {literal}");
            }
        }

        Assert.True(offenders.Count == 0,
            "ROROROblox is the repo name; the user-facing brand is RoRoRo:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TheScanSeesTheWindowsItClaimsTo()
    {
        // Both tests above pass trivially if the enumeration finds nothing. The app has ~25 windows
        // carrying a Title; anything far below that means the scan broke, not that the app got tidy.
        var titled = XamlStyleScanner.EnumerateAppXamlFiles().Count(f => TitlesIn(f.FullPath).Any());

        Assert.True(titled >= 20, $"Expected to find the app's titled windows, found {titled}.");
    }

    /// <summary>Title attributes on a XAML file's root element (Window, ui:FluentWindow, …).</summary>
    private static IEnumerable<string> TitlesIn(string path)
    {
        XDocument doc;
        try { doc = XDocument.Load(path); }
        catch (System.Xml.XmlException) { yield break; }

        var root = doc.Root;
        if (root is null) yield break;

        // Only the window's own Title. A Title= on a nested element is some other control's property.
        var title = root.Attribute("Title")?.Value;
        if (!string.IsNullOrWhiteSpace(title)) yield return title;
    }

    /// <summary>
    /// <c>Title = "…"</c> / <c>Title = $"…"</c> assignments in code-behind — the ones no XAML sweep
    /// would ever see.
    /// <para>
    /// <b>Known limit, recorded rather than fixed (F-098).</b> This captures the LITERAL, so for
    /// <c>Title = $"Friends — {ChromeName(current)}"</c> it reads the interpolation source and not
    /// the string a user sees. <c>ChromeName</c> returns a streamer alias or an account display
    /// name, so nothing leaks today — but that is the shape of the very bug this method exists to
    /// prevent, one level down. Resolving it means executing the app, which this gate deliberately
    /// does not do; the honest move is to say so here rather than imply coverage it lacks.
    /// </para>
    /// <para>
    /// It also matches <c>Title =</c> on <c>SaveFileDialog</c> and <c>OpenFileDialog</c>, not only
    /// on Windows. That is deliberate — those captions are user-facing chrome and the naming rule
    /// applies to them the same way.
    /// </para>
    /// </summary>
    private static IEnumerable<(string Label, string Literal)> RuntimeTitleAssignments()
    {
        var appDir = XamlStyleScanner.AppSourceDirectory();
        if (appDir is null) yield break;

        foreach (var path in Directory.EnumerateFiles(appDir, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;

            foreach (Match m in Regex.Matches(File.ReadAllText(path), """Title\s*=\s*(\$?"[^"]*")"""))
            {
                yield return (Path.GetFileName(path), m.Groups[1].Value);
            }
        }
    }
}
