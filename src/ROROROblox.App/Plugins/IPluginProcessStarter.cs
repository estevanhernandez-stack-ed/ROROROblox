namespace ROROROblox.App.Plugins;

public interface IPluginProcessStarter
{
    /// <summary>Launch the plugin EXE. Returns its process id.</summary>
    int Start(string pluginId, string exePath);
    /// <summary>Terminate the plugin process. No-op if already dead.</summary>
    void Kill(int pid);

    /// <summary>
    /// Raised when a plugin process exits — whether it was killed, crashed, or
    /// shut down gracefully. The supervisor subscribes to this and raises its
    /// own <c>PluginExited(pluginId, pid)</c> after mapping PID back to the
    /// plugin id. Implementations must marshal the event off the OS-thread the
    /// process-exit notification lands on (e.g. the Process.Exited callback) —
    /// callers expect to be able to take the supervisor lock without re-entry.
    /// </summary>
    event Action<int>? ProcessExited;
}
