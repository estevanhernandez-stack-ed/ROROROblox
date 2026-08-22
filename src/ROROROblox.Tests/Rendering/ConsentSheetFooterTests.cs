using System.Windows;
using System.Windows.Media;
using ROROROblox.App.Plugins;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// F-120 — the consent sheet clipped its own Install button, live, on the first ur-mcp install.
/// <para>
/// The sheet was a six-row Grid, all rows <c>Auto</c>, buttons last, under
/// <c>SizeToContent="Height"</c> with <c>MaxHeight="620"</c> and <c>ResizeMode="NoResize"</c>.
/// No manifest before ur-mcp's was tall enough to pass the cap, so this shipped certified and
/// surfaced on the first plugin big enough to trip it: WPF clips bottom rows first, the footer
/// fell off the window, and the only way through was the accident that Install is
/// <c>IsDefault</c>. A mouse-only user was simply stuck on the one dialog that gates every
/// plugin install.
/// </para>
/// <para>
/// <c>WindowContentFitsTests</c> cannot hold this window: its assertion is "the frame gives the
/// content its full desired height", and the consent sheet legitimately refuses that for a tall
/// manifest — the cap is the design, the ScrollViewer is the pressure valve. What must hold
/// instead is WHICH element absorbs the shortfall. These two tests pin both ends: under a
/// worst-case manifest the footer and the list survive inside the frame (the list shrunk to its
/// scroller, never gone); under a one-capability manifest the sheet is still compact, so the fix
/// did not spend F-115's win to buy F-120's.
/// </para>
/// </summary>
public class ConsentSheetFooterTests
{
    private static PluginManifest Manifest(string description, params string[] capabilities) => new()
    {
        SchemaVersion = PluginManifest.CurrentSchemaVersion,
        Id = "626labs.test-fixture",
        Name = "Consent sheet fixture",
        Version = "9.9.9",
        ContractVersion = "1.0",
        Publisher = "626 Labs LLC",
        Description = description,
        Capabilities = capabilities,
    };

    /// <summary>
    /// Taller than anything shipped: ur-mcp carries the family's longest description and six
    /// capabilities; this doubles the paragraph and asks for ten, so the fixture stays worst-case
    /// even after the next big manifest lands.
    /// </summary>
    private static PluginManifest WorstCase() => Manifest(
        "Lets an AI operator drive RoRoRo over MCP: list and launch your saved accounts, follow "
        + "the main, check who is in game, stop clients, and run or stop Ur Task macros. Installed "
        + "and consented here; launched by the operator, not by RoRoRo — autostart stays off. "
        + "This second paragraph exists to out-grow every real manifest: it pushes the description "
        + "block past anything the family ships so that the window is guaranteed to hit its "
        + "MaxHeight and the layout has to choose which element loses. The buttons must not be "
        + "the answer, in any theme, at any DPI, for any manifest a release can produce.",
        PluginCapability.HostQueriesAccounts,
        PluginCapability.HostQueriesCurrentServer,
        PluginCapability.HostQueriesAccountActivity,
        PluginCapability.HostCommandsRequestLaunch,
        PluginCapability.HostCommandsLaunchTarget,
        PluginCapability.HostCommandsStopAccounts,
        PluginCapability.HostEventsAccountLaunched,
        PluginCapability.HostEventsMemoryPressure,
        PluginCapability.SystemSynthesizeKeyboardInput,
        PluginCapability.SystemPreventSleep);

    private static (Rect install, Rect cancel, Rect list, double rootHeight, double windowHeight, double contentDesired)
        Render(PluginManifest manifest)
        => WindowRenderHost.Run(() =>
        {
            var window = new ConsentSheet(manifest)
            {
                // Off-screen and never activated, same as WindowContentFitsTests: this measures
                // layout, and showing a modal in a test run is how you wedge a headless host.
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                ShowInTaskbar = false,
            };
            window.Show();

            try
            {
                window.UpdateLayout();

                var root = (FrameworkElement)window.Content;

                Rect BoundsOf(FrameworkElement element) =>
                    element.TransformToAncestor(root)
                        .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

                // Bounds first: the desired-size measure below invalidates layout, and transforms
                // read the arrange that has already happened.
                var install = BoundsOf(window.InstallButton);
                var cancel = BoundsOf(window.CancelButton);
                var list = BoundsOf(window.CapabilityFrame);
                var rootHeight = root.ActualHeight;
                var windowHeight = window.ActualHeight;

                root.Measure(new Size(root.ActualWidth, double.PositiveInfinity));

                return (install, cancel, list, rootHeight, windowHeight, root.DesiredSize.Height);
            }
            finally
            {
                window.Close();
            }
        }, "consent sheet footer");

    [WindowRenderFact]
    public void WorstCaseManifest_TheFooterAndTheListBothSurviveInsideTheFrame()
    {
        var r = Render(WorstCase());

        // The fixture only proves anything if it actually overflows: the window must be pinned at
        // its cap. If a future edit raises MaxHeight past what this manifest needs, the fixture
        // has stopped exercising F-120 and must grow, not quietly pass.
        Assert.True(r.windowHeight >= 619.5,
            $"The worst-case manifest no longer reaches the window's MaxHeight (window measured "
            + $"{r.windowHeight:F1}px). Make the fixture taller — a fixture that fits proves nothing.");

        Assert.True(r.install.Bottom <= r.rootHeight + 0.5 && r.cancel.Bottom <= r.rootHeight + 0.5,
            $"F-120 is back: the footer is clipped. Install bottom {r.install.Bottom:F1}px, Cancel "
            + $"bottom {r.cancel.Bottom:F1}px, against {r.rootHeight:F1}px of layout room. The "
            + "footer must be laid out before the flexible content — dock order in "
            + "ConsentSheet.xaml is the fix, not a taller window.");

        Assert.True(r.install.Height >= 1 && r.cancel.Height >= 1,
            "A zero-height footer button 'fits' any frame. The buttons must actually have size.");

        // F-115's fear, held to: the shortfall lands on the capability list, which shrinks to its
        // scroller — it must never vanish. 60px is under the XAML MinHeight of 96 by enough to
        // absorb border and rounding, and far over the zero that 'collapsed' means.
        Assert.True(r.list.Height >= 60 && r.list.Bottom <= r.rootHeight + 0.5,
            $"The capability list did not survive the squeeze: {r.list.Height:F1}px tall, bottom "
            + $"{r.list.Bottom:F1}px in {r.rootHeight:F1}px. The list absorbs overflow by "
            + "scrolling, not by disappearing (F-115).");
    }

    [WindowRenderFact]
    public void OneCapabilityManifest_TheSheetIsStillCompact()
    {
        var r = Render(Manifest("Reads the palette.", PluginCapability.HostQueriesCurrentServer));

        // The F-115 win, restated as WindowContentFitsTests would: a short manifest fits whole —
        // nothing clipped, no scroller engaged, and the frame well under the cap rather than
        // 620px of empty.
        Assert.True(r.windowHeight <= 560,
            $"A one-capability sheet measured {r.windowHeight:F1}px — SizeToContent has stopped "
            + "shrinking the window and F-115's compact sheet is gone.");

        // Same comparison WindowContentFitsTests makes: DesiredSize includes the root's margins,
        // so the frame that has to cover it is the window, not the margin-less arrange height.
        Assert.True(r.windowHeight + 0.5 >= r.contentDesired,
            $"Short-manifest content wants {r.contentDesired:F1}px and the window gives it "
            + $"{r.windowHeight:F1}px — the compact case must fit completely (F-113, F-115).");
    }
}
