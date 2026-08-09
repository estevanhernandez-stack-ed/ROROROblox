using ROROROblox.App.Tray;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// F-001 — the tray and the Tools menu now share implementations, so a transposed subscription is
/// a silent behaviour swap rather than a compile error. This asserts the wiring table itself.
/// <para>
/// It also fails when a new tray event is added with no handler, which is the failure that rots
/// quietly: nothing else in the app notices an event nobody subscribed to.
/// </para>
/// </summary>
public class TrayWiringTests
{
    private static (RecordingTray Tray, List<string> Fired) Connect()
    {
        var tray = new RecordingTray();
        var fired = new List<string>();
        var handlers = new TrayHandlers(
            OpenMainWindow: () => fired.Add(nameof(TrayHandlers.OpenMainWindow)),
            ToggleMutex: () => fired.Add(nameof(TrayHandlers.ToggleMutex)),
            StopAllInstances: () => fired.Add(nameof(TrayHandlers.StopAllInstances)),
            Quit: () => fired.Add(nameof(TrayHandlers.Quit)),
            OpenDiagnostics: () => fired.Add(nameof(TrayHandlers.OpenDiagnostics)),
            OpenLogs: () => fired.Add(nameof(TrayHandlers.OpenLogs)),
            OpenPreferences: () => fired.Add(nameof(TrayHandlers.OpenPreferences)),
            ActivateMain: () => fired.Add(nameof(TrayHandlers.ActivateMain)),
            OpenHistory: () => fired.Add(nameof(TrayHandlers.OpenHistory)),
            OpenPlugins: () => fired.Add(nameof(TrayHandlers.OpenPlugins)),
            FocusAccount: id => fired.Add($"{nameof(TrayHandlers.FocusAccount)}:{id}"));

        TrayWiring.Connect(tray, handlers);
        return (tray, fired);
    }

    [Theory]
    [InlineData(nameof(RecordingTray.RaiseOpenMainWindow), nameof(TrayHandlers.OpenMainWindow))]
    [InlineData(nameof(RecordingTray.RaiseToggleMutex), nameof(TrayHandlers.ToggleMutex))]
    [InlineData(nameof(RecordingTray.RaiseStopAllInstances), nameof(TrayHandlers.StopAllInstances))]
    [InlineData(nameof(RecordingTray.RaiseQuit), nameof(TrayHandlers.Quit))]
    [InlineData(nameof(RecordingTray.RaiseOpenDiagnostics), nameof(TrayHandlers.OpenDiagnostics))]
    [InlineData(nameof(RecordingTray.RaiseOpenLogs), nameof(TrayHandlers.OpenLogs))]
    [InlineData(nameof(RecordingTray.RaiseOpenPreferences), nameof(TrayHandlers.OpenPreferences))]
    [InlineData(nameof(RecordingTray.RaiseActivateMain), nameof(TrayHandlers.ActivateMain))]
    [InlineData(nameof(RecordingTray.RaiseOpenHistory), nameof(TrayHandlers.OpenHistory))]
    [InlineData(nameof(RecordingTray.RaiseOpenPlugins), nameof(TrayHandlers.OpenPlugins))]
    public void EachTrayEventReachesItsOwnHandler(string raiseMethod, string expectedHandler)
    {
        var (tray, fired) = Connect();

        typeof(RecordingTray).GetMethod(raiseMethod)!.Invoke(tray, null);

        Assert.Equal(new[] { expectedHandler }, fired);
    }

    [Fact]
    public void FocusAccountCarriesItsAccountId()
    {
        var (tray, fired) = Connect();
        var id = Guid.NewGuid();

        tray.RaiseFocusAccount(id);

        Assert.Equal(new[] { $"{nameof(TrayHandlers.FocusAccount)}:{id}" }, fired);
    }

    [Fact]
    public void EveryTrayRequestEventHasAHandler()
    {
        // Guards the rot case: a new Request* event added to ITrayService with nothing subscribed.
        var events = typeof(ITrayService).GetEvents()
            .Select(e => e.Name)
            .Where(n => n.StartsWith("Request", StringComparison.Ordinal))
            .Select(n => n["Request".Length..])
            .ToList();

        var handlerNames = typeof(TrayHandlers).GetProperties().Select(p => p.Name).ToHashSet();

        // ITrayService names each event Request<X>; TrayHandlers names each delegate <X>. That
        // one-to-one correspondence is the invariant — if a future event breaks it, rename the
        // handler to match rather than adding a mapping table here, or this test stops meaning
        // anything.
        var missing = events.Where(e => !handlerNames.Contains(e)).ToList();

        Assert.Empty(missing);
    }
}

internal sealed class RecordingTray : ITrayService
{
    public event EventHandler? RequestOpenMainWindow;
    public event EventHandler? RequestToggleMutex;
    public event EventHandler? RequestStopAllInstances;
    public event EventHandler? RequestQuit;
    public event EventHandler? RequestOpenDiagnostics;
    public event EventHandler? RequestOpenLogs;
    public event EventHandler? RequestOpenPreferences;
    public event EventHandler? RequestActivateMain;
    public event EventHandler? RequestOpenHistory;
    public event EventHandler? RequestOpenPlugins;
    public event EventHandler<Guid>? RequestFocusAccount;

    public void RaiseOpenMainWindow() => RequestOpenMainWindow?.Invoke(this, EventArgs.Empty);
    public void RaiseToggleMutex() => RequestToggleMutex?.Invoke(this, EventArgs.Empty);
    public void RaiseStopAllInstances() => RequestStopAllInstances?.Invoke(this, EventArgs.Empty);
    public void RaiseQuit() => RequestQuit?.Invoke(this, EventArgs.Empty);
    public void RaiseOpenDiagnostics() => RequestOpenDiagnostics?.Invoke(this, EventArgs.Empty);
    public void RaiseOpenLogs() => RequestOpenLogs?.Invoke(this, EventArgs.Empty);
    public void RaiseOpenPreferences() => RequestOpenPreferences?.Invoke(this, EventArgs.Empty);
    public void RaiseActivateMain() => RequestActivateMain?.Invoke(this, EventArgs.Empty);
    public void RaiseOpenHistory() => RequestOpenHistory?.Invoke(this, EventArgs.Empty);
    public void RaiseOpenPlugins() => RequestOpenPlugins?.Invoke(this, EventArgs.Empty);
    public void RaiseFocusAccount(Guid id) => RequestFocusAccount?.Invoke(this, id);

    // Remaining ITrayService members are no-ops for wiring tests, copied verbatim from the
    // existing FakeTrayService reference (src/ROROROblox.Tests/MainViewModelTests.cs).
    public void Show() { }
    public void UpdateStatus(MultiInstanceState state) { }
    public void ShowToast(string title, string message) { }
    public void SetMemoryWarning(bool active) { }
    public void ShowMemoryWarning(string title, string message, Guid accountId) { }
    public void Dispose() { }
}
