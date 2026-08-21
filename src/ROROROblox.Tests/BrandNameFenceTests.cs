using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ROROROblox.Tests;

/// <summary>
/// F-034 — the repo name reaching a person's eyes.
/// <para>
/// <c>ROROROblox</c> is the repo, the assembly, the namespace, the <c>%LOCALAPPDATA%</c> folder,
/// the single-instance guard name, the drag format and the User-Agent. All correct. It is also,
/// therefore, the string that is always within reach when a developer needs to write the product's
/// name — and it has shipped in front of users four separate times: a runtime-assembled window
/// title (wave 4), the tray menu's "Open ROROROblox", the tray tooltip in all three states, and the
/// support-bundle header. A fifth, the idle-alert toast title, was found only when this row was
/// re-measured on 2026-08-20; the row recorded three remaining sites and there were four.
/// </para>
/// <para>
/// <c>WindowTitleConventionTests.NoUserFacingTitleUsesTheRepoName</c> already covers window titles
/// and only window titles, which is why every one of the sites above survived it. This covers the
/// rest of the surface, in two passes that catch different halves:
/// </para>
/// <list type="bullet">
/// <item>SHAPE — a repo-name literal must look like an identifier, a path or a URL. Prose fails.</item>
/// <item>SINK — a literal reaching a display property or a toast fails even when it IS an
/// identifier, because <c>ShowToast("ROROROblox", …)</c> is shaped exactly like the eleven
/// legitimate uses of the same word as a folder name.</item>
/// </list>
/// </summary>
public class BrandNameFenceTests
{
    private const string RepoName = "ROROROblox";

    /// <summary>String literals, single-line, escapes tolerated. Enough — no repo-name literal in
    /// this codebase spans lines.</summary>
    private static readonly Regex Literal = new("\"(?:[^\"\\\\]|\\\\.)*\"", RegexOptions.Compiled);

    /// <summary>
    /// Display sinks in C#. A literal reaching one of these is read by a person whatever it looks
    /// like, so the shape rule below does not get to excuse it.
    /// </summary>
    private static readonly Regex DisplaySink = new(
        "(?:ShowToast\\s*\\(|ShowMemoryWarning\\s*\\(|\\b(?:Header|Content|Text|ToolTip|ToolTipText|Title)\\s*=\\s*)(\\$?@?\"(?:[^\"\\\\]|\\\\.)*\")",
        RegexOptions.Compiled);

    /// <summary>
    /// The two log lines. Logs are read by us, in a file, while diagnosing — the repo name is the
    /// more useful word there, because it matches the process name and the folder the log sits in.
    /// Listed one at a time rather than pattern-matched on "looks like a log call": an exemption you
    /// have to type is an exemption somebody has to justify.
    /// </summary>
    private static readonly string[] ExemptLiterals =
    [
        "\"ROROROblox starting (v{Version}, OS {Os})\"",
        "\"ROROROblox exiting (code {Code}).\"",
    ];

    [Fact]
    public void EveryRepoNameLiteralIsAnIdentifierAPathOrAUrl()
    {
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var (label, line) in SourceLines())
        {
            foreach (Match m in Literal.Matches(line))
            {
                var literal = m.Value;
                if (!literal.Contains(RepoName, StringComparison.Ordinal)) continue;

                scanned++;
                if (ExemptLiterals.Contains(literal)) continue;
                if (LooksLikeAnIdentifier(literal)) continue;

                offenders.Add($"{label}: {literal}");
            }
        }

        // The floor. The assertion below passes trivially if the walk finds nothing, and this walk
        // climbs the filesystem from the test assembly — a moved output directory would break it
        // silently. 25 sits comfortably under the 33 measured on 2026-08-20 and comfortably over
        // anything a real reduction would reach.
        Assert.True(scanned >= 25,
            $"Expected to find the repo-name literals, found {scanned}. That is the scan breaking, not the code getting tidy.");

        Assert.True(offenders.Count == 0,
            $"{RepoName} is the repo; the product is RoRoRo. These literals read as prose, so use "
            + "Branding.ProductName — or, if this is a log line nobody but us reads, add it to "
            + "ExemptLiterals with a reason:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void NoDisplaySinkIsHandedTheRepoName()
    {
        // The hole the shape rule cannot close. ShowToast("ROROROblox", msg) — the shipped idle
        // alert — is character-for-character identical to the legitimate uses of the same literal
        // as a folder name. Only the destination tells them apart.
        var offenders = new List<string>();
        var sinks = 0;

        foreach (var (label, line) in SourceLines())
        {
            foreach (Match m in DisplaySink.Matches(line))
            {
                sinks++;
                if (m.Groups[1].Value.Contains(RepoName, StringComparison.Ordinal))
                {
                    offenders.Add($"{label}: {m.Value.Trim()}");
                }
            }
        }

        Assert.True(sinks >= 20, $"Expected to find the app's display assignments, found {sinks}.");

        Assert.True(offenders.Count == 0,
            "A person reads these. Use Branding.ProductName:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void NoXamlAttributeAPersonReadsCarriesTheRepoName()
    {
        // Markup is the easier half — x:Class and pack:// URIs legitimately carry the assembly
        // name, and neither is an attribute anybody reads. The one displayed string that contains
        // the repo name on purpose is About's repository link, which is the repository's actual
        // address, so a URL passes here exactly as it does in the shape rule above.
        var offenders = new List<string>();
        string[] displayed = ["Text", "Content", "Header", "ToolTip", "Title"];

        foreach (var file in XamlStyleScanner.EnumerateAppXamlFiles())
        {
            XDocument doc;
            try { doc = XDocument.Load(file.FullPath); }
            catch (System.Xml.XmlException) { continue; }

            foreach (var element in doc.Descendants())
            {
                foreach (var attr in element.Attributes())
                {
                    if (!displayed.Contains(attr.Name.LocalName)) continue;
                    if (!attr.Value.Contains(RepoName, StringComparison.Ordinal)) continue;
                    if (attr.Value.Contains("github.com/", StringComparison.OrdinalIgnoreCase)) continue;

                    offenders.Add($"{file.Label}: {attr.Name.LocalName}=\"{attr.Value}\"");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            $"{RepoName} is the repo; the product is RoRoRo:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// A repo-name literal that is a name rather than a sentence: a URL, an assembly or pack
    /// resource path, the bare folder name, or a dotted/hyphenated identifier built from it.
    /// </summary>
    private static bool LooksLikeAnIdentifier(string literal)
    {
        var inner = literal.Trim('"');

        if (inner.Contains("github.com/", StringComparison.OrdinalIgnoreCase)) return true;
        if (inner.Contains(";component/", StringComparison.Ordinal)) return true;
        if (inner.StartsWith("pack://", StringComparison.Ordinal)) return true;
        if (inner == RepoName) return true;

        // ROROROblox-app-singleton, ROROROblox.AccountSummary, ROROROblox.App.Themes.AGENT_PROMPT.md
        // — one token, no spaces, and the repo name is where it starts.
        return inner.StartsWith(RepoName, StringComparison.Ordinal)
            && inner.Length > RepoName.Length
            && (inner[RepoName.Length] is '-' or '.')
            && !inner.Contains(' ');
    }

    /// <summary>
    /// Every non-comment line of App and Core source. Comment lines are skipped because
    /// <c>&lt;see cref="ROROROblox.App…"/&gt;</c> is how this codebase writes cross-references —
    /// dozens of them — and none of it reaches a user.
    /// </summary>
    private static IEnumerable<(string Label, string Line)> SourceLines()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        if (root is null) yield break;

        foreach (var project in new[] { "ROROROblox.App", "ROROROblox.Core" })
        {
            var dir = Path.Combine(root, "src", project);
            if (!Directory.Exists(dir)) continue;

            foreach (var path in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;

                var label = Path.GetFileName(path);
                foreach (var line in File.ReadLines(path))
                {
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
                    if (trimmed.StartsWith('*')) continue;
                    if (trimmed.StartsWith("/*", StringComparison.Ordinal)) continue;
                    yield return (label, line);
                }
            }
        }
    }
}
