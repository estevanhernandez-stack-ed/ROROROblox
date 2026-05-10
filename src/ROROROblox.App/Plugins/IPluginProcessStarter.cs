namespace ROROROblox.App.Plugins;

public interface IPluginProcessStarter
{
    /// <summary>Launch the plugin EXE. Returns its process id.</summary>
    int Start(string pluginId, string exePath);
    /// <summary>Terminate the plugin process. No-op if already dead.</summary>
    void Kill(int pid);
}
