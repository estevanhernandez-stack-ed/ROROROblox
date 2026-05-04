using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Velopack;
using Velopack.Sources;

namespace ROROROblox.App.Updates;

/// <summary>
/// Wraps <see cref="UpdateManager"/> with a 24-hour debounce stamp. Spec §9.
/// Failures are non-fatal — auto-update is comfort, not load-bearing — but every failure
/// logs at Debug so a support bundle can show whether the check ran at all.
/// </summary>
internal sealed class UpdateChecker : IUpdateChecker
{
    private const string GitHubRepoUrl = "https://github.com/estevanhernandez-stack-ed/ROROROblox";
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromHours(24);

    private static readonly string DebounceStampPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ROROROblox",
        "last-update-check.txt");

    private readonly ILogger<UpdateChecker> _log;

    public UpdateChecker() : this(NullLogger<UpdateChecker>.Instance) { }

    public UpdateChecker(ILogger<UpdateChecker> log)
    {
        _log = log ?? NullLogger<UpdateChecker>.Instance;
    }

    public async Task CheckForUpdatesAsync()
    {
        if (!ShouldCheck())
        {
            _log.LogDebug("Update check debounced (within 24h window).");
            return;
        }

        try
        {
            var source = new GithubSource(GitHubRepoUrl, accessToken: null, prerelease: false);
            var manager = new UpdateManager(source);

            // IsInstalled is false during dev (running from bin/Debug); skip the check there.
            if (!manager.IsInstalled)
            {
                _log.LogDebug("Velopack reports not-installed; skipping update check.");
                StampNow();
                return;
            }

            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is not null)
            {
                _log.LogInformation("Update available: {Version}", update.TargetFullRelease.Version);
                // Item 10 closes here — no UI surface yet for "Update Available."
                // Item 11 (MSIX/packaging) wires download + apply via the tray menu.
            }
            else
            {
                _log.LogDebug("Update check: no update available.");
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Update check threw — treating as no-update.");
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
