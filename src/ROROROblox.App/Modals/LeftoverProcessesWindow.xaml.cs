using System.Windows;
using ROROROblox.App.ViewModels;

namespace ROROROblox.App.Modals;

/// <summary>Informational (non-blocking) modal: mutex acquired but leftover Roblox processes
/// exist. Continue is the default; Clean up + continue sets CleanUpRequested so the caller runs
/// the stop-all teardown (with the unsaved-state confirm when windowed clients exist).</summary>
internal partial class LeftoverProcessesWindow : Window
{
    public bool CleanUpRequested { get; private set; }

    public LeftoverProcessesWindow(int windowless, int windowed)
    {
        InitializeComponent();
        BodyText.Text = LeftoverSummary.Format(windowless, windowed);
    }

    private void OnCleanUpClick(object sender, RoutedEventArgs e)
    {
        CleanUpRequested = true;
        DialogResult = true;
        Close();
    }

    private void OnContinueClick(object sender, RoutedEventArgs e)
    {
        CleanUpRequested = false;
        DialogResult = true;
        Close();
    }
}
