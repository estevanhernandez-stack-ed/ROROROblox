using System.IO;
using Microsoft.Extensions.Logging;

namespace ROROROblox.Core;

/// <summary>
/// Per-capture user-data folder allocator for WebView2-backed cookie capture. Each
/// <see cref="AllocateNew"/> returns a fresh GUID-named subdirectory under <see cref="Root"/>
/// so a second Add Account never reuses the previous capture's session state.
///
/// Why this exists: msedgewebview2.exe child processes outlive the WPF window that owned them.
/// The pre-1.3.4 design wiped a single shared <c>webview2-data\</c> dir on each capture and
/// silently swallowed the IOException raised when the previous capture's children still pinned
/// files. The next CoreWebView2Environment then booted against the leftover state, the user
/// arrived at roblox.com already "logged in," and Account #1's <c>.ROBLOSECURITY</c> was
/// re-captured. Per-capture dirs sidestep the lifecycle race entirely. Lands the v1.2 promise
/// of per-account profiles a cycle early — when v1.2 ships persistent per-account dirs this
/// reduces to "use the persistent dir instead of allocating a new one."
/// </summary>
public sealed class WebView2UserDataDirectory
{
    private readonly string _root;
    private readonly ILogger<WebView2UserDataDirectory> _log;

    public WebView2UserDataDirectory(string root, ILogger<WebView2UserDataDirectory> log)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Root path that holds per-capture subdirectories.</summary>
    public string Root => _root;

    /// <summary>
    /// Allocate a fresh per-capture user-data folder under <see cref="Root"/>. Creates the
    /// directory eagerly so the caller can hand the absolute path to
    /// <c>CoreWebView2Environment.CreateAsync</c>.
    /// </summary>
    public string AllocateNew()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Best-effort delete of every sibling subdirectory under <see cref="Root"/>, optionally
    /// excluding one (typically the dir just allocated for the current capture). Failures are
    /// logged but never thrown — a leftover dir is harmless on its own; what mattered was that
    /// we stopped reusing it for the next capture.
    /// </summary>
    public void SweepStale(string? exclude = null)
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        string? excludeFull = exclude is null ? null : Path.GetFullPath(exclude);

        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(_root);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "WebView2 user-data sweep: enumerating {Root} failed.", _root);
            return;
        }

        foreach (var dir in children)
        {
            if (excludeFull is not null
                && string.Equals(Path.GetFullPath(dir), excludeFull, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException ex)
            {
                // Stale handles from a still-draining msedgewebview2.exe child process — exactly
                // the case this whole class exists to make harmless. Leftover dir gets swept on
                // the next capture or app start.
                _log.LogWarning(
                    ex,
                    "WebView2 user-data sweep: couldn't delete {Dir} (likely stale msedgewebview2 handles). Will retry next sweep.",
                    dir);
            }
            catch (UnauthorizedAccessException ex)
            {
                _log.LogWarning(ex, "WebView2 user-data sweep: access denied on {Dir}.", dir);
            }
        }
    }
}
