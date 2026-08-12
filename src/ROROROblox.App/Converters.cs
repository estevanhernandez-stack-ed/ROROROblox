using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ROROROblox.App;

/// <summary>
/// Tiny WPF value converters used by MainWindow.xaml. Registered as window resources via
/// App.xaml so XAML can use them directly. Kept private to App; if MVVM grows we'll move
/// to a dedicated converters project.
/// </summary>
internal sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Visibility.Visible;
    }
}

internal sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : false;
    }
}

internal sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not Visibility.Visible;
    }
}

internal sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

internal sealed class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is int n && n == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Translate per-account caption-color signal into a SolidColorBrush. Implemented as an
/// <see cref="IMultiValueConverter"/> deliberately: a single <c>{Binding .}</c> on
/// AccountSummary doesn't re-evaluate when sub-properties (CaptionColorHex / IsMain) change,
/// so the row swatch + avatar ring would freeze on first paint. MultiBinding subscribes to
/// each Path individually and re-fires when any of them notifies.
/// <para>
/// Resolution order mirrors <c>RobloxWindowDecorator.ResolveCaptionColor</c>:
/// manual hex wins → magenta-for-main → stable hash-index into the 8-palette.
/// </para>
/// </summary>
internal sealed class CaptionColorBrushConverter : IMultiValueConverter
{
    // Same palette as RobloxWindowDecorator.AutoPalette. That used to say "keep in sync if either
    // changes" and be the only enforcement, and by 2026-08-11 the two had drifted: the "ocean"
    // entry read 0x07,0x58,0x85 here against the decorator's 0xFF075985, so the Settings swatch
    // previewed a colour the Roblox title bar never painted. The palette is Tailwind's and sky-800
    // is #075985, so the decorator was right. CaptionPaletteSyncTests now compares the two copies
    // by value; the comment is no longer load-bearing.
    //
    // KEEP THIS ARRAY COMPACT. ThemedStatusColourTests allow-lists these literals by searching 12
    // lines back for the "AutoPalette" anchor, so comments interleaved between the declaration and
    // the last entry push it out of reach and the gate reports governed colours as violations.
    // It fails loud rather than passing quietly, which is the right direction, but the reach is a
    // constant sized to this array's current length. Explanations go above the declaration.
    private static readonly System.Windows.Media.Color[] AutoPalette =
    {
        System.Windows.Media.Color.FromRgb(0x1E, 0x40, 0xAF),
        System.Windows.Media.Color.FromRgb(0x7C, 0x2D, 0x12),
        System.Windows.Media.Color.FromRgb(0x14, 0x53, 0x2D),
        System.Windows.Media.Color.FromRgb(0x58, 0x1C, 0x87),
        System.Windows.Media.Color.FromRgb(0x7F, 0x1D, 0x1D),
        System.Windows.Media.Color.FromRgb(0x07, 0x59, 0x85), // was 0x58 — see the note above
        System.Windows.Media.Color.FromRgb(0x71, 0x3F, 0x12),
        System.Windows.Media.Color.FromRgb(0x13, 0x4E, 0x4A),
    };
    private static readonly System.Windows.Media.Color MainColor =
        System.Windows.Media.Color.FromRgb(0xE1, 0x3A, 0xA0);

    /// <summary>
    /// Bindings, in order: <c>CaptionColorHex</c> (string?), <c>IsMain</c> (bool),
    /// <c>Id</c> (Guid). Order matters — match the MultiBinding declaration in MainWindow.xaml.
    /// </summary>
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var manual = values.Length > 0 ? values[0] as string : null;
        var isMain = values.Length > 1 && values[1] is bool b && b;
        var id = values.Length > 2 && values[2] is Guid g ? g : Guid.Empty;

        if (!string.IsNullOrWhiteSpace(manual))
        {
            try
            {
                if (System.Windows.Media.ColorConverter.ConvertFromString(manual) is System.Windows.Media.Color c)
                {
                    return new System.Windows.Media.SolidColorBrush(c);
                }
            }
            catch
            {
                // Bad hex → fall through to auto path so the swatch still renders.
            }
        }

        var color = isMain ? MainColor : ColorForId(id);
        return new System.Windows.Media.SolidColorBrush(color);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static System.Windows.Media.Color ColorForId(Guid id)
    {
        if (id == Guid.Empty) return MainColor;
        var hash = id.GetHashCode();
        var idx = ((hash % AutoPalette.Length) + AutoPalette.Length) % AutoPalette.Length;
        return AutoPalette[idx];
    }
}

/// <summary>
/// Compare two values and return <see cref="Visibility.Collapsed"/> when they're equal,
/// <see cref="Visibility.Visible"/> otherwise. Used by the follow-alt chip strip — each chip
/// represents one account; the chip whose Id matches the host row's Id self-hides so a row
/// never lists itself as a follow target.
/// </summary>
internal sealed class EqualsToCollapseConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2) return Visibility.Visible;
        return Equals(values[0], values[1]) ? Visibility.Collapsed : Visibility.Visible;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// StatusDotBrushConverter and IdleChipBrushConverter used to live here, holding six RGB literals
// between them for the row's status dot and its two warning chips. Deleted in v1.17.0: a converter
// cannot observe a resource-dictionary change, and ThemeService.ApplySlot REPLACES the brush
// instance rather than mutating it, so a converter that resolved the theme at Convert time would
// still hand back a stale brush the moment the theme changed. Those colours now come from a Style +
// DataTrigger setting {DynamicResource} in MainWindow.xaml, which re-resolves live. See spec §5.2,
// and ThemedStatusColourTests for the fence that keeps them from growing back.
