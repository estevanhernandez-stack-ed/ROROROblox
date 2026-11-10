using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// The Roblox-needed modal's Microsoft Store edition sentence (2026-11-10): on screen only when
/// that edition was found, and first in the body when it is.
/// </summary>
public class RobloxNeededStoreEditionLineTests
{
    [WindowRenderFact]
    public void WithoutTheStoreEdition_TheSentenceTakesNoSpace()
    {
        var (visibility, height) = Measure(storeEditionFound: false);

        Assert.Equal(Visibility.Collapsed, visibility);
        Assert.Equal(0, height);
    }

    [WindowRenderFact]
    public void WithTheStoreEdition_TheSentenceIsDrawn_AndLeadsTheBody()
    {
        var (visibility, height, storeTop, genericTop) = WindowRenderHost.Run(() =>
        {
            var window = new App.Modals.RobloxNotInstalledWindow(storeEditionFound: true);
            return Shown(window, () =>
            {
                var store = window.StoreEditionLine;
                var root = (Grid)window.Content;
                // The generic "can't find that handler" paragraph, found by what it SAYS. The first
                // version of this test found it by row, relative to the sentence under test, and so
                // passed with the two rows swapped (caught by mutation, 2026-11-10).
                var genericText = App.Localization.Loc.Get("RobloxNotInstalledWindow_RororoLaunchesRobloxViaThe");
                var generic = root.Children.OfType<TextBlock>().Single(t => t.Text == genericText);
                return (store.Visibility, store.ActualHeight,
                    store.TransformToAncestor(root).Transform(new Point(0, 0)).Y,
                    generic.TransformToAncestor(root).Transform(new Point(0, 0)).Y);
            });
        }, "Roblox-needed modal, Store edition found");

        Assert.Equal(Visibility.Visible, visibility);
        Assert.True(height > 0, "the Store-edition sentence has no height, so it is not drawn");
        Assert.True(storeTop < genericTop,
            $"the Store-edition sentence (y={storeTop:F0}) must come before the generic paragraph (y={genericTop:F0}): "
            + "it is the one line that explains what the person is looking at");
    }

    private static (Visibility, double) Measure(bool storeEditionFound) => WindowRenderHost.Run(() =>
    {
        var window = new App.Modals.RobloxNotInstalledWindow(storeEditionFound);
        return Shown(window, () => (window.StoreEditionLine.Visibility, window.StoreEditionLine.ActualHeight));
    }, $"Roblox-needed modal, storeEditionFound={storeEditionFound}");

    /// <summary>Off-screen and never activated, the same way <see cref="WindowContentFitsTests"/> shows a window.</summary>
    private static T Shown<T>(Window window, Func<T> read)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000;
        window.Top = -10000;
        window.ShowInTaskbar = false;
        window.Show();
        try
        {
            window.UpdateLayout();
            return read();
        }
        finally
        {
            window.Close();
        }
    }
}
