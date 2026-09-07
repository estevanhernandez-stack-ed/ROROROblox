using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ROROROblox.App.Localization;
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
    // The Label is a resx KEY, resolved through Loc when a swatch tooltip is built. This modal is
    // constructed per open, so it reads the current UI culture at build time (localization Phase D).
    private static readonly (string Hex, string LabelKey)[] Palette =
    {
        ("#1E40AF", "Shell_Caption_DeepBlue"),
        ("#7C2D12", "Shell_Caption_BurntOrange"),
        ("#14532D", "Shell_Caption_ForestGreen"),
        ("#581C87", "Shell_Caption_RoyalPurple"),
        ("#7F1D1D", "Shell_Caption_Crimson"),
        ("#075985", "Shell_Caption_Ocean"),
        ("#713F12", "Shell_Caption_AmberBrown"),
        ("#134E4A", "Shell_Caption_DeepTeal"),
        ("#E13AA0", "Shell_Caption_MagentaMain"),
    };

    private readonly AccountSummary _summary;
    private readonly Action? _onApplied;

    public CaptionColorPickerWindow(AccountSummary summary, Action? onApplied = null)
    {
        _summary = summary ?? throw new ArgumentNullException(nameof(summary));
        _onApplied = onApplied;
        InitializeComponent();
        HeaderText.Text = Loc.Format("Shell_Caption_Header", _summary.RenderName);
        HexInput.Text = _summary.CaptionColorHex ?? string.Empty;
        BuildSwatches();
    }

    private void BuildSwatches()
    {
        foreach (var (hex, labelKey) in Palette)
        {
            var label = Loc.Get(labelKey);
            var swatch = new Button
            {
                Width = 50,
                Height = 50,
                Margin = new Thickness(4),
                Background = new SolidColorBrush(ParseColor(hex)),
                // Built in code, so wave 5's markup sweep never saw it — and neither does any test
                // in that wave, which all parse XAML. Found by the review gate.
                BorderBrush = (Brush)FindResource("InteractiveEdgeBrush"),
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
            StatusText.Text = Loc.Get("Shell_Caption_TypeHex");
            return;
        }
        if (!TryNormalizeHex(hex, out var normalized))
        {
            StatusText.Text = Loc.Get("Shell_Caption_BadHex");
            return;
        }
        ApplyColor(normalized);
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        _summary.CaptionColorHex = null;
        HexInput.Text = string.Empty;
        StatusText.Text = Loc.Get("Shell_Caption_Reverted");
        _onApplied?.Invoke();
    }

    private void ApplyColor(string hex)
    {
        _summary.CaptionColorHex = hex;
        HexInput.Text = hex;
        StatusText.Text = Loc.Format("Shell_Caption_Applied", hex);
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
