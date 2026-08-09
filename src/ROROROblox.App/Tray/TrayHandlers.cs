namespace ROROROblox.App.Tray;

/// <summary>
/// One delegate per <see cref="ROROROblox.Core.ITrayService"/> request event.
/// <para>
/// Delegates rather than the collaborators themselves, so <see cref="TrayWiring.Connect"/> holds no
/// knowledge of what any handler does and stays readable as a table. The table is what
/// <c>TrayWiringTests</c> asserts against.
/// </para>
/// </summary>
internal sealed record TrayHandlers(
    Action OpenMainWindow,
    Action ToggleMutex,
    Action StopAllInstances,
    Action Quit,
    Action OpenDiagnostics,
    Action OpenLogs,
    Action OpenPreferences,
    Action ActivateMain,
    Action OpenHistory,
    Action OpenPlugins,
    Action<Guid> FocusAccount);
