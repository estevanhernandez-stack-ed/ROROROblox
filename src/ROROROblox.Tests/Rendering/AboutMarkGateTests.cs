using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using ROROROblox.App.Theming;
using ROROROblox.Core.Theming;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// The two drawn About-page marks and the theme plumbing they need (v1.33 item 10).
/// <para>
/// <b>Why this renders pixels rather than only reading the dictionary.</b> The egg mark's fill is a
/// <c>LinearGradientBrush</c> whose stops read <c>CyanColor</c> and <c>MagentaColor</c> through
/// <c>DynamicResource</c>. A <c>GradientStop</c> is a <see cref="Freezable"/>, not a framework
/// element, and whether a dynamic reference resolves inside one depends on WPF handing it an
/// inheritance context — which it does here, but nothing in a successful BUILD says so. A gradient
/// that silently failed to resolve would paint the mark transparent or black, and every other test in
/// this suite would stay green through it. So one clause renders the real shape under a real theme
/// and reads the hues back off the bitmap.
/// </para>
/// </summary>
public class AboutMarkGateTests
{
    private const string EggMarkXaml = """
        <Path xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
              Stretch="Fill" Width="200" Height="40"
              Fill="{DynamicResource DuoBrush}"
              Data="M0,0 L1,0 L1,1 L0,1 Z" />
        """;

    private static Theme BrandTheme() => new(
        Id: "brand-test", Name: "Brand", Bg: "#0F1F31", Cyan: "#17D4FA", Magenta: "#F22F89",
        White: "#FFFFFF", MutedText: "#9AA8B8", Divider: "#1F3149", RowBg: "#15263A",
        RowExpiredBg: "#3A2D14", RowExpiredAccent: "#D9A33C", Navy: "#0F1F31");

    [Fact]
    public void ThemeServicePublishesTheDuoAsAFrozenBrushFromTheThemesOwnEnds()
    {
        var resources = new ResourceDictionary();

        ThemeService.ApplyTo(resources, BrandTheme(), edgeAnswer: null);

        var duo = Assert.IsType<LinearGradientBrush>(resources[ThemeService.DuoBrushKey]);
        Assert.True(duo.IsFrozen, "the duo brush must be frozen — nothing may edit a theme brush in place.");
        Assert.Equal(2, duo.GradientStops.Count);
        // Ends come from the theme, not from baked brand hex, so a theme that redefines either end
        // carries the mark with it.
        Assert.Equal(((SolidColorBrush)resources[ThemeSlots.Cyan]).Color, duo.GradientStops[0].Color);
        Assert.Equal(((SolidColorBrush)resources[ThemeSlots.Magenta]).Color, duo.GradientStops[1].Color);
    }

    [Fact]
    public void TheEggMarksGradientResolvesTheThemesCyanAndMagenta()
    {
        var (left, right) = WindowRenderHost.Run<(Color Left, Color Right)>(() =>
        {
            var resources = new ResourceDictionary();
            ThemeService.ApplyTo(resources, BrandTheme(), edgeAnswer: null);

            var path = (System.Windows.Shapes.Path)System.Windows.Markup.XamlReader.Parse(EggMarkXaml);
            // Attached AFTER the parse, exactly as the other render gates do it: a DynamicResource
            // is resolved at lookup time, which is why the markup above must not use StaticResource.
            path.Resources = resources;

            var w = (int)path.Width;
            var h = (int)path.Height;
            path.Measure(new Size(w, h));
            path.Arrange(new Rect(0, 0, w, h));
            path.UpdateLayout();

            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(path);

            var stride = w * 4;
            var px = new byte[stride * h];
            rtb.CopyPixels(Int32Rect.Empty, px, stride, 0);

            Color At(int x, int y)
            {
                var i = y * stride + x * 4;
                return Color.FromArgb(px[i + 3], px[i + 2], px[i + 1], px[i]);
            }

            return (At(2, h / 2), At(w - 3, h / 2));
        }, "about egg mark gradient");

        // Opaque at both ends: an unresolved DynamicResource leaves the stop at its default, which
        // paints transparent — the exact failure this clause exists to catch.
        Assert.True(left.A > 200, $"left end is not opaque ({left}) — the gradient did not resolve.");
        Assert.True(right.A > 200, $"right end is not opaque ({right}) — the gradient did not resolve.");

        // Cyan end: blue and green dominant, red low. Magenta end: red and blue dominant, green low.
        Assert.True(left.B > 180 && left.G > 150 && left.R < 120,
            $"left end should read as the theme's cyan #17D4FA, got {left}.");
        Assert.True(right.R > 180 && right.B > 100 && right.G < 120,
            $"right end should read as the theme's magenta #F22F89, got {right}.");
    }

    [Theory]
    [InlineData("Mark626LabsWordmark")]
    [InlineData("MarkKoii4Eva")]
    public void BothMarksResolveToRealGeometry(string key)
    {
        // Marks.xaml is generated and 65 KB of path data. A truncated or mis-escaped Figures string
        // can still parse into an EMPTY geometry, which draws nothing and looks like a layout bug
        // rather than a broken asset.
        var bounds = Sta.Run(() =>
        {
            var root = XamlStyleScanner.FindRepoRoot();
            Assert.False(root is null, "Could not locate ROROROblox.slnx.");
            var marks = System.IO.Path.Combine(root!, "src", "ROROROblox.App", "About", "Marks", "Marks.xaml");
            Assert.True(System.IO.File.Exists(marks), $"Marks.xaml is missing at {marks}. It is generated — run scripts/gen-about-marks-xaml.py.");

            // Parsed from the committed file rather than a pack URI: the pack scheme is only
            // registered once an Application exists, which made this clause pass or fail on test
            // ORDER. Reading the source also means this asserts what is in the repo.
            var dict = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(System.IO.File.ReadAllText(marks));
            var geometry = dict[key] as Geometry;
            Assert.True(geometry is not null, $"Marks.xaml no longer exposes '{key}'.");
            return geometry!.Bounds;
        }, $"about mark {key}");

        Assert.False(bounds.IsEmpty, $"'{key}' parsed to empty geometry.");
        Assert.True(bounds.Width > 1 && bounds.Height > 1,
            $"'{key}' has degenerate bounds {bounds} — it would draw as nothing.");
        // Both marks are wordmarks: wider than tall, by a lot. Catches a mark that parsed into a
        // single stray contour.
        Assert.True(bounds.Width > bounds.Height * 2,
            $"'{key}' is not wordmark-shaped ({bounds.Width:n0}x{bounds.Height:n0}).");
    }
}
