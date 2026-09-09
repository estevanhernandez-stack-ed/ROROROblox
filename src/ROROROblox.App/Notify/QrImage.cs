using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace ROROROblox.App.Notify;

/// <summary>
/// Renders a short payload as a QR image for the phone-setup panels.
///
/// Uses QRCoder's <see cref="PngByteQRCode"/> renderer rather than its bitmap ones, because that
/// path emits PNG bytes with no <c>System.Drawing.Common</c> dependency — which matters here:
/// this app ships packaged, and System.Drawing is exactly the kind of thing that works on the
/// dev box and fails inside an MSIX.
///
/// Dark modules on a white field, not the app's dark theme. A QR reader wants high contrast and
/// a light quiet zone, and an inverted code is a real-world scan failure on some phones. The
/// surrounding UI supplies the brand; the code itself stays boring so it works the first time.
/// </summary>
internal static class QrImage
{
    // 626 navy rather than pure black: still far past the contrast a scanner needs, and it keeps
    // the panel from looking like a pasted-in screenshot.
    private static readonly byte[] Dark = [0x0f, 0x1f, 0x31];
    private static readonly byte[] Light = [0xff, 0xff, 0xff];

    /// <summary>
    /// A frozen, ready-to-bind image for <paramref name="payload"/>, or <c>null</c> when there is
    /// nothing to encode — callers collapse the control rather than showing an empty frame.
    /// </summary>
    /// <param name="pixelsPerModule">Module size. 6 puts a ~40-character URL near 250 px, which
    /// scans reliably off a monitor at arm's length.</param>
    internal static ImageSource? From(string? payload, int pixelsPerModule = 6)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;

        // Q (25% recovery) rather than L: the code is photographed off a glossy screen, often at
        // an angle, sometimes with a reflection across it. The extra density costs nothing at
        // this payload length.
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule, Dark, Light);

        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = new MemoryStream(png);
        image.CacheOption = BitmapCacheOption.OnLoad;   // detach from the stream before it dies
        image.EndInit();
        image.Freeze();                                  // usable from any thread, cheaper to draw
        return image;
    }
}
