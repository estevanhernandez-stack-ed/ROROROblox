using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.Core;

namespace ROROROblox.App.Tray;

/// <summary>
/// Composes the tray icon from the user's main-account avatar plus a state-colored ring.
/// Best-effort: any failure (network, malformed PNG, GDI+ hiccup) silently leaves the
/// resource ICOs in place. Cached per-URL to avoid re-downloading on app restart.
/// </summary>
internal sealed class MainAvatarTrayPainter
{
    // Windows tray scales the icon to whatever DPI / "small icons" setting the user has —
    // typically 16x16 or 20x20 displayed even from a 32x32 source. A 3-px ring at 32 dropped
    // to ~1px on screen, which is what made the green status invisible. We render at 64x64
    // and use a thicker ring so the status survives the down-scale on every taskbar setting.
    private const int CanvasSize = 64;
    private const int RingThickness = 8;
    private const int Inset = RingThickness + 2;

    // Brand-aligned ring colors per state. Match RowBg / status dots elsewhere in the app.
    private static readonly Color RingOn = Color.FromArgb(0xFF, 0x4F, 0xE0, 0x8C);     // green
    private static readonly Color RingOff = Color.FromArgb(0xFF, 0x4A, 0x5C, 0x70);    // muted grey
    private static readonly Color RingError = Color.FromArgb(0xFF, 0xF2, 0x2F, 0x89);  // magenta
    private static readonly Color Navy = Color.FromArgb(0xFF, 0x0F, 0x1F, 0x31);

    private readonly TrayService _tray;
    private readonly HttpClient _http;
    private readonly ILogger<MainAvatarTrayPainter> _log;
    private readonly string _cacheDir;

    private string? _activeAvatarUrl;
    private byte[]? _activeAvatarBytes;

    public MainAvatarTrayPainter(TrayService tray, HttpClient http, ILogger<MainAvatarTrayPainter>? log = null)
    {
        _tray = tray ?? throw new ArgumentNullException(nameof(tray));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _log = log ?? NullLogger<MainAvatarTrayPainter>.Instance;
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ROROROblox",
            "tray-cache");
    }

    /// <summary>
    /// Refresh the tray icon based on the current main account. Pass null/empty to revert to
    /// the bundled defaults. Network + composition happens off-thread; once icons are built we
    /// marshal back to the dispatcher to swap them in.
    /// </summary>
    public async Task UpdateAsync(string? avatarUrl, System.Windows.Threading.Dispatcher dispatcher)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(avatarUrl))
            {
                _activeAvatarUrl = null;
                _activeAvatarBytes = null;
                dispatcher.Invoke(() => _tray.SetCustomStateIcons(null, null, null));
                return;
            }

            if (avatarUrl == _activeAvatarUrl && _activeAvatarBytes is not null)
            {
                // Same URL we already painted — nothing to do.
                return;
            }

            var bytes = await LoadAvatarBytesAsync(avatarUrl).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0)
            {
                _log.LogDebug("Avatar bytes empty for {Url}; leaving default tray icons.", avatarUrl);
                return;
            }

            var (on, off, error) = ComposeIcons(bytes);
            _activeAvatarUrl = avatarUrl;
            _activeAvatarBytes = bytes;

            dispatcher.Invoke(() => _tray.SetCustomStateIcons(on, off, error));
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Tray avatar paint failed; leaving defaults in place.");
        }
    }

    private async Task<byte[]?> LoadAvatarBytesAsync(string avatarUrl)
    {
        var key = ToCacheFileName(avatarUrl);
        var cachePath = Path.Combine(_cacheDir, key);
        try
        {
            Directory.CreateDirectory(_cacheDir);
            if (File.Exists(cachePath))
            {
                return await File.ReadAllBytesAsync(cachePath).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Avatar cache read failed; will re-download.");
        }

        try
        {
            using var resp = await _http.GetAsync(avatarUrl).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogDebug("Avatar GET returned {Status} for {Url}", resp.StatusCode, avatarUrl);
                return null;
            }
            var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            try { await File.WriteAllBytesAsync(cachePath, bytes).ConfigureAwait(false); } catch { }
            return bytes;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Avatar download threw for {Url}; tray stays default.", avatarUrl);
            return null;
        }
    }

    /// <summary>
    /// Produce three composited icons (one per multi-instance state). Each is a 32x32 navy
    /// disk with the avatar inset and a state-colored ring outline. Caller takes ownership of
    /// the returned Icons and must dispose them (TrayService.SetCustomStateIcons does this).
    /// </summary>
    private static (Icon on, Icon off, Icon error) ComposeIcons(byte[] avatarBytes)
    {
        using var avatarStream = new MemoryStream(avatarBytes);
        using var avatarBmp = new Bitmap(avatarStream);

        var on = ComposeOne(avatarBmp, RingOn);
        var off = ComposeOne(avatarBmp, RingOff);
        var error = ComposeOne(avatarBmp, RingError);
        return (on, off, error);
    }

    private static Icon ComposeOne(Bitmap avatar, Color ringColor)
    {
        using var canvas = new Bitmap(CanvasSize, CanvasSize, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;

            // Navy disk background.
            using (var bgBrush = new SolidBrush(Navy))
            {
                g.FillEllipse(bgBrush, 0, 0, CanvasSize - 1, CanvasSize - 1);
            }

            // Avatar clipped to inner circle.
            var inner = new RectangleF(Inset, Inset, CanvasSize - 1 - 2 * Inset, CanvasSize - 1 - 2 * Inset);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(inner);
                g.SetClip(path);
                g.DrawImage(avatar, inner);
                g.ResetClip();
            }

            // State ring on the outer edge.
            using var ringPen = new Pen(ringColor, RingThickness);
            var ringRect = new RectangleF(
                RingThickness / 2f,
                RingThickness / 2f,
                CanvasSize - 1 - RingThickness,
                CanvasSize - 1 - RingThickness);
            g.DrawEllipse(ringPen, ringRect);
        }

        // Convert to a self-contained Icon. Icon.FromHandle's source HICON must be destroyed —
        // round-trip via .ico stream so the returned Icon owns its bytes and we can DestroyIcon.
        var hicon = canvas.GetHicon();
        try
        {
            using var fromHandle = Icon.FromHandle(hicon);
            using var ms = new MemoryStream();
            fromHandle.Save(ms);
            ms.Position = 0;
            return new Icon(ms);
        }
        finally
        {
            DestroyIcon(hicon);
        }
    }

    private static string ToCacheFileName(string url)
    {
        // Hash so the same URL always maps to the same file; safe-for-filename.
        var hash = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(url));
        return Convert.ToHexString(hash) + ".png";
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
