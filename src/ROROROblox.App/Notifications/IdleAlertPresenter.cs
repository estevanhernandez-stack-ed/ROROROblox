using ROROROblox.Core;

namespace ROROROblox.App.Notifications;

/// <summary>Turns a coalesced warn-threshold crossing into one mutable tray toast.</summary>
public sealed class IdleAlertPresenter
{
    private readonly ITrayService _tray;
    public IdleAlertPresenter(ITrayService tray) => _tray = tray;

    public void Notify(int crossedCount, int thresholdMinutes, bool muted)
    {
        if (crossedCount <= 0 || muted) return;
        var msg = crossedCount == 1
            ? $"1 account idle > {thresholdMinutes}m — it may reconnect soon."
            : $"{crossedCount} accounts idle > {thresholdMinutes}m — they may reconnect together.";
        // F-034, FOURTH leak site — the row named three and this was not among them, found by
        // re-measuring on 2026-08-20. The memory-warning balloon three files away already titles
        // itself "RoRoRo — memory warning", so the app was announcing itself under two different
        // names in the same notification tray.
        _tray.ShowToast(Branding.ProductName, msg);
    }
}
