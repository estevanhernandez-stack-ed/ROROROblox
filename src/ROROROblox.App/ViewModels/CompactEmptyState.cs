namespace ROROROblox.App.ViewModels;

/// <summary>
/// Three discrete empty-state variants for compact mode. The MainWindow XAML pivots a
/// <c>ContentControl</c> on this so the empty area never looks broken — each variant has its
/// own intentional CTA.
/// </summary>
public enum CompactEmptyState
{
    /// <summary>Main account is set + idle. Show a "Start [Username]" CTA.</summary>
    StartMain,

    /// <summary>Accounts exist but none is marked main. Show a hint to expand and pick one.</summary>
    NoMainPicked,

    /// <summary>No accounts saved. Show "+ Add your first account."</summary>
    NoAccounts,
}
