using System.Windows;
using System.Windows.Controls;
using ROROROblox.App.ViewModels;

namespace ROROROblox.App.Games;

/// <summary>
/// The Library's Play as picker: a themed <see cref="ContextMenu"/> over the roster. It knows nothing
/// about launching; it raises <see cref="AccountPicked"/> and the page hands the pick to
/// <see cref="LibraryViewModel.PlayAsync"/>. Expired accounts are disabled in XAML (<c>CanPlayAs</c>), so
/// the event never fires for one; the view model still answers a forced call (the launch path reports the
/// dead cookie).
/// </summary>
internal partial class AccountPickerFlyout : ContextMenu
{
    public AccountPickerFlyout() => InitializeComponent();

    /// <summary>The user chose this account in the picker.</summary>
    public event EventHandler<AccountSummary>? AccountPicked;

    private void OnItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: AccountSummary account })
        {
            AccountPicked?.Invoke(this, account);
        }

        e.Handled = true;
    }
}
