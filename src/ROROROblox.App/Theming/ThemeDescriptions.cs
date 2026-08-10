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
        "brand" => "Cyan and magenta on navy: RoRoRo's default look, and the one you'll see in every screenshot.",
        "midnight" => "Cooler and dimmer than Brand, built for late sessions when you don't want a bright screen staring back.",
        "magenta-heat" => "Leads with magenta instead of cyan, so your Launch and Stop buttons are the first thing you notice.",
        "flatline" => "Reads the same whether or not you can tell colours apart: everything the app says in colour, it also says in words or shape.",
        _ => null,
    };
}
