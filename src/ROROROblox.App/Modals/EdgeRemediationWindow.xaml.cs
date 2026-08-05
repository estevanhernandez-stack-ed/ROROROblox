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

        // NOT "your colour is too faint." Our own theme template ships divider #1F3149 on navy
        // #0F1F31 — 1.26:1 — and that is the RIGHT value for a hairline between rows. The divider
        // was doing two jobs and only one of them needed 3:1; anyone who followed our documentation
        // wrote exactly this. Copy that implies the author erred would be blaming them for our
        // design. Found by the wave-5 review gate.
        BodyText.Text =
            $"RoRoRo now outlines buttons so they can be told apart from the surface behind them. "
            + $"Your theme — {question.ThemeName} — sets one divider colour, and it does two jobs: "
            + "the faint rule between rows, which is right as you wrote it, and now the outline on a "
            + "button, which needs to be brighter to be seen. We brightened it for buttons only.";

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

        // Dismissed is not answered. One reflexive click on the title-bar X must not permanently opt
        // somebody's theme out of the fix without them reading a word of it — there is no re-ask
        // affordance anywhere.
        //
        // MEASURED, because the obvious version of this check does not work: a first attempt tested
        // `ShowDialog() is null`, on the assumption that closing via the X leaves DialogResult unset.
        // It does not. Verified live 2026-08-05 by sending WM_CLOSE to the real dialog — WPF closes a
        // modal with DialogResult false, so the X was indistinguishable from pressing Keep, and the
        // answer was still written. A flag the two button handlers set is the only thing that
        // actually separates an answer from a dismissal.
        dialog.ShowDialog();
        if (!dialog._answered) return;

        // Esc DOES count as an answer: IsCancel routes it through the labelled Keep button, which
        // sets the flag — the same contract as every other modal in the app.
        await themeService.AnswerEdgeQuestionAsync(question, dialog.DialogResult == true);
    }

    /// <summary>
    /// True once one of the two labelled buttons was actually pressed. WPF closes a modal with
    /// <c>DialogResult == false</c> whether the user chose "keep mine" or just clicked the X, so
    /// <c>DialogResult</c> alone cannot tell an answer from a dismissal — this can.
    /// </summary>
    private bool _answered;

    private void OnAcceptClick(object sender, RoutedEventArgs e)
    {
        _answered = true;
        DialogResult = true;
        Close();
    }

    private void OnKeepClick(object sender, RoutedEventArgs e)
    {
        _answered = true;
        DialogResult = false;
        Close();
    }
}
