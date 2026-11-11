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

    /// <remarks>
    /// 75, 100 and 180 joined on 2026-11-10: they are common monitor refresh rates the list skipped,
    /// and a Store review asked for 75 and 180 by name. The dropdown in MainWindow.xaml is written
    /// by hand; <c>FpsCapChoicesTests</c> holds it to this list.
    /// </remarks>
    public static readonly IReadOnlyList<int> Values = new[]
    {
        20, 30, 45, 60, 75, 90, 100, 120, 144, 165, 180, 240, Unlimited
    };

    /// <summary>
    /// Clamp a user-supplied custom value into the supported range. Out-of-range silently snaps —
    /// the dropdown is not the place to surface "you typed an invalid number" modals.
    /// </summary>
    public static int ClampCustom(int raw) => Math.Clamp(raw, MinCustom, MaxCustom);
}
