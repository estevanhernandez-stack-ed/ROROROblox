using System.Globalization;

namespace ROROROblox.Core.Theming;

/// <summary>
/// Guarantees an interactive boundary stays visible against the surface behind it, whatever the
/// theme says.
/// <para>
/// WHY THIS EXISTS. Measured from the built-in themes on 2026-08-05: a secondary button's edge
/// reaches 1.26:1 in <c>brand</c>, 1.16:1 in <c>midnight</c>, 1.14:1 in <c>magenta-heat</c>, against
/// WCAG 1.4.11's 3:1 floor for a non-text interactive boundary. All three set <c>Navy == Bg</c>, so
/// the button's fill contributes exactly zero separation and the whole affordance rests on that
/// hairline. It ships in the default theme today.
/// </para>
/// <para>
/// WHY DERIVED RATHER THAN A NEW SLOT. <c>Theme</c> has exactly ten required slots and every user
/// theme on disk supplies all ten, so an eleventh would break them all unless it defaulted — and a
/// default would not be enough anyway, because all three built-ins set <c>Navy == Bg</c>, which
/// reads to a theme author as the intended pattern. A new token would inherit the same collapse the
/// moment someone copied the shape of a built-in. Deriving the boundary from whatever the theme
/// supplies means there is no value a theme can set that defeats it, and every existing user theme
/// is fixed on next launch without its author touching a thing.
/// </para>
/// </summary>
public static class ContrastGuard
{
    /// <summary>WCAG 1.4.11 — non-text contrast for UI components and graphical objects.</summary>
    public const double MinimumBoundaryRatio = 3.0;

    /// <summary>
    /// Returns <paramref name="candidate"/> when it already clears <see cref="MinimumBoundaryRatio"/>
    /// against <paramref name="surface"/>; otherwise nudges it away from the surface until it does.
    /// <para>
    /// Direction is chosen by the surface, not the candidate: a dark field pushes the boundary
    /// toward white, a light field toward black. Picking by the candidate would fail exactly where
    /// it matters, since the candidate is usually near-identical to the surface — the situation this
    /// exists to fix.
    /// </para>
    /// </summary>
    /// <returns>An <c>#RRGGBB</c> string. Input that cannot be parsed is returned unchanged, so a
    /// malformed theme degrades to today's behaviour rather than throwing on a render path.</returns>
    public static string Ensure(string? surface, string? candidate)
    {
        if (!TryParse(surface, out var bg) || !TryParse(candidate, out var fg)) return candidate ?? "";

        if (Ratio(bg, fg) >= MinimumBoundaryRatio) return candidate!;

        // Toward white on a dark field, toward black on a light one. 0.179 is the luminance at which
        // white and black are equally readable against a surface — the standard crossover.
        var target = Luminance(bg) > 0.179 ? (0.0, 0.0, 0.0) : (1.0, 1.0, 1.0);

        // Walk the blend rather than solving it: the relationship between blend fraction and
        // contrast ratio is monotonic here but not linear, and 100 steps lands within 1% of the
        // minimum needed. Overshooting would make every secondary control louder than the primary.
        for (var step = 1; step <= 100; step++)
        {
            var t = step / 100.0;
            var mixed = (
                fg.R + (target.Item1 - fg.R) * t,
                fg.G + (target.Item2 - fg.G) * t,
                fg.B + (target.Item3 - fg.B) * t);

            if (Ratio(bg, mixed) >= MinimumBoundaryRatio) return ToHex(mixed);
        }

        // Unreachable for any real surface — pure white and pure black cannot both fail 3:1 against
        // the same colour — but returning the extreme beats returning something that still fails.
        return ToHex(target);
    }

    /// <summary>Contrast ratio between two <c>#RRGGBB</c> strings, or null if either will not parse.</summary>
    public static double? RatioBetween(string? a, string? b) =>
        TryParse(a, out var x) && TryParse(b, out var y) ? Ratio(x, y) : null;

    private static double Ratio((double R, double G, double B) a, (double R, double G, double B) b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        var (hi, lo) = la > lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double Luminance((double R, double G, double B) c) =>
        0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    private static double Linear(double channel) =>
        channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

    private static bool TryParse(string? hex, out (double R, double G, double B) rgb)
    {
        rgb = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;

        var s = hex.Trim().TrimStart('#');

        // #AARRGGBB is accepted because theme JSON in the wild carries both; alpha is dropped, since
        // a boundary's contrast is decided by what actually lands on screen and we do not know what
        // is behind a translucent one.
        if (s.Length == 8) s = s[2..];
        if (s.Length != 6) return false;

        if (!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)) return false;

        rgb = (Convert.ToInt32(s[..2], 16) / 255.0,
               Convert.ToInt32(s[2..4], 16) / 255.0,
               Convert.ToInt32(s[4..], 16) / 255.0);
        return true;
    }

    private static string ToHex((double R, double G, double B) c) =>
        $"#{Byte(c.R):X2}{Byte(c.G):X2}{Byte(c.B):X2}";

    private static int Byte(double channel) => Math.Clamp((int)Math.Round(channel * 255), 0, 255);
}
