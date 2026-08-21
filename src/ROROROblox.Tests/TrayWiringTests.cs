using System.Reflection;
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
        // Guards the rot case: a new Request* event added to ITrayService that TrayWiring.Connect
        // never subscribes to. Naming correspondence alone (ITrayService's Request<X> vs
        // TrayHandlers' <X>) is not enough — RecordingTray must implement any new event to compile,
        // so the suite stays green even if Connect forgets the subscription line. This actually
        // calls TrayWiring.Connect and inspects whether each event got a subscriber.
        //
        // C# field-like events (`public event EventHandler? Foo;`, as RecordingTray declares) are
        // compiler-generated into a private instance field named exactly Foo holding the multicast
        // delegate. An event nobody subscribed to has a null backing field; Connect subscribing to
        // it makes the field non-null. Reflecting over those fields after Connect runs is what
        // "has a handler" now genuinely means.
        var (tray, _) = Connect();

        // OUTBOUND events are excluded by name, and the list is asserted rather than assumed —
        // a typo here would silently drop a real Request event out of the check. StatusChanged
        // reports state TO subscribers (F-018's footer); TrayWiring.Connect has no business
        // subscribing to it, so requiring a handler would be requiring the wrong thing.
        string[] outbound = [nameof(ITrayService.StatusChanged)];
        var allNames = typeof(ITrayService).GetEvents().Select(e => e.Name).ToHashSet();
        Assert.All(outbound, name => Assert.Contains(name, allNames));

        var eventNames = allNames.Except(outbound).ToHashSet();

        // And everything left really is a request, so a future outbound event cannot quietly join
        // the inbound set and be demanded a handler it should never have.
        Assert.All(eventNames, name => Assert.StartsWith("Request", name, StringComparison.Ordinal));

        var backingFields = typeof(RecordingTray)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(f => eventNames.Contains(f.Name))
            .ToList();

        // Sanity check on the reflection technique itself: if this is empty, the field-like-event
        // backing-field assumption stopped holding (e.g. RecordingTray switched to explicit
        // add/remove) and the test below would pass vacuously without saying so.
        Assert.Equal(eventNames.Count, backingFields.Count);

        var unsubscribed = backingFields
            .Where(f => f.GetValue(tray) is null)
            .Select(f => f.Name)
            .ToList();

        Assert.Empty(unsubscribed);
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

    // OUTBOUND, not a request: the tray reporting a state change rather than relaying a click.
    // EveryTrayRequestEventHasAHandler excludes it by name for exactly that reason.
    public event EventHandler<MultiInstanceState>? StatusChanged;

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
    public void RaiseStatusChanged(MultiInstanceState state) => StatusChanged?.Invoke(this, state);

    // Remaining ITrayService members are no-ops for wiring tests, copied verbatim from the
    // existing FakeTrayService reference (src/ROROROblox.Tests/MainViewModelTests.cs).
    public void Show() { }
    public void UpdateStatus(MultiInstanceState state) { }
    // Recorded rather than dropped (F-100): the idle-alert path ends at a toast, and a recorder
    // that discards the only observable effect cannot witness the handler it exists to witness.
    public readonly List<(string Title, string Message)> Toasts = new();
    public void ShowToast(string title, string message) => Toasts.Add((title, message));
    public void SetMemoryWarning(bool active) { }
    public void ShowMemoryWarning(string title, string message, Guid accountId) { }
    public void Dispose() { }
}
