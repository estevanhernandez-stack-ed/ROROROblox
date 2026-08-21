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
    /// WCAG 1.4.3 AA — text against its background. 4.5:1, the normal-text figure; the 3:1
    /// large-text allowance needs 18pt, or 14pt bold, and the labels this guards are 8-9px.
    /// </summary>
    public const double MinimumTextRatio = 4.5;

    /// <summary>
    /// Picks the most readable of several theme-supplied foregrounds against a fill, and nudges it
    /// only if the best of them still falls short (F-050).
    /// <para>
    /// WHY PICK BEFORE DERIVING. The theme already supplies two colours meant to sit on things —
    /// <c>White</c> and <c>Navy</c> — and which one wins depends entirely on the fill: white reads
    /// better on midnight's muted magenta, navy reads better on brand's hot one. Choosing per theme
    /// is most of the fix and costs nothing in character, because both options are the author's own.
    /// The walk is the remainder: brand's best option reaches 4.40:1 and midnight's 4.16:1, so
    /// picking alone closes two themes of four. Measured 2026-08-20.
    /// </para>
    /// </summary>
    /// <returns>The winning candidate unchanged when it already clears
    /// <paramref name="minimumRatio"/>; otherwise that candidate nudged away from the fill until it
    /// does. Unparseable input degrades to the first candidate, matching the rest of this class.</returns>
    public static string BestForeground(string? fill, IReadOnlyList<string?> candidates, double minimumRatio)
    {
        var fallback = candidates.Count > 0 ? candidates[0] ?? "" : "";
        if (!TryParse(fill, out var bg, out _)) return fallback;

        string? best = null;
        var bestRatio = -1.0;
        foreach (var candidate in candidates)
        {
            if (!TryParse(candidate, out var raw, out var alpha)) continue;
            var r = Ratio(bg, Composite(raw, alpha, bg));
            if (r > bestRatio) (best, bestRatio) = (candidate, r);
        }

        if (best is null) return fallback;
        if (bestRatio >= minimumRatio) return best;

        return Nudge(bg, best, minimumRatio);
    }

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
    public static string Ensure(string? surface, string? candidate) =>
        EnsureAgainstAll([surface], candidate);

    /// <summary>
    /// <see cref="Ensure"/> for a boundary that lands on more than one surface: returns a colour
    /// clearing <see cref="MinimumBoundaryRatio"/> against <b>every</b> surface given.
    /// <para>
    /// WHY THIS EXISTS (F-090). The derived edge was computed against <c>Navy</c> and only Navy,
    /// where it cleared 3:1 by the width of the walk — 3.02 to 3.07 across the four built-ins. Every
    /// built-in also ships <c>RowBg</c> lighter than Navy, so the same edge on a card measured 2.82
    /// in brand, midnight and magenta-heat and 2.28 in flatline, the accessibility theme. Eight of
    /// the fourteen call sites using those styles sit on a card. Nothing was wrong with the
    /// arithmetic; the arithmetic was answering a question about one surface and the answer was
    /// being painted on two.
    /// </para>
    /// <para>
    /// It returns ONE opaque colour rather than one per surface, because what it feeds is a single
    /// brush in a single style. That is also why the walk measures the snapped opaque value against
    /// each surface rather than compositing per surface: the thing that lands on screen is the
    /// opaque colour, and the composite matters only for reading the candidate that came in.
    /// </para>
    /// </summary>
    /// <param name="surfaces">Surfaces the boundary can sit on. Unparseable and empty entries are
    /// skipped — <c>ThemeStore</c> does no format validation, so a named colour does reach here, and
    /// honouring the surfaces we can read beats refusing to derive at all. No parseable surface at
    /// all returns the candidate untouched, matching this class's degrade-quietly contract.</param>
    public static string EnsureAgainstAll(IReadOnlyList<string?> surfaces, string? candidate)
    {
        var fields = new List<(double R, double G, double B)>();
        foreach (var s in surfaces)
        {
            if (TryParse(s, out var parsed, out _)) fields.Add(parsed);
        }

        if (fields.Count == 0 || !TryParse(candidate, out var raw, out var alpha))
        {
            return candidate ?? "";
        }

        // Already good enough everywhere — including the case this method exists for, where it is
        // good enough on the first surface and not on the second. Composited per surface, because a
        // translucent candidate lands differently on each.
        if (fields.All(bg => Ratio(bg, Composite(raw, alpha, bg)) >= MinimumBoundaryRatio))
        {
            return candidate!;
        }

        // Toward white on a dark field, toward black on a light one. 0.179 is the luminance at which
        // white and black are equally readable against a surface — the standard crossover.
        (double, double, double) TargetFor((double R, double G, double B) bg) =>
            Luminance(bg) > 0.179 ? (0.0, 0.0, 0.0) : (1.0, 1.0, 1.0);

        var target = TargetFor(fields[0]);

        // Surfaces that pull opposite ways have no common answer: a near-black field wants a light
        // boundary and a near-white field wants a dark one, and one opaque colour cannot be both.
        // Fall back to the first surface — the pre-F-090 behaviour — so a theme in that shape is no
        // worse off than it was, and say so here rather than returning an extreme that fails both.
        if (fields.Any(bg => TargetFor(bg) != target))
        {
            return EnsureAgainstAll([FormatSurface(fields[0])], candidate);
        }

        var start = Composite(raw, alpha, fields[0]);

        // Walk the blend rather than solving it: the relationship between blend fraction and
        // contrast ratio is monotonic here but not linear, and 100 steps lands within 1% of the
        // minimum needed. Overshooting would make every secondary control louder than the primary.
        for (var step = 1; step <= 100; step++)
        {
            var t = step / 100.0;
            var mixed = (
                start.R + (target.Item1 - start.R) * t,
                start.G + (target.Item2 - start.G) * t,
                start.B + (target.Item3 - start.B) * t);

            // MEASURE WHAT WE RETURN. Snap to bytes BEFORE the check, because ToHex rounds and that
            // rounding can drop the result back under the floor — the un-quantized triple passing is
            // not the same claim as the string we hand back passing. Found by the wave-5 review gate
            // 2026-08-05: over 200k random pairs needing a fix, 9,569 (6.5%) came back below 3:1,
            // e.g. surface #000000 -> #595959 = 2.998:1. All three built-ins cleared it by luck
            // (midnight by 0.019), which is exactly why the tests were green. Snapping first takes
            // that to zero and leaves every built-in byte-identical.
            var snapped = Snap(mixed);
            if (fields.All(bg => Ratio(bg, snapped) >= MinimumBoundaryRatio)) return ToHex(snapped);
        }

        // Unreachable for any real surface — pure white and pure black cannot both fail 3:1 against
        // the same colour — but returning the extreme beats returning something that still fails.
        return ToHex(target);
    }

    /// <summary>Round-trips a parsed surface back to hex so the single-surface fallback can reuse
    /// the public entry point instead of duplicating the walk.</summary>
    private static string FormatSurface((double R, double G, double B) c) => ToHex(c);

    /// <summary>
    /// Walks <paramref name="candidate"/> away from <paramref name="bg"/> until it clears
    /// <paramref name="minimumRatio"/>. Same walk <see cref="EnsureAgainstAll"/> uses, at a
    /// different floor and against one surface — text sits on the control it labels, so there is no
    /// second field to satisfy.
    /// </summary>
    private static string Nudge((double R, double G, double B) bg, string candidate, double minimumRatio)
    {
        if (!TryParse(candidate, out var raw, out var alpha)) return candidate;

        var fg = Composite(raw, alpha, bg);

        // The candidate is already the better of the theme's two, so it is already heading the right
        // way: keep going the way it points rather than the way the fill points. On brand's magenta
        // that means navy gets darker, not white getting lighter — nudging the loser would undo the
        // pick.
        var target = Luminance(fg) < Luminance(bg) ? (0.0, 0.0, 0.0) : (1.0, 1.0, 1.0);

        for (var step = 1; step <= 100; step++)
        {
            var t = step / 100.0;
            var mixed = (
                fg.R + (target.Item1 - fg.R) * t,
                fg.G + (target.Item2 - fg.G) * t,
                fg.B + (target.Item3 - fg.B) * t);

            var snapped = Snap(mixed);
            if (Ratio(bg, snapped) >= minimumRatio) return ToHex(snapped);
        }

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
    /// The <c>#RRGGBB</c> that <paramref name="overlay"/> at <paramref name="opacity"/> actually
    /// produces over <paramref name="surface"/> — the colour a label ends up sitting on.
    /// <para>
    /// Exists because a button's state layers are translucent. The hover and pressed sheens are
    /// white borders at a fixed opacity laid OVER the rank's fill, so the surface behind the label
    /// in those states is not any theme slot: it is this blend. Measuring the label against the
    /// rank's declared fill would report the resting number for all three states and call a state
    /// gate green without ever measuring a state.
    /// </para>
    /// <para>
    /// Public so <c>ButtonStateGateTests</c> composites through the same code path
    /// <see cref="RatioBetween"/> does. This repo has twice shipped a fix into one scanner and
    /// missed its copy in another; a second implementation of alpha compositing would be the third.
    /// </para>
    /// </summary>
    /// <returns>Input that cannot be parsed comes back as <paramref name="surface"/> unchanged,
    /// matching the rest of this class's degrade-quietly contract on a render path.</returns>
    public static string Flatten(string? surface, string? overlay, double opacity)
    {
        if (!TryParse(surface, out var bg, out _)) return surface ?? "";
        if (!TryParse(overlay, out var raw, out var alpha)) return surface ?? "";

        // The element's Opacity and the brush's own alpha both attenuate, and they multiply — a
        // 50%-alpha brush on a 50%-opacity layer lands at 25%, not 50%.
        return ToHex(Snap(Composite(raw, alpha * Math.Clamp(opacity, 0.0, 1.0), bg)));
    }

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
