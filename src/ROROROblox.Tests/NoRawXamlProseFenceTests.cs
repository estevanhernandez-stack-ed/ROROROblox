using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ROROROblox.Tests;

/// <summary>
/// No user-facing string in App XAML is raw English (localization Phase D step 3 batch 15, the
/// closing guard). Phase C moved the static markup onto <c>{loc:Loc Key}</c>, and the batches after
/// it swept the code-behind; this fence keeps a new raw literal from creeping back into the markup
/// where the eye slides past it — a <c>Text="Launch selected"</c> in a fresh DataTemplate renders in
/// English for every one of the six translated cultures and nothing else notices.
/// <para>
/// It reads the markup with <see cref="XDocument"/>, not a regex, so a glyph entity
/// (<c>&amp;#x25BE;</c>) arrives already decoded to a single non-letter character and never trips the
/// prose test. Two carve-outs match the ones the codebase already makes elsewhere: a markup
/// extension (<c>{loc:Loc …}</c>, <c>{Binding …}</c>, <c>{DynamicResource …}</c>) is not a literal,
/// and a URL or bare domain is an identifier, not copy — the same reasoning
/// <see cref="BrandNameFenceTests"/> uses for the About page's repository link and the cookie
/// sheet's <c>roblox.com</c>.
/// </para>
/// </summary>
public class NoRawXamlProseFenceTests
{
    /// <summary>Attributes a person reads. Exact <c>LocalName</c> match — <c>SizeToContent</c> is
    /// layout and <c>InputGestureText</c> is a keyboard-shortcut hint, so neither is here.</summary>
    private static readonly HashSet<string> ProseAttributes =
        new(StringComparer.Ordinal) { "Text", "Header", "Content", "ToolTip", "Title", "PlaceholderText" };

    /// <summary>Inline text a person reads sits inside one of these. <c>FontFamily</c> (font stacks)
    /// is deliberately absent, and XML comments are <see cref="XComment"/> nodes the text walk never
    /// visits — so neither a font stack nor a commented-out block reads as UI copy.</summary>
    private static readonly HashSet<string> TextElements =
        new(StringComparer.Ordinal)
        {
            "TextBlock", "Run", "Hyperlink", "Bold", "Italic", "Underline", "Span", "Paragraph",
            "Label", "Button", "CheckBox", "RadioButton", "TextBox",
        };

    /// <summary>Two consecutive ASCII letters is the prose bar. Glyphs, punctuation (" -- "),
    /// numbers, and single symbols all fall below it once XDocument has decoded the value.</summary>
    private static readonly Regex TwoLetters = new("[A-Za-z]{2}", RegexOptions.Compiled);

    /// <summary>A URL or bare domain is an identifier, not translatable copy.</summary>
    private static readonly Regex UrlOrDomain =
        new(@"://|[A-Za-z0-9-]+\.(?:com|org|net|io|dev|gg|co)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Intentional raw English, each with a reason. Empty today — the URL/domain and
    /// markup-extension carve-outs cover every current case; an exemption you have to type is one
    /// somebody has to justify.</summary>
    private static readonly string[] ExemptLiterals = [];

    [Fact]
    public void NoUserFacingXamlStringIsRawEnglish()
    {
        var files = XamlStyleScanner.EnumerateAppXamlFiles().ToList();
        Assert.True(files.Count >= 20, $"Expected to scan the app's XAML, found {files.Count}. The repo-root walk probably failed.");

        var offenders = new List<string>();
        var scanned = 0;

        foreach (var file in files)
        {
            XDocument doc;
            try { doc = XDocument.Load(file.FullPath); }
            catch (System.Xml.XmlException) { continue; }

            foreach (var el in doc.Descendants())
            {
                foreach (var attr in el.Attributes())
                {
                    if (!ProseAttributes.Contains(attr.Name.LocalName)) continue;
                    scanned++;
                    if (IsRawProse(attr.Value))
                        offenders.Add($"{file.Label}: {attr.Name.LocalName}=\"{attr.Value}\"");
                }

                if (!TextElements.Contains(el.Name.LocalName)) continue;
                foreach (var node in el.Nodes().OfType<XText>())
                {
                    var text = node.Value.Trim();
                    if (text.Length == 0) continue;
                    scanned++;
                    if (IsRawProse(text))
                        offenders.Add($"{file.Label}: <{el.Name.LocalName}>{text}</{el.Name.LocalName}>");
                }
            }
        }

        // Vacuity floor: hundreds of Text=/Header= sinks exist across the app's ~33 XAML files. A
        // count near zero is a broken walk or parse, which would make the assertion below pass on
        // nothing — the exact failure mode this fence exists to prevent.
        Assert.True(scanned >= 50, $"Only {scanned} text sinks scanned — the walk or parse broke, not the markup getting tidy.");

        Assert.True(offenders.Count == 0,
            "Raw English in XAML a person reads — use {loc:Loc Key} (or, if it is genuinely an "
            + "identifier, add it to ExemptLiterals with a reason):" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void Scanner_FlagsRawProse_ButNotMarkupGlyphsOrDomains()
    {
        // Fails-loud direction: real copy must trip it.
        Assert.True(IsRawProse("Launch selected"));
        Assert.True(IsRawProse("Presence is off."));

        // Quiet direction: the shapes that are not translatable copy.
        Assert.False(IsRawProse("{loc:Loc Foo}"));
        Assert.False(IsRawProse("{Binding Name}"));
        Assert.False(IsRawProse("{DynamicResource CyanBrush}"));
        Assert.False(IsRawProse("×"));            // decoded glyph
        Assert.False(IsRawProse(" -- "));         // punctuation
        Assert.False(IsRawProse(" · "));          // punctuation
        Assert.False(IsRawProse("roblox.com"));   // bare domain
        Assert.False(IsRawProse("github.com/estevanhernandez-stack-ed/ROROROblox"));
        Assert.False(IsRawProse("42"));
        Assert.False(IsRawProse(""));
    }

    private static bool IsRawProse(string value)
    {
        var v = value.Trim();
        if (v.Length == 0) return false;
        if (v.StartsWith("{", StringComparison.Ordinal)) return false;   // markup extension / binding
        if (!TwoLetters.IsMatch(v)) return false;                        // glyphs, punctuation, numbers
        if (UrlOrDomain.IsMatch(v)) return false;                        // a URL or domain is an identifier
        if (ExemptLiterals.Contains(v)) return false;
        return true;
    }
}
