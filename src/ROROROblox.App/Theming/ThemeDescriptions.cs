using ROROROblox.App.Localization;

namespace ROROROblox.App.Theming;

/// <summary>
/// One sentence per built-in theme, keyed by <see cref="ROROROblox.Core.Theming.Theme.Id"/>.
/// Deliberately NOT a <c>Theme</c> slot — the contract stays at ten required fields so every
/// user JSON on disk stays valid without its author touching it, the same invariant
/// <see cref="ROROROblox.Core.Theming.ContrastGuard"/> defends for the derived edge brush. This
/// is App-layer presentation copy; <c>ROROROblox.Core</c> never learns a theme has a description.
/// User themes are not in this switch, so <see cref="For"/> returns <c>null</c> for them and the
/// caller collapses the line rather than leaving an empty gap.
/// </summary>
internal static class ThemeDescriptions
{
    public static string? For(string id) => id.ToLowerInvariant() switch
    {
        "brand" => Loc.Get("Shell_Theme_Desc_Brand"),
        "midnight" => Loc.Get("Shell_Theme_Desc_Midnight"),
        "magenta-heat" => Loc.Get("Shell_Theme_Desc_MagentaHeat"),
        "flatline" => Loc.Get("Shell_Theme_Desc_Flatline"),
        _ => null,
    };
}
