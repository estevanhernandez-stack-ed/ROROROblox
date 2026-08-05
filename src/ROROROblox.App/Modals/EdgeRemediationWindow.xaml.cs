using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ROROROblox.App.Theming;
using ROROROblox.Core.Theming;

namespace ROROROblox.App.Modals;

/// <summary>
/// Puts wave 5's one question to the author of a user theme: we raised the outline on clickable
/// controls so it clears the 3:1 contrast floor — keep that, or keep your theme exactly as written?
/// <c>DialogResult == true</c> means keep the derived edge.
/// <para>
/// Shown at most once per theme. The rules for when it appears at all live in
/// <see cref="EdgeRemediation"/>; this window only asks and reports back.
/// </para>
/// </summary>
internal partial class EdgeRemediationWindow : Window
{
    public EdgeRemediationWindow(EdgeQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);
        InitializeComponent();

        BodyText.Text =
            $"RoRoRo now outlines buttons and other clickable controls so they can be told apart "
            + $"from the surface behind them. Your theme — {question.ThemeName} — draws that "
            + "outline in a colour too faint to do the job, so we brightened it just enough to pass. "
            + "Nothing else about your theme changed.";

        Paint(AuthoredSwatch, AuthoredRatio, question.Surface, question.AuthoredEdge);
        Paint(DerivedSwatch, DerivedRatio, question.Surface, question.DerivedEdge);
    }

    /// <summary>
    /// Fills one preview with the theme's own surface and the edge colour in question, and captions
    /// it with the measured ratio. Unparseable colours leave the preview unpainted rather than
    /// inventing one — the caption then reads "—" instead of a number nobody can trust.
    /// </summary>
    private static void Paint(System.Windows.Controls.Border swatch, System.Windows.Controls.TextBlock caption, string surface, string edge)
    {
        if (TryBrush(surface, out var surfaceBrush)) swatch.Background = surfaceBrush;
        if (TryBrush(edge, out var edgeBrush)) swatch.BorderBrush = edgeBrush;

        var ratio = ContrastGuard.RatioBetween(surface, edge);
        caption.Text = ratio is null
            ? "—"
            : string.Format(
                CultureInfo.CurrentCulture,
                "{0:0.0}:1 · {1}",
                ratio.Value,
                ratio.Value >= ContrastGuard.MinimumBoundaryRatio ? "passes" : "below the 3:1 floor");
    }

    private static bool TryBrush(string hex, out SolidColorBrush brush)
    {
        brush = null!;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color color)
            {
                brush = new SolidColorBrush(color);
                return true;
            }
        }
        catch (FormatException)
        {
        }
        return false;
    }

    /// <summary>
    /// Asks, if there is anything to ask, and records the answer. Safe to call after any theme
    /// apply — on a built-in, on a theme already answered for, or on one that already passes, this
    /// does nothing at all. The three call sites (startup, the theme picker, the theme builder)
    /// share it so the question cannot end up phrased or persisted three different ways.
    /// </summary>
    internal static async Task AskIfPendingAsync(ThemeService themeService, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(themeService);

        var question = themeService.PendingEdgeQuestion;
        if (question is null) return;

        var dialog = new EdgeRemediationWindow(question);
        // Setting Owner to a window that has not been shown throws. Falling back to CenterScreen
        // keeps this callable from anywhere rather than making callers reason about window state.
        if (owner is not null && owner.IsLoaded)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        var accepted = dialog.ShowDialog() == true;
        await themeService.AnswerEdgeQuestionAsync(accepted);
    }

    private void OnAcceptClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnKeepClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
