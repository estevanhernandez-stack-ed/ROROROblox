namespace ROROROblox.Core.Theming;

/// <summary>
/// Eleven of the twelve brush values a theme resolves to <b>once applied</b>, in <c>#RRGGBB</c>
/// form. <c>OnMagentaBrush</c>, derived alongside the interactive edge by F-050, is deliberately
/// NOT here: this record is the payload of <c>ThemePalette</c> in the plugin contract proto, and
/// adding a field to that is a versioned contract change with an author guide behind it — additive
/// and backwards-compatible, but a decision of its own rather than a side effect of a contrast fix.
/// Stated rather than quietly left out; a plugin reading this palette gets eleven and the app
/// renders twelve.
/// <para>
/// Deliberately distinct from <see cref="Theme"/>, and the distinction is the whole reason this
/// type exists. <c>Theme</c> is what an author wrote; this is what the app is showing. They differ
/// in two ways, both of which matter to anything consuming a palette second-hand:
/// </para>
/// <list type="number">
/// <item><description>
/// <see cref="ThemeSlots.InteractiveEdge"/> is <b>derived</b>, not authored. It is absent from
/// <c>Theme</c> on purpose — see the note there about the contract not growing — and whether it is
/// derived at all is <c>EdgeRemediation</c>'s call, which reads <c>IsBuiltIn</c> plus a persisted
/// per-theme answer. Nothing downstream can recompute it without a second copy of that logic.
/// </description></item>
/// <item><description>
/// <c>ThemeService.ApplySlot</c> returns early when a hex will not parse, leaving the previous
/// brush in place. So the record can say one thing while the screen shows another. This type always
/// carries the screen's answer, which is the same rule <c>ContrastPairGateTests</c> follows when it
/// measures brushes rather than records.
/// </description></item>
/// </list>
/// <para>
/// Lives in Core, beside <see cref="Theme"/> and <see cref="ThemeSlots"/>, because it is consumed
/// both by the WPF layer that produces it and by the plugin host that ships it over the wire —
/// neither of which should have to reach into the other.
/// </para>
/// </summary>
/// <summary>
/// Whatever is applying themes, viewed as "the current palette, plus a nudge when it changes."
/// <para>
/// Exists so the plugin-side bridge can be built and tested against the question rather than
/// against <c>ThemeService</c>, which needs a live <c>Application</c> and <c>Dispatcher</c> to
/// apply anything and therefore cannot raise its event under a test at all. Declared in Core so
/// <c>ThemeService</c> implementing it points Theming at Core — the direction it already runs —
/// rather than at Plugins.
/// </para>
/// </summary>
public interface IThemeAppliedSource
{
    /// <summary>The palette currently on screen, or null before the first apply.</summary>
    ResolvedPalette? CurrentPalette { get; }

    /// <summary>Raised after an apply completes, with the palette that was written.</summary>
    event Action<ResolvedPalette>? ThemeApplied;
}

public sealed record ResolvedPalette(
    string Bg,
    string Cyan,
    string Magenta,
    string White,
    string MutedText,
    string Divider,
    string RowBg,
    string RowExpiredBg,
    string RowExpiredAccent,
    string Navy,
    string InteractiveEdge);
