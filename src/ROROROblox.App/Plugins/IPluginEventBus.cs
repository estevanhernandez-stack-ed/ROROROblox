namespace ROROROblox.App.Plugins;

/// <summary>
/// In-process pub/sub used by PluginHostService to fan out runtime events to subscribed
/// plugins via server-streaming RPCs (SubscribeAccountLaunched, SubscribeAccountExited,
/// SubscribeMutexStateChanged).
///
/// The App layer raises events here when accounts launch / exit or the singleton-mutex
/// state changes; the host service translates those to wire-shape proto messages and
/// writes them onto each subscriber's per-call <c>Channel&lt;T&gt;</c>.
///
/// Decoupled from PluginHostService so the WPF / launcher code never depends on a proto
/// type, and so tests can raise events without spinning up a live gRPC server.
/// </summary>
public interface IPluginEventBus
{
    event Action<RunningAccountSnapshot>? AccountLaunched;
    event Action<RunningAccountSnapshot, long>? AccountExited; // snapshot + exited-at-unix-ms
    event Action<string>? MutexStateChanged;                   // "On" / "Off" / "Error"
}
