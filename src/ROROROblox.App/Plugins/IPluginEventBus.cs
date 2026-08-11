using ROROROblox.Core.Diagnostics;

namespace ROROROblox.App.Plugins;

/// <summary>
/// In-process pub/sub used by PluginHostService to fan out runtime events to subscribed
/// plugins via server-streaming RPCs (SubscribeAccountLaunched, SubscribeAccountExited,
/// SubscribeMutexStateChanged, SubscribeMemoryPressure).
///
/// The App layer raises events here when accounts launch / exit, the singleton-mutex
/// state changes, or the memory watchdog latches a pressure crossing; the host service
/// translates those to wire-shape proto messages and writes them onto each subscriber's
/// per-call <c>Channel&lt;T&gt;</c>.
///
/// Decoupled from PluginHostService so the WPF / launcher code never depends on a proto
/// type, and so tests can raise events without spinning up a live gRPC server.
/// </summary>
public interface IPluginEventBus
{
    event Action<RunningAccountSnapshot>? AccountLaunched;
    event Action<RunningAccountSnapshot, long>? AccountExited; // snapshot + exited-at-unix-ms
    event Action<string>? MutexStateChanged;                   // "On" / "Off" / "Error"

    /// <summary>
    /// One tracked account's memory reading, raised once per account whenever
    /// <see cref="IMemoryWatchdog.PressureCrossed"/> fires. Reuses the Core-layer
    /// <see cref="AccountMemory"/> record rather than minting a bespoke bus type --
    /// unlike <see cref="RunningAccountSnapshot"/> (which exists specifically to decouple
    /// this bus from the proto runtime), AccountMemory already has no proto dependency,
    /// so wrapping it again would just be a second name for the same shape.
    /// </summary>
    event Action<AccountMemory>? MemoryPressure;

    /// <summary>
    /// The host's active palette, raised on every theme application including the one at
    /// startup. Reuses the Core-layer <see cref="ROROROblox.Core.Theming.ResolvedPalette"/>
    /// for the same reason <see cref="MemoryPressure"/> reuses <see cref="AccountMemory"/> —
    /// it already has no proto dependency, so minting a bus-local twin would just be a second
    /// name for one shape.
    /// <para>
    /// Carries resolved colours and never a theme id. A plugin handed an id needs somewhere to
    /// look it up, and the only "somewhere" available was the host's own settings file and
    /// themes folder — which is F-091.
    /// </para>
    /// </summary>
    event Action<ROROROblox.Core.Theming.ResolvedPalette>? ThemeChanged;
}
