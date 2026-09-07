using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ROROROblox.Tests;

/// <summary>
/// The neutral UI string catalog (Strings.resx), for the copy fences.
///
/// <para>Localization (2026-09-06) moved every user-visible XAML literal into
/// <c>Properties/Strings.resx</c> and left an <c>{x:Static loc:Strings.Key}</c> reference in
/// its place. The fences that assert on COPY — button labels, settings hints, tooltips — must
/// therefore resolve those references back to display text before asserting. This is the one
/// shared place that does it: read the resx once, and turn an attribute value (a literal that
/// survived, or an x:Static reference) into the string a user actually sees.</para>
/// </summary>
internal static class ResxCatalog
{
    private static readonly Regex XStaticRef =
        new(@"^\{x:Static\s+\w+:Strings\.([A-Za-z_][A-Za-z0-9_]*)\}$", RegexOptions.Compiled);

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Neutral = new(Load);

    /// <summary>key → neutral (English) value, straight from Strings.resx.</summary>
    public static IReadOnlyDictionary<string, string> Entries => Neutral.Value;

    /// <summary>
    /// Resolve a XAML attribute value to the display text a user sees: an
    /// <c>{x:Static loc:Strings.Key}</c> reference becomes its catalog value; a surviving
    /// literal is returned unchanged; a binding or other markup extension returns null (there
    /// is no static display text to assert on). null in → null out.
    /// </summary>
    public static string? Resolve(string? attrValue)
    {
        if (attrValue is null) return null;
        var trimmed = attrValue.Trim();
        var m = XStaticRef.Match(trimmed);
        if (m.Success)
        {
            return Entries.TryGetValue(m.Groups[1].Value, out var v) ? v : null;
        }
        // Any other markup extension ({Binding …}, {DynamicResource …}) has no static text.
        if (trimmed.StartsWith('{') && !trimmed.StartsWith("{}")) return null;
        return trimmed.StartsWith("{}") ? trimmed[2..] : attrValue;
    }

    /// <summary>True when the value is an x:Static reference into the Strings catalog.</summary>
    public static bool IsLocalizedRef(string? attrValue) =>
        attrValue is not null && XStaticRef.IsMatch(attrValue.Trim());

    private static IReadOnlyDictionary<string, string> Load()
    {
        var root = XamlStyleScanner.FindRepoRoot()
            ?? throw new InvalidOperationException("Could not locate ROROROblox.slnx to load Strings.resx.");
        var path = Path.Combine(root, "src", "ROROROblox.App", "Properties", "Strings.resx");
        return XDocument.Load(path).Root!
            .Elements("data")
            .Where(d => d.Attribute("name") is not null)
            .ToDictionary(
                d => (string)d.Attribute("name")!,
                d => (string?)d.Element("value") ?? string.Empty,
                StringComparer.Ordinal);
    }
}
