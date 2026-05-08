namespace ROROROblox.Core;

/// <summary>
/// Canonical FPS preset values surfaced in the per-account dropdown. <see cref="MinCustom"/>
/// / <see cref="MaxCustom"/> bound the Custom… text entry. Spec §5.6 + §11.
/// </summary>
public static class FpsPresets
{
    public const int MinCustom = 10;
    public const int MaxCustom = 9999;
    public const int Unlimited = 9999;

    /// <summary>240 is the Roblox cap-removal threshold — above this, write the cap-removal flag too.</summary>
    public const int CapRemovalThreshold = 240;

    public static readonly IReadOnlyList<int> Values = new[]
    {
        20, 30, 45, 60, 90, 120, 144, 165, 240, Unlimited
    };

    /// <summary>
    /// Clamp a user-supplied custom value into the supported range. Out-of-range silently snaps —
    /// the dropdown is not the place to surface "you typed an invalid number" modals.
    /// </summary>
    public static int ClampCustom(int raw) => Math.Clamp(raw, MinCustom, MaxCustom);
}
