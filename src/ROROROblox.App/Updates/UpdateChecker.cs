using System.IO;
using Velopack;
using Velopack.Sources;

namespace ROROROblox.App.Updates;

/// <summary>
/// Wraps <see cref="UpdateManager"/> with a 24-hour debounce stamp. Spec §9.
/// All failures are silent — auto-update is a comfort feature, not load-bearing.
/// </summary>
internal sealed class UpdateChecker : IUpdateChecker
{
    private const string GitHubRepoUrl = "https://github.com/estevanhernandez-stack-ed/ROROROblox";
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromHours(24);

    private static readonly string DebounceStampPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ROROROblox",
        "last-update-check.txt");

    public async Task CheckForUpdatesAsync()
    {
        if (!ShouldCheck())
        {
            return;
        }

        try
        {
            var source = new GithubSource(GitHubRepoUrl, accessToken: null, prerelease: false);
            var manager = new UpdateManager(source);

            // IsInstalled is false during dev (running from bin/Debug); skip the check there.
            if (!manager.IsInstalled)
            {
                StampNow();
                return;
            }

            // Returns null when no update is available.
            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is not null)
            {
                // Item 10 closes here — no UI surface yet for "Update Available."
                // Item 11 (MSIX/packaging) wires download + apply via the tray menu.
            }
        }
        catch
        {
            // Network failure, no releases yet, malformed feed — all fine, treat as no-update.
        }
        finally
        {
            StampNow();
        }
    }

    private static bool ShouldCheck()
    {
        try
        {
            if (!File.Exists(DebounceStampPath))
            {
                return true;
            }
            var contents = File.ReadAllText(DebounceStampPath).Trim();
            if (!DateTimeOffset.TryParse(contents, out var lastChecked))
            {
                return true;
            }
            return DateTimeOffset.UtcNow - lastChecked >= DebounceWindow;
        }
        catch
        {
            return true;
        }
    }

    private static void StampNow()
    {
        try
        {
            var dir = Path.GetDirectoryName(DebounceStampPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(DebounceStampPath, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch
        {
            // Best-effort; if this fails we'll just check again sooner. Not load-bearing.
        }
    }
}
