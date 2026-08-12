using System.IO;
using Microsoft.Extensions.Logging;

namespace ROROROblox.App.Plugins;

public sealed class PluginProcessSupervisor
{
    private readonly IPluginProcessStarter _starter;
    private readonly ILogger<PluginProcessSupervisor>? _log;
    private readonly Dictionary<string, int> _pidByPluginId = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    /// <summary>
    /// Raised when a plugin process exits — kill, crash, or graceful shutdown.
    /// The supervisor strips the PID-to-plugin-id mapping before invoking, so
    /// subscribers can safely query <see cref="RunningPids"/> for fresh state.
    /// Crash-detection sits on top of this event in later items.
    /// </summary>
    public event Action<string, int>? PluginExited;

    public PluginProcessSupervisor(IPluginProcessStarter starter, ILogger<PluginProcessSupervisor>? log = null)
    {
        _starter = starter ?? throw new ArgumentNullException(nameof(starter));
        _log = log;
        _starter.ProcessExited += OnProcessExited;
    }

    public IReadOnlyDictionary<string, int> RunningPids
    {
        get { lock (_lock) { return new Dictionary<string, int>(_pidByPluginId); } }
    }

    public void StartAutostart(IEnumerable<InstalledPlugin> plugins)
    {
        foreach (var plugin in plugins)
        {
            if (!plugin.Consent.AutostartEnabled) continue;
            StartOne(plugin);
        }
    }

    public void Restart(InstalledPlugin plugin)
    {
        _log?.LogInformation("Restarting plugin {PluginId}.", plugin.Manifest.Id);
        Stop(plugin.Manifest.Id);
        StartOne(plugin);
    }

    /// <summary>
    /// Start a single plugin process now, outside the autostart sweep. If the plugin is
    /// already running it's restarted (stop + start) so the caller always ends with a
    /// fresh process. The install flow calls this right after consent is granted so a
    /// freshly-installed plugin runs without a RoRoRo restart — autostart governs future
    /// launches, this governs "now".
    /// </summary>
    public void Start(InstalledPlugin plugin)
    {
        // Check under the lock, act outside it — same minimal-hold pattern as StartOne /
        // OnProcessExited (keep the Process.Start syscall off the lock). The window between
        // lock-release and Restart/StartOne is safe: Stop is idempotent (a TryGetValue miss
        // is a no-op), and no other caller reaches StartOne concurrently (autostart is a
        // fire-once startup sweep).
        bool alreadyRunning;
        lock (_lock) { alreadyRunning = _pidByPluginId.ContainsKey(plugin.Manifest.Id); }
        if (alreadyRunning)
        {
            Restart(plugin);
        }
        else
        {
            StartOne(plugin);
        }
    }

    /// <summary>
    /// Stop every plugin process — tracked OR orphaned — running out of
    /// <paramref name="installDir"/>, then wait for them to actually exit so their file
    /// handles release. The install flow calls this before wiping a plugin's dir:
    /// <see cref="Stop"/> alone only kills what THIS session tracks, but an orphan — a
    /// plugin process that outlived the RoRoRo session that launched it — holds its DLLs
    /// locked and would block the re-extract. Found by image path so orphans can't hide.
    /// Polls until the dir is clear rather than sleeping a fixed interval.
    /// </summary>
    public async Task StopByInstallDirAsync(string pluginId, string installDir)
    {
        // Clean tracked stop first — kills + drops the PID mapping so OnProcessExited
        // doesn't fire a spurious "plugin stopped" banner mid-install.
        Stop(pluginId);

        // Sweep for anything else running out of the dir — orphans the PID map never knew.
        var orphans = _starter.FindRunningUnder(installDir);
        if (orphans.Count > 0)
        {
            _log?.LogWarning(
                "Killing {Count} orphan process(es) running under {InstallDir} (pids: {Pids}).",
                orphans.Count, installDir, string.Join(", ", orphans));
        }
        foreach (var pid in orphans)
        {
            _starter.Kill(pid);
        }

        // Killing only signals termination; the OS releases file handles a beat later.
        // Poll until nothing runs from the dir (or give up after a bounded wait).
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (_starter.FindRunningUnder(installDir).Count > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50).ConfigureAwait(false);
        }

        // Still alive after the ceiling — the dir can't be safely wiped. Fail loud with an
        // actionable message instead of letting the caller hit a bare "access denied".
        if (_starter.FindRunningUnder(installDir).Count > 0)
        {
            _log?.LogError(
                "Plugin {PluginId} process(es) under {InstallDir} still running after 5s kill wait; refusing dir wipe.",
                pluginId, installDir);
            throw new TimeoutException(
                $"A '{pluginId}' process is still running and won't exit — close it, then try again.");
        }
    }

    /// <summary>
    /// Kills plugin processes left over from a PREVIOUS RoRoRo session, before autostart adds more.
    /// Returns how many it killed.
    /// <para>
    /// F-101. <c>PluginJobObject</c> is the real fix and covers a host that crashes; this covers the
    /// two things a job object cannot. A pile that already exists on disk from versions before the
    /// job shipped would otherwise survive forever, because the only existing sweep
    /// (<see cref="StopByInstallDirAsync"/>) runs on install and uninstall — when a plugin's FILES
    /// need unlocking — and never at session boundaries, which is when orphans are made. And the
    /// microseconds between <c>Process.Start</c> and job assignment are a real if narrow window.
    /// </para>
    /// <para>
    /// Scoped to the plugins root by image path, the same way <see cref="StopByInstallDirAsync"/>
    /// finds orphans, so it can only ever kill something running out of RoRoRo's own plugins
    /// directory. It runs BEFORE autostart, so nothing it kills is something this session started.
    /// </para>
    /// </summary>
    public int SweepOrphans(string pluginsRoot)
    {
        if (string.IsNullOrWhiteSpace(pluginsRoot) || !Directory.Exists(pluginsRoot)) return 0;

        IReadOnlyList<int> orphans;
        try
        {
            orphans = _starter.FindRunningUnder(pluginsRoot);
        }
        catch (Exception ex)
        {
            // Never let a cleanup sweep stop the app starting.
            _log?.LogWarning(ex, "Orphan sweep could not enumerate processes under {Root}.", pluginsRoot);
            return 0;
        }

        if (orphans.Count == 0) return 0;

        _log?.LogWarning(
            "Killing {Count} plugin process(es) left over from a previous session (pids: {Pids}). "
            + "These outlived the RoRoRo that started them; see F-101.",
            orphans.Count, string.Join(", ", orphans));

        var killed = 0;
        foreach (var pid in orphans)
        {
            try
            {
                _starter.Kill(pid);
                killed++;
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "Could not kill orphan plugin pid {Pid}.", pid);
            }
        }

        return killed;
    }

    /// <summary>
    /// Kill the running plugin process for <paramref name="pluginId"/> if one is tracked.
    /// No-op when the plugin isn't running. Called by Revoke so a plugin that was active
    /// when consent is removed actually goes away (rather than walking zombie until the
    /// host restarts).
    /// </summary>
    public void Stop(string pluginId)
    {
        lock (_lock)
        {
            if (_pidByPluginId.TryGetValue(pluginId, out var pid))
            {
                _log?.LogInformation("Stopping plugin {PluginId} (pid {Pid}).", pluginId, pid);
                _starter.Kill(pid);
                _pidByPluginId.Remove(pluginId);
            }
        }
    }

    public void StopAll()
    {
        lock (_lock)
        {
            foreach (var pid in _pidByPluginId.Values)
            {
                _starter.Kill(pid);
            }
            _pidByPluginId.Clear();
        }
    }

    private void StartOne(InstalledPlugin plugin)
    {
        var pid = _starter.Start(plugin.Manifest.Id, plugin.ExecutablePath);
        lock (_lock) { _pidByPluginId[plugin.Manifest.Id] = pid; }
    }

    private void OnProcessExited(int pid)
    {
        // Map PID → plugin id under the lock, then drop the mapping. We hold
        // the lock just long enough to mutate state; PluginExited fires outside
        // the lock so subscribers can re-enter (Restart, query RunningPids,
        // etc.) without deadlocking.
        string? pluginId = null;
        lock (_lock)
        {
            foreach (var kv in _pidByPluginId)
            {
                if (kv.Value == pid)
                {
                    pluginId = kv.Key;
                    break;
                }
            }
            if (pluginId is not null)
            {
                _pidByPluginId.Remove(pluginId);
            }
        }
        if (pluginId is not null)
        {
            PluginExited?.Invoke(pluginId, pid);
        }
    }
}
