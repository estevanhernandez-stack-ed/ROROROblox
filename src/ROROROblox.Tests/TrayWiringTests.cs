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

    /// <summary>
    /// TrayService's own source, stripped of comments. The source-scan clauses below need it in both
    /// directions: the class's doc comments legitimately NAME <c>ShowBalloonTip</c> and
    /// <c>ShowMemoryWarning</c> while explaining why neither is there any more, and a guard that
    /// reads prose as code reports on something other than what it claims — the lesson
    /// <c>SettingsReachabilityTests.WithoutComments</c> was written for and this file now reuses.
    /// </summary>
    private static string TrayServiceCode()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate ROROROblox.slnx above the test assembly.");
        var path = Path.Combine(root!, "src", "ROROROblox.App", "Tray", "TrayService.cs");
        return string.Join('\n', SettingsReachabilityTests.WithoutComments(path, File.ReadAllLines(path)));
    }

    [Fact]
    public void TheNotificationInTrayService_IsRaisedOnTheUiThread()
    {
        // A source scan because TrayService needs a real WPF TaskbarIcon and cannot be constructed in a
        // test. What it guards is a measured defect (2026-09-11): ShowToast touched the taskbar icon
        // directly while two siblings in the same class marshalled, and its callers are NOT on the UI
        // thread — AlertDispatcher.DispatchAsync runs fire-and-forget from MetricReportSinkAdapter's
        // event, raised on a thread-pool timer thread when a metric grouping window closes (a gRPC
        // handler thread until 2026-09-15). Touching a FrameworkElement from there throws, the
        // dispatcher swallows it into one Warning, and the alert vanishes: no toast, no visible error.
        //
        // Worse than one lost toast, which is why this is a fence and not a comment: the dispatcher's
        // fan-out loop is sequential and stamps the cooldown inside it, per destination, so a throw ends
        // the loop and every destination after Local loses its send too.
        //
        // RETARGETED 2026-10-06 (v1.33 item 4). It read ShowBalloonTip and asserted exactly two calls
        // — ShowToast's and ShowMemoryWarning's. Both are gone: the balloon is ours now
        // (ShowCustomBalloon) and ShowMemoryWarning went with the shell balloon it wrapped. The
        // THREAD-AFFINITY hazard did not change at all, which is the reason this clause follows the
        // call instead of retiring with it — constructing an AlertBalloon off the UI thread throws
        // exactly as touching the taskbar icon did, and gets swallowed exactly as quietly.
        var source = TrayServiceCode();

        var calls = source.Split("ShowCustomBalloon").Length - 1;
        Assert.Equal(1, calls);     // ShowToast's, and only ShowToast's; a second needs its own answer here

        const string method = "public void ShowToast(";
        var start = source.IndexOf(method, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{method} is gone from TrayService — this fence needs updating.");

        var body = source[start..];
        var marshal = body.IndexOf("Dispatcher.Invoke", StringComparison.Ordinal);
        Assert.True(marshal >= 0,
            $"{method} does not marshal to the UI thread at all. Its callers are not on that thread "
            + "and the exception is swallowed downstream, so the alert would disappear silently.");

        // INSIDE THE LAMBDA, not merely after the word "Dispatcher.Invoke" — and this clause is
        // stronger than the one it replaced because the weaker version was MEASURED not to bite.
        // The old fence asked whether ShowBalloonTip appeared later in the file than the marshal
        // did, which every call does: moving the show call out of the lambda and leaving it on the
        // next line satisfies "later in the text" perfectly while running on the caller's thread.
        // Tried on 2026-10-06 against the retargeted version of this clause; it stayed green.
        Assert.Contains("ShowCustomBalloon", DelimitedSpan(body, marshal), StringComparison.Ordinal);
    }

    /// <summary>
    /// The source from <paramref name="from"/> through the delimiter that closes the construct
    /// starting there — the body of the <c>Dispatcher.Invoke(() =&gt; { … })</c> call, brackets
    /// balanced, so "is this statement inside the marshal" is a real question rather than a
    /// question about line order.
    /// </summary>
    private static string DelimitedSpan(string source, int from)
    {
        var depth = 0;
        var opened = false;
        for (var i = from; i < source.Length; i++)
        {
            if (source[i] is '(' or '{') { depth++; opened = true; }
            else if (source[i] is ')' or '}') { depth--; }
            if (opened && depth == 0) return source[from..(i + 1)];
        }

        Assert.Fail("Bracket scan ran off the end of TrayService.cs — the file does not parse the "
            + "way this fence assumes, so its verdict means nothing.");
        return "";
    }

    [Fact]
    public void TrayServiceNoLongerShowsTheShellBalloon()
    {
        // The deliberate absence, and the reason item 4 exists at all: ShowBalloonTip ALWAYS plays the
        // system notification sound. BalloonFlags.NoSound is in the shipped Hardcodet.NotifyIcon.Wpf
        // 2.0.1 but only behind a NON-PUBLIC overload (verified by reflection 2026-10-05), so there is
        // no supported way to ask the shell for a quiet one. A single ShowBalloonTip creeping back in
        // would take the sound setting (item 5) out of the user's hands for that one alert kind and
        // nothing else would notice.
        var source = TrayServiceCode();

        Assert.DoesNotContain("ShowBalloonTip", source, StringComparison.Ordinal);

        // Vacuity floor. Both clauses here read one file off a filesystem walk, and a walk that
        // returned an empty string would pass this and the equality above for free.
        Assert.Contains("_taskbarIcon", source, StringComparison.Ordinal);
        Assert.Contains("public void ShowToast(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TrayServiceKeepsNoRememberedAccountForABalloonClick()
    {
        // The field that made item 3's regression invisible. _lastMemoryWarningAccountId was
        // app-lifetime state holding "which account the balloon currently on screen is about", which
        // meant every unrelated notification had to CLEAR it — and when the collapse routed every
        // balloon through ShowToast, that clearing silently turned click-to-focus off. No test went
        // red, because no signature mentioned an account.
        //
        // The account rides on the AlertBalloon instance now. This fails if anyone reintroduces the
        // shape, including under a new name: the rule is that the tray holds no account between
        // notifications, and the shell's payload-free click event has no subscriber to need one.
        var source = TrayServiceCode();

        Assert.DoesNotContain("_lastMemoryWarningAccountId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TrayBalloonTipClicked", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public void ShowMemoryWarning(", source, StringComparison.Ordinal);

        // What SHOULD be there: the per-balloon subscription that replaced it.
        Assert.Contains("FocusRequested", source, StringComparison.Ordinal);
        Assert.Contains("RequestFocusAccount?.Invoke", source, StringComparison.Ordinal);
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
    public readonly List<(string Title, string Message, Guid? AccountId)> Toasts = new();
    public void ShowToast(string title, string message, Guid? accountId = null) =>
        Toasts.Add((title, message, accountId));
    public void SetMemoryWarning(bool active) { }
    public void Dispose() { }
}
