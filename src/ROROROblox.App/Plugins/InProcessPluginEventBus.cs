namespace ROROROblox.App.Plugins;

/// <summary>
/// Default in-process implementation of <see cref="IPluginEventBus"/>. The App layer
/// raises events via the public Raise* methods; PluginHostService subscribes to the
/// matching events and forwards them to each plugin's gRPC stream.
///
/// Single-process by design — plugins live in separate processes but reach the bus
/// through gRPC subscription RPCs, not directly. v1 keeps the bus deliberately simple
/// (synchronous Action invoke); ordering across event kinds is best-effort.
/// </summary>
public sealed class InProcessPluginEventBus : IPluginEventBus
{
    public event Action<RunningAccountSnapshot>? AccountLaunched;
    public event Action<RunningAccountSnapshot, long>? AccountExited;
    public event Action<string>? MutexStateChanged;

    public void RaiseAccountLaunched(RunningAccountSnapshot snapshot)
        => AccountLaunched?.Invoke(snapshot);

    public void RaiseAccountExited(RunningAccountSnapshot snapshot, long exitedAtUnixMs)
        => AccountExited?.Invoke(snapshot, exitedAtUnixMs);

    public void RaiseMutexStateChanged(string state)
        => MutexStateChanged?.Invoke(state);
}
