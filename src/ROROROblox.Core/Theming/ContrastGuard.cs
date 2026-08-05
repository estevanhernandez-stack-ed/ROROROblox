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
/// supplies means every existing user theme is fixed on next launch without its author touching a
/// thing.
/// </para>
/// <para>
/// WHAT IT DOES NOT COVER. An earlier version of this comment claimed "there is no value a theme
/// can set that defeats it." The wave-5 review gate refuted that by measurement, so: a
/// <c>divider</c> written as a named colour (<c>Gray</c>) or in <c>sc#</c> form will not parse
/// here, and an unparseable value is left exactly as authored — no fix, and no question either.
/// <c>ThemeStore</c> does no format validation, so those values do reach this code. Closing that
/// would mean a WPF colour dependency in Core; it is a stated gap, not a solved problem.
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
        if (!TryParse(surface, out var bg, out _) || !TryParse(candidate, out var raw, out var alpha))
        {
            return candidate ?? "";
        }

        var fg = Composite(raw, alpha, bg);
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

            // MEASURE WHAT WE RETURN. Snap to bytes BEFORE the check, because ToHex rounds and that
            // rounding can drop the result back under the floor — the un-quantized triple passing is
            // not the same claim as the string we hand back passing. Found by the wave-5 review gate
            // 2026-08-05: over 200k random pairs needing a fix, 9,569 (6.5%) came back below 3:1,
            // e.g. surface #000000 -> #595959 = 2.998:1. All three built-ins cleared it by luck
            // (midnight by 0.019), which is exactly why the tests were green. Snapping first takes
            // that to zero and leaves every built-in byte-identical.
            var snapped = Snap(mixed);
            if (Ratio(bg, snapped) >= MinimumBoundaryRatio) return ToHex(snapped);
        }

        // Unreachable for any real surface — pure white and pure black cannot both fail 3:1 against
        // the same colour — but returning the extreme beats returning something that still fails.
        return ToHex(target);
    }

    /// <summary>
    /// Contrast ratio of <paramref name="candidate"/> against <paramref name="surface"/>, or null if
    /// either will not parse. A translucent candidate is composited over the surface first — what a
    /// boundary contrasts with is what actually lands on screen.
    /// </summary>
    public static double? RatioBetween(string? surface, string? candidate) =>
        TryParse(surface, out var bg, out _) && TryParse(candidate, out var raw, out var alpha)
            ? Ratio(bg, Composite(raw, alpha, bg))
            : null;

    /// <summary>
    /// Flattens a translucent colour onto the surface behind it.
    /// <para>
    /// This used to drop the alpha byte outright, on the reasoning that we do not know what is
    /// behind a translucent colour. We do — it is this method's own <paramref name="surface"/>
    /// argument. Dropping it measured <c>#20FFFFFF</c>, a 12%-alpha white hairline, at 16.66:1
    /// against brand navy when it really lands at 1.46:1: the guard reported a comfortable pass and
    /// left the theme alone, so an author using alpha hairlines got neither the fix nor the
    /// question. Found by the wave-5 review gate 2026-08-05.
    /// </para>
    /// </summary>
    private static (double R, double G, double B) Composite(
        (double R, double G, double B) c, double alpha, (double R, double G, double B) surface) =>
        alpha >= 1.0
            ? c
            : (surface.R + (c.R - surface.R) * alpha,
               surface.G + (c.G - surface.G) * alpha,
               surface.B + (c.B - surface.B) * alpha);

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

    /// <summary>
    /// Parses the hex forms WPF's <c>ColorConverter</c> accepts that we can measure: <c>#RGB</c>,
    /// <c>#ARGB</c>, <c>#RRGGBB</c>, <c>#AARRGGBB</c>. Alpha comes back separately rather than being
    /// discarded — see <see cref="Composite"/>.
    /// <para>
    /// NOT parsed: named colours (<c>Gray</c>) and <c>sc#</c> scRGB, both of which WPF renders
    /// happily. <see cref="ThemeStore"/> does no format validation, so those do reach a theme — they
    /// return false here and the caller degrades to leaving the theme untouched. That is a real gap,
    /// stated rather than papered over; measuring them would mean a WPF dependency in Core.
    /// </para>
    /// </summary>
    private static bool TryParse(string? hex, out (double R, double G, double B) rgb, out double alpha)
    {
        rgb = default;
        alpha = 1.0;
        if (string.IsNullOrWhiteSpace(hex)) return false;

        var s = hex.Trim().TrimStart('#');

        // Shorthand expands by digit doubling, the same rule CSS and WPF use: #abc -> #aabbcc.
        if (s.Length is 3 or 4)
        {
            s = string.Concat(s.Select(c => new string(c, 2)));
        }

        if (s.Length == 8)
        {
            if (!int.TryParse(s[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a)) return false;
            alpha = a / 255.0;
            s = s[2..];
        }
        if (s.Length != 6) return false;

        if (!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)) return false;

        rgb = (Convert.ToInt32(s[..2], 16) / 255.0,
               Convert.ToInt32(s[2..4], 16) / 255.0,
               Convert.ToInt32(s[4..], 16) / 255.0);
        return true;
    }

    /// <summary>
    /// Rounds each channel to the byte it will serialize as. Used before the floor check so the
    /// value measured is the value returned.
    /// </summary>
    private static (double R, double G, double B) Snap((double R, double G, double B) c) =>
        (Byte(c.R) / 255.0, Byte(c.G) / 255.0, Byte(c.B) / 255.0);

    private static string ToHex((double R, double G, double B) c) =>
        $"#{Byte(c.R):X2}{Byte(c.G):X2}{Byte(c.B):X2}";

    private static int Byte(double channel) => Math.Clamp((int)Math.Round(channel * 255), 0, 255);
}
