using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace ROROROblox.Core.KnownIssues;

/// <summary>
/// The two spellings of a Roblox version this feature meets.
/// <para>
/// <b>Feed-authored</b> (<c>0.740</c>, <c>0.740.0.7400927</c>): two to four dot-separated numbers with
/// a three-digit second number, which is how Roblox numbers its builds. <c>0.74</c> is refused because
/// <see cref="Version"/> reads it as minor 74 — a different number from 0.740, and an entry written that
/// way would silently match the wrong builds.
/// </para>
/// <para>
/// <b>Installed</b>: <c>RobloxPlayerBeta.exe</c> reports its <c>FileVersion</c> as
/// <c>0, 740, 0, 7400927</c>. <see cref="Version.TryParse(string?, out Version?)"/> rejects that form, so
/// the spaces go and the commas become dots first. (The same raw string reaches
/// <c>RobloxCompatChecker.CheckAsync</c>, which is why its drift banner cannot fire — recorded in the spec,
/// deliberately not fixed here.)
/// </para>
/// </summary>
public static class RobloxVersion
{
    private static readonly Regex FeedForm = new(@"^\d+\.\d{3}(\.\d+){0,2}$", RegexOptions.CultureInvariant);

    public static bool TryParseFeed(string? text, [NotNullWhen(true)] out Version? version)
    {
        version = null;
        var trimmed = text?.Trim();
        return trimmed is not null && FeedForm.IsMatch(trimmed) && Version.TryParse(trimmed, out version);
    }

    public static bool TryParseInstalled(string? text, [NotNullWhen(true)] out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalised = text.Replace(" ", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        return Version.TryParse(normalised, out version);
    }
}
