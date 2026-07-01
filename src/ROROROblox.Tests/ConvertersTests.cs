using System.Globalization;
using System.Windows.Media;
using ROROROblox.App;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// Task 7 — idle-chip amber/muted brush converter. Mirrors the shape of
/// <see cref="StatusDotBrushConverter"/>: a static Amber/Muted brush pair, no allocation per call.
/// </summary>
public class ConvertersTests
{
    [Fact]
    public void IdleChipBrush_WarnTrue_ReturnsAmber()
    {
        var conv = new IdleChipBrushConverter();
        var brush = (SolidColorBrush)conv.Convert(true, typeof(Brush), null, CultureInfo.InvariantCulture);
        Assert.Equal(Color.FromRgb(0xF1, 0xB2, 0x32), brush.Color);
    }

    [Fact]
    public void IdleChipBrush_WarnFalse_ReturnsMuted()
    {
        var conv = new IdleChipBrushConverter();
        var brush = (SolidColorBrush)conv.Convert(false, typeof(Brush), null, CultureInfo.InvariantCulture);
        Assert.Equal(Color.FromRgb(0x8A, 0x93, 0xA0), brush.Color);
    }
}
