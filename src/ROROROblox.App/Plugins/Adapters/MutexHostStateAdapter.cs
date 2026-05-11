using ROROROblox.Core;

namespace ROROROblox.App.Plugins.Adapters;

/// <summary>
/// Maps <see cref="IMutexHolder"/> state onto the plugin-facing
/// <see cref="IPluginHostStateProvider"/> contract. v1.4 emits "On" / "Off" only —
/// the third state "Error" is reachable via the bus when <see cref="IMutexHolder.MutexLost"/>
/// fires (App.WirePluginEventBus raises <c>RaiseMutexStateChanged("Error")</c> there).
/// A snapshot read of the property here just reflects current ownership.
/// </summary>
public sealed class MutexHostStateAdapter : IPluginHostStateProvider
{
    private readonly IMutexHolder _mutex;

    public MutexHostStateAdapter(IMutexHolder mutex)
    {
        _mutex = mutex ?? throw new ArgumentNullException(nameof(mutex));
    }

    public bool MultiInstanceEnabled => _mutex.IsHeld;

    public string MultiInstanceState => _mutex.IsHeld ? "On" : "Off";
}
