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
