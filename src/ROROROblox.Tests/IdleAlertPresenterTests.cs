using System.Collections.Generic;
using ROROROblox.App.Notifications;
using ROROROblox.Core;
using Xunit;

namespace ROROROblox.Tests;

public class IdleAlertPresenterTests
{
    private sealed class FakeTray : ITrayService
    {
        public readonly List<(string title, string message)> Toasts = new();
        public void ShowToast(string title, string message) => Toasts.Add((title, message));

        // remaining ITrayService members — no-op stubs
        public void Show() { }
        public void UpdateStatus(MultiInstanceState state) { }
        public void SetCustomStateIcons(System.Drawing.Icon? on, System.Drawing.Icon? off, System.Drawing.Icon? error) { }
        public void Dispose() { }
        public event System.EventHandler? RequestOpenMainWindow { add { } remove { } }
        public event System.EventHandler? RequestToggleMutex { add { } remove { } }
        public event System.EventHandler? RequestStopAllInstances { add { } remove { } }
        public event System.EventHandler? RequestQuit { add { } remove { } }
        public event System.EventHandler? RequestOpenDiagnostics { add { } remove { } }
        public event System.EventHandler? RequestOpenLogs { add { } remove { } }
        public event System.EventHandler? RequestOpenPreferences { add { } remove { } }
        public event System.EventHandler? RequestActivateMain { add { } remove { } }
        public event System.EventHandler? RequestOpenHistory { add { } remove { } }
        public event System.EventHandler? RequestOpenPlugins { add { } remove { } }
    }

    [Fact]
    public void Notify_Unmuted_MultipleAccounts_ShowsOneCoalescedToast()
    {
        var tray = new FakeTray();
        new IdleAlertPresenter(tray).Notify(crossedCount: 3, thresholdMinutes: 15, muted: false);

        var toast = Assert.Single(tray.Toasts);
        Assert.Contains("3 accounts", toast.message);
        Assert.Contains("15m", toast.message);
    }

    [Fact]
    public void Notify_Muted_ShowsNothing()
    {
        var tray = new FakeTray();
        new IdleAlertPresenter(tray).Notify(crossedCount: 3, thresholdMinutes: 15, muted: true);
        Assert.Empty(tray.Toasts);
    }

    [Fact]
    public void Notify_ZeroCount_ShowsNothing()
    {
        var tray = new FakeTray();
        new IdleAlertPresenter(tray).Notify(crossedCount: 0, thresholdMinutes: 15, muted: false);
        Assert.Empty(tray.Toasts);
    }
}
