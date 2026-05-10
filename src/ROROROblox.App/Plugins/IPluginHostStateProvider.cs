namespace ROROROblox.App.Plugins;

/// <summary>
/// Read-only view of host-side state that plugins can observe via GetHostInfo.
/// Implemented by the App layer (e.g. a small adapter over MutexHolder + settings) so
/// PluginHostService stays unaware of WPF / runtime concerns and remains test-friendly.
/// </summary>
public interface IPluginHostStateProvider
{
    /// <summary>True when multi-instance is currently active (mutex held).</summary>
    bool MultiInstanceEnabled { get; }

    /// <summary>"On" / "Off" / "Error" — matches the MutexStateEvent contract.</summary>
    string MultiInstanceState { get; }
}
