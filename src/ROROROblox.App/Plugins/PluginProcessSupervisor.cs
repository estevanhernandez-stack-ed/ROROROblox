namespace ROROROblox.App.Plugins;

public sealed class PluginProcessSupervisor
{
    private readonly IPluginProcessStarter _starter;
    private readonly Dictionary<string, int> _pidByPluginId = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    /// <summary>
    /// Raised when a plugin process exits — kill, crash, or graceful shutdown.
    /// The supervisor strips the PID-to-plugin-id mapping before invoking, so
    /// subscribers can safely query <see cref="RunningPids"/> for fresh state.
    /// Crash-detection sits on top of this event in later items.
    /// </summary>
    public event Action<string, int>? PluginExited;

    public PluginProcessSupervisor(IPluginProcessStarter starter)
    {
        _starter = starter ?? throw new ArgumentNullException(nameof(starter));
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
        Stop(plugin.Manifest.Id);
        StartOne(plugin);
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
