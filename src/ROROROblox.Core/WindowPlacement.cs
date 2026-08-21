namespace ROROROblox.Core;

/// <summary>
/// A saved main-window position and size, and the rule for whether it is still safe to restore
/// (F-060).
/// </summary>
/// <remarks>
/// <para>
/// The restore is guarded because monitor layouts change. A window saved on a second display that
/// is now unplugged, or on a monitor whose resolution dropped, restores to coordinates that are no
/// longer on any screen — and a window nobody can see is worse than one that forgot its size, which
/// is the very complaint this fixes. Docking stations and laptop lids make that ordinary, not
/// exotic.
/// </para>
/// </remarks>
public readonly record struct WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized)
{
    /// <summary>How much of the window must land inside the virtual desktop to count as reachable.</summary>
    private const double RequiredVisibleFraction = 0.25;

    /// <summary>Below this, a saved size is a corrupt or collapsed record rather than a preference.</summary>
    private const double MinimumSensibleEdge = 200;

    /// <summary>
    /// True when this placement can be restored onto the given virtual-desktop bounds.
    /// </summary>
    /// <remarks>
    /// The test is overlap, not containment. A window deliberately hanging off the right edge of a
    /// display is a normal thing people do and must survive a restart; only one with almost nothing
    /// on screen is unreachable. A quarter of its area is enough to grab a title bar.
    /// </remarks>
    public bool IsRestorableOnto(double screenLeft, double screenTop, double screenWidth, double screenHeight)
    {
        if (Width < MinimumSensibleEdge || Height < MinimumSensibleEdge) return false;
        if (double.IsNaN(Left) || double.IsNaN(Top) || double.IsNaN(Width) || double.IsNaN(Height)) return false;

        var overlapWidth = Math.Min(Left + Width, screenLeft + screenWidth) - Math.Max(Left, screenLeft);
        var overlapHeight = Math.Min(Top + Height, screenTop + screenHeight) - Math.Max(Top, screenTop);
        if (overlapWidth <= 0 || overlapHeight <= 0) return false;

        var visible = overlapWidth * overlapHeight;
        return visible >= Width * Height * RequiredVisibleFraction;
    }
}
