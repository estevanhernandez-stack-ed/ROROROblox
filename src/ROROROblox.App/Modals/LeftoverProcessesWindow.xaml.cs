using System.Windows;
using ROROROblox.App.ViewModels;

namespace ROROROblox.App.Modals;

/// <summary>What the LEFTOVER modal's caller should do on close. Replaces a single
/// CleanUpRequested bool now that there are two distinct cleanup lanes — the modal only ever
/// resolves to one of these three.</summary>
internal enum LeftoverCleanupAction
{
    /// <summary>Continue (the default) — no cleanup requested.</summary>
    None,

    /// <summary>Clean up + continue — the existing all-stop teardown (unsaved-state confirm
    /// applies when windowed clients are present).</summary>
    StopAll,

    /// <summary>Clear strays — stops only the windowless orphans, no confirmation. Windowed
    /// clients are left running untouched.</summary>
    ClearStrays,
}

/// <summary>Informational (non-blocking) modal: mutex acquired but leftover Roblox processes
/// exist. Continue is the default. Clean up + continue runs the existing all-stop teardown; Clear
/// strays (shown only when there are windowless orphans) closes just those, with no confirmation
/// — a windowless orphan has nothing to lose, the same reasoning SeamlessTakeover.WindowlessOnly
/// already uses to justify a silent close.</summary>
internal partial class LeftoverProcessesWindow : Window
{
    public LeftoverCleanupAction Action { get; private set; } = LeftoverCleanupAction.None;

    public LeftoverProcessesWindow(int windowless, int windowed)
    {
        InitializeComponent();
        BodyText.Text = LeftoverSummary.Format(windowless, windowed);
        // Clear strays only makes sense — and is only offered — when there's an orphan to clear.
        ClearStraysButton.Visibility = windowless > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnClearStraysClick(object sender, RoutedEventArgs e)
    {
        Action = LeftoverCleanupAction.ClearStrays;
        DialogResult = true;
        Close();
    }

    private void OnCleanUpClick(object sender, RoutedEventArgs e)
    {
        Action = LeftoverCleanupAction.StopAll;
        DialogResult = true;
        Close();
    }

    private void OnContinueClick(object sender, RoutedEventArgs e)
    {
        Action = LeftoverCleanupAction.None;
        DialogResult = true;
        Close();
    }
}
