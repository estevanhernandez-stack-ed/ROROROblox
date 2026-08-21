using System.ComponentModel;
using ROROROblox.Core.StreamerMode;

namespace ROROROblox.App.Tray;

/// <summary>
/// A bindable view of streamer mode, so the tray's checkable menu item can be BOUND rather than
/// clicked (F-102).
/// </summary>
/// <remarks>
/// <para>
/// The tray entry had the same defect the Settings checkbox did, and it was found by asking the
/// question F-102's row said to ask. Measured with a real automation peer:
/// <c>MenuItemAutomationPeer.Toggle()</c> raises <b>zero</b> Click events, exactly like
/// <c>ToggleButtonAutomationPeer</c>. The tray's handler was defensive in the right way — it read
/// the provider's state rather than the menu item's own auto-toggled <c>IsChecked</c> — and it
/// still never ran for an automation caller, because nothing ran at all.
/// </para>
/// <para>
/// Binding is immune to that split: every path, click or programmatic or automation, sets the
/// dependency property, and the binding carries it to the source.
/// </para>
/// </remarks>
internal sealed class StreamerModeFlag : INotifyPropertyChanged
{
    private readonly IStreamerIdentityProvider _provider;

    public StreamerModeFlag(IStreamerIdentityProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));

        // The provider is the source of truth and can be flipped from Settings, a plugin, or here.
        // Re-raising on its Changed keeps all three views showing one answer.
        _provider.Changed += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(On)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool On
    {
        get => _provider.IsActive;
        set
        {
            // Same equality guard the view model's StreamerModeOn carries: a two-way binding echoes
            // the current value back on every change notification, and without this each echo would
            // start another write.
            if (_provider.IsActive == value) return;
            _ = _provider.SetActiveAsync(value);
        }
    }
}
