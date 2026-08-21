using System.Windows;
using System.Windows.Controls;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// F-115, and the instrument F-113 said did not exist: something that measures content PAST a
/// window's bottom edge.
/// <para>
/// <c>AboutWindow</c> shipped a trademark disclaimer cut mid-sentence, on a Store-listed product,
/// for as long as its current copy had existed. Every existing render gate measures pixels inside
/// the frame — colours, ratios, geometry — so all of them were green while the last two lines of
/// the window were simply not drawn. Nothing threw. Nothing logged. The only reason it surfaced is
/// that a human looked at a screenshot.
/// </para>
/// <para>
/// This asks the opposite question: given infinite room, how tall does this window's content WANT
/// to be, and is the frame at least that tall? A fixed-height <c>NoResize</c> window with a star
/// row answers "taller than the frame" and renders anyway, because a star row absorbs the shortfall
/// by giving its child less than it asked for. That is the whole mechanism.
/// </para>
/// <para>
/// <c>WindowSizingFenceTests</c> is the cheap half of the same rule and runs everywhere, including
/// CI. This one needs a real WPF measure pass, so it carries <c>[WindowRenderFact]</c> and skips on
/// CI with the other nine (F-105). Both exist because the fence checks the CONDITION — an
/// unresizable frame with a number in it — and this checks the CONSEQUENCE, which is the thing that
/// actually reaches a user.
/// </para>
/// </summary>
public class WindowContentFitsTests
{
    /// <summary>
    /// Windows cheap enough to construct without a fixture. Deliberately not all fourteen: the
    /// remainder take an API client, an account summary or a plugin manifest, and a test that
    /// builds elaborate doubles to measure a height is a test nobody maintains. These five cover
    /// every shape the conversion touched — two with a body paragraph, two with a heading plus
    /// buttons, one with a bulleted list.
    /// </summary>
    public static TheoryData<string, Func<Window>> Windows() => new()
    {
        { "DpapiCorruptWindow", () => new App.Modals.DpapiCorruptWindow() },
        { "RobloxNotInstalledWindow", () => new App.Modals.RobloxNotInstalledWindow() },
        { "WebView2NotInstalledWindow", () => new App.Modals.WebView2NotInstalledWindow() },
        { "StopAllConfirmWindow", () => new App.Modals.StopAllConfirmWindow(3) },
        { "LeftoverProcessesWindow", () => new App.Modals.LeftoverProcessesWindow(2, 1) },
        { "AboutWindow", () => new App.About.AboutWindow() },
    };

    [WindowRenderTheory]
    [MemberData(nameof(Windows))]
    public void TheFrameIsAtLeastAsTallAsItsContentWantsToBe(string label, Func<Window> build)
    {
        var (desired, actual) = WindowRenderHost.Run(() =>
        {
            var window = build();

            // Off-screen and never activated: this measures layout, and showing a modal in a test
            // run is how you wedge a headless host. Left/Top put it outside any real desktop.
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -10000;
            window.Top = -10000;
            window.ShowInTaskbar = false;
            window.Show();

            try
            {
                window.UpdateLayout();

                // The root element's appetite with the window's width held and its height free. That
                // is exactly the question a fixed frame answers wrong and never reports.
                var root = (FrameworkElement)window.Content;
                root.Measure(new Size(root.ActualWidth, double.PositiveInfinity));

                return (root.DesiredSize.Height, window.ActualHeight);
            }
            finally
            {
                window.Close();
            }
        }, $"content fit [{label}]");

        // Half a pixel of slack for DPI rounding, and no more. The failure this exists to catch is
        // whole lines of text, not sub-pixel drift.
        Assert.True(actual + 0.5 >= desired,
            $"{label}: content wants {desired:F1}px and the frame gives it {actual:F1}px, so "
            + $"{desired - actual:F1}px of it is not drawn — and nothing else in this suite would "
            + "have said so (F-113). Use SizeToContent=\"Height\" with Auto rows.");
    }
}
