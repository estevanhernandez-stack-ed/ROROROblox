using ROROROblox.App.AppLifecycle;
using System.ComponentModel;
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

    private ExternalCloseWatcher? _externalClose;

    /// <summary>
    /// True when this window was closed by a request from another process (<c>taskkill</c> without
    /// <c>/F</c>, Task Manager's End task, <c>CloseMainWindow</c>), as <see cref="ExternalCloseWatcher"/>
    /// identifies it. The caller reads it after <c>ShowDialog</c> and ends the app instead of treating
    /// the close as the user's answer. taskkill posts to one unowned top-level window, never to an owned
    /// one (measured 2026-11-17), so this is only ever set on a dialog that has no owner.
    /// </summary>
    public bool ClosedFromOutside { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _externalClose ??= new ExternalCloseWatcher(this);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Asked on every attempt so the note is cleared by the close it belongs to.
        if (_externalClose?.TryConsume() ?? false)
        {
            ClosedFromOutside = true;
        }

        base.OnClosing(e);
    }

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
