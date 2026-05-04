using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.ViewModels;

namespace ROROROblox.App.Tray;

/// <summary>
/// Repaints foreign Roblox windows so each account's RobloxPlayerBeta is visually distinct in
/// the taskbar. Two ops, both via Win32 against the foreign process's main HWND:
/// <list type="bullet">
///   <item><c>SetWindowText</c> — title becomes <c>"Roblox - {DisplayName}"</c>.</item>
///   <item><c>DwmSetWindowAttribute(DWMWA_CAPTION_COLOR)</c> — title bar tinted per account
///   (Windows 11 / Mica only; older Windows silently no-ops).</item>
/// </list>
/// We re-apply on a 1.5s timer because Roblox sometimes renames its own window on focus or
/// game-state changes; the user's customization wins on the next tick.
/// </summary>
internal sealed class RobloxWindowDecorator : IDisposable
{
    // DwmSetWindowAttribute attribute id for the title bar color. Win11+ only.
    // Doc: https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute
    private const int DWMWA_CAPTION_COLOR = 35;

    // Auto-palette for accounts that don't have an explicit override. Stable + deterministic
    // — Account.Id hash picks an index. Hand-tuned to feel distinct from each other and from
    // the brand cyan/magenta so the main account stays visually privileged.
    private static readonly uint[] AutoPalette =
    {
        0xFF1E40AF, // deep blue
        0xFF7C2D12, // burnt orange
        0xFF14532D, // forest green
        0xFF581C87, // royal purple
        0xFF7F1D1D, // crimson
        0xFF075985, // ocean
        0xFF713F12, // amber-brown
        0xFF134E4A, // teal-deep
    };

    // Magenta for the main account — it pops against any theme.
    private const uint MainCaptionColor = 0xFFE13AA0;

    private readonly System.Threading.Timer _reapplyTimer;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, DecoratedTarget> _targets = new();
    private readonly ILogger<RobloxWindowDecorator> _log;
    private bool _disposed;

    public RobloxWindowDecorator(ILogger<RobloxWindowDecorator>? log = null)
    {
        _log = log ?? NullLogger<RobloxWindowDecorator>.Instance;
        // Re-apply every 1.5s — cheap (a couple Win32 calls per running Roblox), defeats
        // Roblox's own occasional title rewrites on game state changes.
        _reapplyTimer = new System.Threading.Timer(_ => ReapplyAll(), null,
            TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(1500));
    }

    /// <summary>
    /// Start decorating the Roblox process for this account. Polls for a non-zero
    /// MainWindowHandle (window may not have appeared yet) for up to 8s, then applies.
    /// We hold a weak ref to the AccountSummary so subsequent CaptionColorHex changes resolve
    /// live on the next 1.5s tick — no extra wiring needed when the user picks a new color.
    /// </summary>
    public void Track(int pid, AccountSummary summary)
    {
        if (pid <= 0 || summary is null) return;
        var target = new DecoratedTarget(pid, summary);
        _targets[pid] = target;
        // Try once now (window often shows up within the first poll), but the timer covers
        // the slow-start case.
        _ = ApplyOnce(target);
    }

    public void Untrack(int pid)
    {
        _targets.TryRemove(pid, out _);
    }

    /// <summary>
    /// Push the latest title + caption color now for any tracked process whose AccountSummary
    /// matches. Used when the user picks a new color — without this they'd wait up to 1.5s for
    /// the next timer tick. Cheap enough to do synchronously on the UI thread.
    /// </summary>
    public void RefreshAccount(Guid accountId)
    {
        foreach (var target in _targets.Values)
        {
            if (target.Summary.Id == accountId)
            {
                _ = ApplyOnce(target);
            }
        }
    }

    private void ReapplyAll()
    {
        if (_disposed) return;
        foreach (var target in _targets.Values)
        {
            _ = ApplyOnce(target);
        }
    }

    private async Task ApplyOnce(DecoratedTarget target)
    {
        try
        {
            using var process = Process.GetProcessById(target.Pid);
            if (process.HasExited)
            {
                _targets.TryRemove(target.Pid, out _);
                return;
            }
            process.Refresh();
            var hwnd = process.MainWindowHandle;
            if (hwnd == IntPtr.Zero)
            {
                return; // window not up yet; try next tick
            }
            // Resolve fresh from the AccountSummary every tick so user-picked color/name
            // changes propagate without explicit re-tracking.
            ApplyTitle(hwnd, $"Roblox - {target.Summary.DisplayName}");
            ApplyCaptionColor(hwnd, ResolveCaptionColor(target.Summary));
        }
        catch (ArgumentException)
        {
            _targets.TryRemove(target.Pid, out _);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Window decorate threw for pid {Pid}; will retry on next tick.", target.Pid);
        }
        await Task.CompletedTask;
    }

    private static void ApplyTitle(IntPtr hwnd, string title)
    {
        SetWindowTextW(hwnd, title);
    }

    private static void ApplyCaptionColor(IntPtr hwnd, uint color)
    {
        // DwmSetWindowAttribute returns 0 on success. We don't surface failures — older
        // Windows just silently ignores DWMWA_CAPTION_COLOR.
        try
        {
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref color, sizeof(uint));
        }
        catch (DllNotFoundException)
        {
            // dwmapi.dll missing (very old Windows) — abandon the per-window color.
        }
        catch (EntryPointNotFoundException)
        {
            // Pre-Win11 SDK — same outcome.
        }
    }

    private static uint ResolveCaptionColor(AccountSummary summary)
    {
        // Manual override wins. Bad hex (typo, unparseable) silently falls through to
        // auto-derive so the user never lands in a "title bar broken" state.
        if (!string.IsNullOrWhiteSpace(summary.CaptionColorHex)
            && TryParseHex(summary.CaptionColorHex!, out var manual))
        {
            return manual;
        }
        if (summary.IsMain)
        {
            return MainCaptionColor;
        }
        var hash = summary.Id.GetHashCode();
        var idx = ((hash % AutoPalette.Length) + AutoPalette.Length) % AutoPalette.Length;
        return AutoPalette[idx];
    }

    /// <summary>
    /// Convert <c>#rrggbb</c> (or <c>rrggbb</c> bare) to the 0xAARRGGBB COLORREF the DWM
    /// expects. DWMWA_CAPTION_COLOR doesn't read alpha, but we still pack 0xFF into it for
    /// consistency. Returns false on any malformed input.
    /// </summary>
    private static bool TryParseHex(string hex, out uint color)
    {
        color = 0;
        var trimmed = hex.Trim().TrimStart('#');
        if (trimmed.Length != 6) return false;
        if (!uint.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var rgb))
        {
            return false;
        }
        color = 0xFF000000u | rgb;
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _reapplyTimer.Dispose();
        _targets.Clear();
    }

    private sealed record DecoratedTarget(int Pid, AccountSummary Summary);

    // P/Invoke. SetWindowTextW + DwmSetWindowAttribute are stable Win32 surfaces.
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowTextW(IntPtr hWnd, string lpString);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint pvAttribute, int cbAttribute);
}
