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
    // Same palette as RobloxWindowDecorator.AutoPalette — keep in sync if either changes.
    private static readonly System.Windows.Media.Color[] AutoPalette =
    {
        System.Windows.Media.Color.FromRgb(0x1E, 0x40, 0xAF),
        System.Windows.Media.Color.FromRgb(0x7C, 0x2D, 0x12),
        System.Windows.Media.Color.FromRgb(0x14, 0x53, 0x2D),
        System.Windows.Media.Color.FromRgb(0x58, 0x1C, 0x87),
        System.Windows.Media.Color.FromRgb(0x7F, 0x1D, 0x1D),
        System.Windows.Media.Color.FromRgb(0x07, 0x58, 0x85),
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

/// <summary>
/// Translate the AccountSummary.StatusDot string ("green" / "yellow" / "grey") into a SolidColorBrush
/// matching the navy/cyan/magenta brand. Used by the row's leading status dot.
/// </summary>
internal sealed class StatusDotBrushConverter : IValueConverter
{
    private static readonly System.Windows.Media.SolidColorBrush Green =
        new(System.Windows.Media.Color.FromRgb(0x4F, 0xE0, 0x8C)); // brand-friendly green

    private static readonly System.Windows.Media.SolidColorBrush Yellow =
        new(System.Windows.Media.Color.FromRgb(0xF1, 0xB2, 0x32)); // matches RowExpiredAccentBrush

    private static readonly System.Windows.Media.SolidColorBrush Grey =
        new(System.Windows.Media.Color.FromRgb(0x4A, 0x5C, 0x70));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as string) switch
        {
            "green" => Green,
            "yellow" => Yellow,
            _ => Grey,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
