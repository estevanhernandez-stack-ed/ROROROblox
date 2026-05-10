namespace ROROROblox.App.Plugins;

public sealed class PluginProcessSupervisor
{
    private readonly IPluginProcessStarter _starter;
    private readonly Dictionary<string, int> _pidByPluginId = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public PluginProcessSupervisor(IPluginProcessStarter starter)
    {
        _starter = starter ?? throw new ArgumentNullException(nameof(starter));
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
        lock (_lock)
        {
            if (_pidByPluginId.TryGetValue(plugin.Manifest.Id, out var oldPid))
            {
                _starter.Kill(oldPid);
                _pidByPluginId.Remove(plugin.Manifest.Id);
            }
        }
        StartOne(plugin);
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
}
