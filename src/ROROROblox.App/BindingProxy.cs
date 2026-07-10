using System.Windows;

namespace ROROROblox.App;

/// <summary>
/// Carries a DataContext into places the visual tree can't reach — specifically a
/// <see cref="System.Windows.Controls.ContextMenu"/>, which lives in its own popup tree and cannot
/// bind up to the window's ViewModel via <c>RelativeSource AncestorType=Window</c>.
/// </summary>
/// <remarks>
/// Declared once in the window's resources with <c>Data="{Binding}"</c> — a <see cref="Freezable"/>
/// in a resource dictionary inherits the DataContext through its inheritance context, so
/// <see cref="Data"/> captures the window's DataContext (the ViewModel). ContextMenu items then
/// reach VM commands via <c>{Binding Data.SomeCommand, Source={StaticResource ...}}</c>.
///
/// Why this exists: the account/game/widget context menus previously bound commands via
/// <c>PlacementTarget.DataContext.SomeCommand</c>, which resolves against the ROW's DataContext
/// (an AccountSummary / FavoriteGame) — the VM commands don't live there, so every one silently
/// resolved to null and the menu items no-op'd. WPF binding failures are silent (no crash, no log),
/// so this went unnoticed for the items that had an alternative UI path.
/// </remarks>
public sealed class BindingProxy : Freezable
{
    protected override Freezable CreateInstanceCore() => new BindingProxy();

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new PropertyMetadata(null));
}
