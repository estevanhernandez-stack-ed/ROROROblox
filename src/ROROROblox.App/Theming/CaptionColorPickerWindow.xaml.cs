using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ROROROblox.App.ViewModels;

namespace ROROROblox.App.Theming;

/// <summary>
/// Tiny modal that lets the user pick a title-bar color for one account. Eight curated swatches,
/// a custom-hex input, and a "Reset to auto" escape hatch. Applies instantly: setting
/// <see cref="AccountSummary.CaptionColorHex"/> fires the ViewModel's persistence pipe AND the
/// decorator's RefreshAccount, so the running Roblox window updates within ~1.5s.
/// </summary>
internal partial class CaptionColorPickerWindow : Window
{
    // Same palette as the auto-derive in RobloxWindowDecorator. Showing them here as
    // first-class options means the user can lock in a color they already get assigned by
    // hash, without it being conditional on Account.Id math.
    private static readonly (string Hex, string Label)[] Palette =
    {
        ("#1E40AF", "Deep Blue"),
        ("#7C2D12", "Burnt Orange"),
        ("#14532D", "Forest Green"),
        ("#581C87", "Royal Purple"),
        ("#7F1D1D", "Crimson"),
        ("#075985", "Ocean"),
        ("#713F12", "Amber Brown"),
        ("#134E4A", "Deep Teal"),
        ("#E13AA0", "Magenta (main)"),
    };

    private readonly AccountSummary _summary;
    private readonly Action? _onApplied;

    public CaptionColorPickerWindow(AccountSummary summary, Action? onApplied = null)
    {
        _summary = summary ?? throw new ArgumentNullException(nameof(summary));
        _onApplied = onApplied;
        InitializeComponent();
        HeaderText.Text = $"Title-bar color for {_summary.DisplayName}";
        HexInput.Text = _summary.CaptionColorHex ?? string.Empty;
        BuildSwatches();
    }

    private void BuildSwatches()
    {
        foreach (var (hex, label) in Palette)
        {
            var swatch = new Button
            {
                Width = 50,
                Height = 50,
                Margin = new Thickness(4),
                Background = new SolidColorBrush(ParseColor(hex)),
                BorderBrush = (Brush)FindResource("DividerBrush"),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = $"{label}  {hex}",
                Tag = hex,
            };
            swatch.Click += OnSwatchClick;
            SwatchGrid.Children.Add(swatch);
        }
    }

    private void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string hex) return;
        ApplyColor(hex);
    }

    private void OnApplyHexClick(object sender, RoutedEventArgs e)
    {
        var hex = HexInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(hex))
        {
            StatusText.Text = "Type a hex color like #4FE08C, or click Reset to auto.";
            return;
        }
        if (!TryNormalizeHex(hex, out var normalized))
        {
            StatusText.Text = "That doesn't look like a hex color. Use #rrggbb (e.g. #4FE08C).";
            return;
        }
        ApplyColor(normalized);
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        _summary.CaptionColorHex = null;
        HexInput.Text = string.Empty;
        StatusText.Text = "Reverted to auto-derived color.";
        _onApplied?.Invoke();
    }

    private void ApplyColor(string hex)
    {
        _summary.CaptionColorHex = hex;
        HexInput.Text = hex;
        StatusText.Text = $"Applied {hex}.";
        _onApplied?.Invoke();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private static bool TryNormalizeHex(string raw, out string normalized)
    {
        normalized = string.Empty;
        var trimmed = raw.Trim().TrimStart('#');
        if (trimmed.Length != 6) return false;
        if (!uint.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            return false;
        }
        normalized = "#" + trimmed.ToUpperInvariant();
        return true;
    }

    private static Color ParseColor(string hex)
    {
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color c) return c;
        }
        catch
        {
        }
        return Colors.Transparent;
    }
}
