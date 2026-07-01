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
        _tray.ShowToast("ROROROblox", msg);
    }
}
