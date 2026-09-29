using System.Windows;
using System.Windows.Controls;
using ROROROblox.App.Localization;
using ROROROblox.App.ViewModels;

namespace ROROROblox.App.Modals;

/// <summary>
/// Asks what to do when a <c>JoinViaFriend</c> account can't follow the main: follow another
/// account that is in a joinable game, say the captcha is fixed and join directly, or cancel.
/// Anything but a deliberate pick, including closing the window, is <see cref="FlaggedLaunchChoice.Cancel"/>.
/// </summary>
internal partial class FlaggedLaunchWindow : Window
{
    private FlaggedLaunchChoice _choice = new FlaggedLaunchChoice.Cancel();

    public FlaggedLaunchWindow(FlaggedLaunchAsk ask)
    {
        InitializeComponent();

        var names = string.Join(", ", ask.AccountNames);
        BodyText.Text = ask.AccountNames.Count == 1
            ? Loc.Format("Shell_FlaggedLaunch_Body", names)
            : Loc.Format("Shell_FlaggedLaunch_BodyMany", names);

        foreach (var (name, userId) in ask.JoinableOthers)
        {
            OthersCombo.Items.Add(new ComboBoxItem { Content = name, Tag = userId });
        }

        if (ask.JoinableOthers.Count == 0)
        {
            OthersCombo.Visibility = Visibility.Collapsed;
            NobodyJoinableText.Visibility = Visibility.Visible;
            FollowButton.IsEnabled = false;
        }
        else
        {
            OthersCombo.SelectedIndex = 0;
        }
    }

    private void OnFollowClick(object sender, RoutedEventArgs e)
    {
        if (OthersCombo.SelectedItem is ComboBoxItem { Tag: long userId })
        {
            _choice = new FlaggedLaunchChoice.FollowAccount(userId);
            DialogResult = true;
            Close();
        }
    }

    private void OnItsFixedClick(object sender, RoutedEventArgs e)
    {
        _choice = new FlaggedLaunchChoice.JoinDirectly();
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        _choice = new FlaggedLaunchChoice.Cancel();
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Show the question and return the answer. Owner rule matches
    /// <see cref="StopAllConfirmWindow.Confirm"/>: parent to the main window only when it is loaded
    /// AND visible (the tray-resident app hides it on X-close), otherwise centre on screen.
    /// </summary>
    internal static FlaggedLaunchChoice Ask(FlaggedLaunchAsk ask)
    {
        var dialog = new FlaggedLaunchWindow(ask);
        var owner = Application.Current?.MainWindow;
        if (owner is not null && owner.IsLoaded && owner.IsVisible)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.ShowDialog();
        return dialog._choice;
    }
}
