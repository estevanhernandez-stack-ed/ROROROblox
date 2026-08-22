using System.Windows.Input;

namespace ROROROblox.App.Input;

/// <summary>What a shortcut does — the id both windows and the About card key on.</summary>
internal enum ShortcutAction
{
    AddAccount,
    LaunchAll,
    SquadLaunch,
    FocusFilter,
    OpenGames,
    OpenSettings,
    OpenHistory,
    OpenDiagnostics,
    OpenPlugins,
    OpenShortcutsList,
}

/// <summary>One row of the vocabulary: the gesture, its display text, and what it means.</summary>
internal sealed record KeyboardShortcut(
    Key Key, ModifierKeys Modifiers, string GestureText, ShortcutAction Action, string Label);

/// <summary>
/// The app's one keyboard vocabulary (F-112). Before this, RoRoRo had no shortcuts at all —
/// zero <c>InputBindings</c> across every XAML — so nothing on the toolbar or in Tools was
/// reachable without a mouse beyond raw tab-order. This table is the single source of truth:
/// <see cref="MainWindow"/> and the shell build their <c>KeyBinding</c>s from it, the Tools menu's
/// <c>InputGestureText</c> hints are fenced against it, and the About page renders the
/// discoverable list from it — so the list a user reads, the hint a menu shows, and the binding
/// that fires cannot drift apart.
/// <para>
/// WHAT IS DELIBERATELY ABSENT, so nobody "completes" it:
/// <b>Stop all</b> takes no shortcut — it is the destructive mass action, and the same reasoning
/// that keeps it a separator away from Plugins in the Tools menu (F-001 group 2: distance is
/// cheaper than a dialog) says a hotkey that can fire it by typo is the wrong direction, confirm
/// or no confirm. Per-row actions (Launch As, Stop, Recycle) take none — they need a row to point
/// at, which is what focus and the mouse are for. And the vocabulary stays this small on purpose:
/// a list nobody can hold in their head is noise wearing a feature's clothes.
/// </para>
/// <para>
/// <b>Ctrl+Shift+R / P / A / L are reserved and must never appear here.</b> They are Ur Task's
/// GLOBAL hotkeys (win32 <c>RegisterHotKey</c>), which win system-wide — a binding on one of them
/// would look dead the moment the plugin runs, and only when it runs, which is the worst kind of
/// intermittent to hand a user. Fenced by <c>KeyboardVocabularyTests</c>.
/// </para>
/// </summary>
internal static class KeyboardVocabulary
{
    /// <summary>
    /// Ur Task's global hotkeys (HotkeyService.cs in that repo). See class remarks — these are
    /// reserved system-wide while the plugin runs, and the fence keeps this table off them.
    /// </summary>
    internal static readonly (Key Key, ModifierKeys Modifiers)[] ReservedByPluginGlobalHotkeys =
    [
        (Key.R, ModifierKeys.Control | ModifierKeys.Shift),
        (Key.P, ModifierKeys.Control | ModifierKeys.Shift),
        (Key.A, ModifierKeys.Control | ModifierKeys.Shift),
        (Key.L, ModifierKeys.Control | ModifierKeys.Shift),
    ];

    /// <summary>
    /// The vocabulary. Actions first, destinations second, help last — the order the About card
    /// renders. Gesture text is stored, not derived, because it is user-facing copy ("Ctrl+,"
    /// reads better than "Ctrl+OemComma") — and a test asserts text and gesture stay in agreement.
    /// </summary>
    internal static readonly IReadOnlyList<KeyboardShortcut> Shortcuts =
    [
        new(Key.N, ModifierKeys.Control, "Ctrl+N", ShortcutAction.AddAccount, "Add account"),
        new(Key.L, ModifierKeys.Control, "Ctrl+L", ShortcutAction.LaunchAll, "Launch multiple"),
        new(Key.J, ModifierKeys.Control, "Ctrl+J", ShortcutAction.SquadLaunch, "Squad Launch"),
        new(Key.F, ModifierKeys.Control, "Ctrl+F", ShortcutAction.FocusFilter, "Filter accounts"),
        new(Key.G, ModifierKeys.Control, "Ctrl+G", ShortcutAction.OpenGames, "Games"),
        new(Key.OemComma, ModifierKeys.Control, "Ctrl+,", ShortcutAction.OpenSettings, "Settings"),
        new(Key.H, ModifierKeys.Control, "Ctrl+H", ShortcutAction.OpenHistory, "History"),
        new(Key.D, ModifierKeys.Control, "Ctrl+D", ShortcutAction.OpenDiagnostics, "Diagnostics"),
        new(Key.P, ModifierKeys.Control, "Ctrl+P", ShortcutAction.OpenPlugins, "Plugins"),
        new(Key.F1, ModifierKeys.None, "F1", ShortcutAction.OpenShortcutsList, "Keyboard shortcuts (this list)"),
    ];

    /// <summary>
    /// Build the window-level bindings from the table. Both windows call this with their own
    /// action map; an action a window does not map is skipped rather than bound to nothing —
    /// the shell, for instance, has no account filter to focus.
    /// </summary>
    internal static IEnumerable<KeyBinding> BuildBindings(
        Func<ShortcutAction, ICommand?> map)
    {
        foreach (var shortcut in Shortcuts)
        {
            if (map(shortcut.Action) is { } command)
            {
                yield return new KeyBinding(command, shortcut.Key, shortcut.Modifiers);
            }
        }
    }
}
