using ROROROblox.App.Tray;

namespace ROROROblox.Tests;

/// <summary>
/// The tray icon has to be disposed on the UI thread, and before the container is.
/// <para>
/// MEASURED, 2026-11-16. Every exit logged "ServiceProvider.DisposeAsync did not complete within 2s;
/// abandoning." (17 of 17 exits on v1.35 and v1.39 in one PC's logs). Two <c>dotnet-stack</c> captures
/// taken inside that two-second window on a real v1.38 exit showed a plain deadlock: the UI thread in
/// <c>App.OnExit</c> → <c>Task.Wait</c>, waiting for the container; a pool thread in
/// <c>ServiceProviderEngineScope.DisposeAsync</c> → Hardcodet's <c>TaskbarIcon.Dispose(bool)</c> →
/// <c>Dispatcher.Invoke</c>, waiting for the UI thread. Hardcodet 2.0.1 marshals its own disposal to
/// the dispatcher. The container has disposed off the UI thread since 2026-07-03 (v1.8.1), so from
/// then on every exit that had built the tray lost two seconds, never removed the icon, and never
/// disposed anything created before it.
/// </para>
/// <para>
/// <c>TrayService</c> needs a real WPF <c>TaskbarIcon</c> and cannot be constructed in a test (it
/// would put an icon in the developer's tray), so the mechanism is covered here by the small class
/// that carries the tray to <c>OnExit</c> and by source fences on the ordering, the way the repo's
/// other composition fences work.
/// </para>
/// </summary>
public class BuiltTrayTests
{
    private sealed class CountingDisposable : IDisposable
    {
        public int Disposals;
        public void Dispose() => Disposals++;
    }

    [Fact]
    public void DisposeIfBuilt_WhenNoTrayWasEverBuilt_DoesNothingAndSaysSo()
    {
        var built = new BuiltTray();

        Assert.False(built.DisposeIfBuilt());
    }

    [Fact]
    public void DisposeIfBuilt_DisposesTheTrayThatWasBuilt()
    {
        var built = new BuiltTray();
        var tray = new CountingDisposable();
        built.Set(tray);

        Assert.True(built.DisposeIfBuilt());
        Assert.Equal(1, tray.Disposals);
    }

    [Fact]
    public void DisposeIfBuilt_ASecondCall_DoesNotDisposeAgain()
    {
        // OnExit can be reached twice in principle (a shutdown requested during a shutdown), and the
        // container still calls the tray's own Dispose afterwards. This class hands the tray over once.
        var built = new BuiltTray();
        var tray = new CountingDisposable();
        built.Set(tray);

        built.DisposeIfBuilt();

        Assert.False(built.DisposeIfBuilt());
        Assert.Equal(1, tray.Disposals);
    }
}

/// <summary>
/// Source fences on the three facts the fix rests on. <c>App.OnExit</c> is a WPF <c>Application</c>
/// override and is not constructible in a test; see <see cref="MetricSinkExitOrderFenceTests"/> for
/// the same approach.
/// </summary>
public class TrayExitOrderFenceTests
{
    private static string Read(params string[] relative)
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate the repo root from the test bin directory.");
        return File.ReadAllText(Path.Combine(new[] { root! }.Concat(relative).ToArray()));
    }

    [Fact]
    public void OnExit_DisposesTheTray_Inline_BeforeTheContainerIsDisposedOffTheUiThread()
    {
        var source = Read("src", "ROROROblox.App", "App.xaml.cs");
        var onExit = source.IndexOf("protected override void OnExit(", StringComparison.Ordinal);
        Assert.True(onExit >= 0, "OnExit is gone from App.xaml.cs — update this fence with its new home.");
        var body = source[onExit..];

        var trayDispose = body.IndexOf("BuiltTray>()?.DisposeIfBuilt()", StringComparison.Ordinal);
        var containerDispose = body.IndexOf("Task.Run(() => _services.DisposeAsync()", StringComparison.Ordinal);

        Assert.True(containerDispose >= 0, "OnExit's container dispose moved or changed shape; re-anchor this fence.");
        Assert.True(trayDispose >= 0,
            "OnExit no longer disposes the tray itself. The container disposes on a pool thread while " +
            "OnExit blocks the UI thread, and Hardcodet's TaskbarIcon.Dispose needs the UI thread: that " +
            "is a deadlock until the 2 s timeout (measured 2026-11-16).");
        Assert.True(trayDispose < containerDispose,
            "The tray must be disposed BEFORE the container, or the container reaches it first and deadlocks.");

        // Inline means on the UI thread, which is where OnExit runs. Inside a Task.Run it would be
        // back on a pool thread with the UI thread blocked: the same deadlock.
        var lineStart = body.LastIndexOf('\n', trayDispose) + 1;
        var statementStart = body.LastIndexOf(';', trayDispose);
        var before = body[Math.Max(lineStart, statementStart + 1)..trayDispose];
        Assert.DoesNotContain("Task.Run", before);
    }

    [Fact]
    public void TheTrayRegistration_RecordsTheTrayItBuilds()
    {
        // OnExit asks BuiltTray, not the container, so it never CONSTRUCTS a tray just to dispose it
        // on an exit that happens before anything resolved one.
        var source = Read("src", "ROROROblox.App", "App.xaml.cs");

        var registration = source.IndexOf("services.AddSingleton<ITrayService>(", StringComparison.Ordinal);
        Assert.True(registration >= 0, "The ITrayService registration moved; re-anchor this fence.");
        var end = source.IndexOf("services.AddSingleton<", registration + 10, StringComparison.Ordinal);
        var block = source[registration..(end < 0 ? source.Length : end)];

        Assert.Contains("BuiltTray>().Set(", block);
        Assert.Contains("services.AddSingleton<Tray.BuiltTray>()", source);
    }

    [Fact]
    public void TrayService_Dispose_ReturnsEarlyOnASecondCall()
    {
        // The container still calls TrayService.Dispose after OnExit already has, from a pool thread
        // with the UI thread blocked. That second call must not reach the TaskbarIcon.
        var source = Read("src", "ROROROblox.App", "Tray", "TrayService.cs");
        var dispose = source.IndexOf("public void Dispose()", StringComparison.Ordinal);
        Assert.True(dispose >= 0, "TrayService.Dispose is gone; re-anchor this fence.");
        var body = source[dispose..];

        var guard = body.IndexOf("if (_disposed)", StringComparison.Ordinal);
        var iconDispose = body.IndexOf("_taskbarIcon.Dispose()", StringComparison.Ordinal);

        Assert.True(guard >= 0 && iconDispose >= 0, "TrayService.Dispose changed shape; re-anchor this fence.");
        Assert.True(guard < iconDispose, "The _disposed guard must come before the TaskbarIcon is touched.");
    }
}
