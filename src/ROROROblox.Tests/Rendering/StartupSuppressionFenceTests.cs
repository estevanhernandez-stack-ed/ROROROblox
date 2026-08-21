namespace ROROROblox.Tests.Rendering;

/// <summary>
/// F-105's fix, fenced — because what it replaced was a guard, and a fix nobody guards is a bug
/// waiting to come back wearing the same disguise.
/// <para>
/// <b>THE DEFECT.</b> <c>WindowRenderHost</c> builds the real <c>App</c> to get App.xaml's
/// converters and styles, and its comment asserted that <c>new App()</c> "does not start anything —
/// only Run() raises OnStartup". That was false: with a dispatcher pumping on that thread, startup
/// ran. The app's own log, written by the TEST HOST during a render run, read <i>"ROROROblox
/// starting"</i>, <i>"Another instance is running; signaling and exiting"</i>, <i>"ROROROblox
/// exiting (code 0)"</i>.
/// </para>
/// <para>
/// So with a RoRoRo instance up, the single-instance guard found the mutex held and called
/// <c>Shutdown(0)</c>, disposing the Application underneath renders in flight — and pack-URI
/// resolution against a disposed Application throws
/// <c>NotSupportedException: The URI prefix is not recognized</c>. That exception was hunted as a
/// cause for weeks, across four isolation probes and one reverted fix. It was a symptom.
/// </para>
/// <para>
/// <b>AND IT HAD A SECOND COST NOBODY WAS LOOKING FOR.</b> Every render run executed real startup:
/// writing to the user's real log file, scanning the plugin registry, <b>starting a real plugin
/// process</b>, and running an update check. A test suite with production side effects, invisible
/// because the failure it caused looked like something else entirely.
/// </para>
/// </summary>
public class StartupSuppressionFenceTests
{
    [Fact]
    public void TheRenderHostSuppressesAppStartup()
    {
        // Touching the host is what sets the flag — it is set on the way to building the App, so
        // asking for a dispatcher is enough to prove the harness does it.
        WindowRenderHost.DrainQueue();

        Assert.True(App.App.SuppressStartupForRenderHarness,
            "WindowRenderHost must suppress App.OnStartup before constructing the App. Without it "
            + "the harness runs the real startup path inside the test host: with a RoRoRo instance "
            + "running it calls Shutdown(0) and every render fails with 'The URI prefix is not "
            + "recognized', and with or without one it writes to the user's log and starts plugin "
            + "processes. That is F-105, and it took four probes and a reverted fix to find.");
    }

    [WindowRenderFact]
    public void RenderingStillWorksWithStartupSuppressed()
    {
        // The other half of the claim. Suppressing startup must not cost the harness the thing it
        // builds the real App FOR — App.xaml's merged dictionaries. A resource that resolves here
        // proves the resources survived the suppression.
        var found = WindowRenderHost.Run(
            () => System.Windows.Application.Current?.TryFindResource("MetaFontSize"),
            "resource lookup with startup suppressed");

        Assert.NotNull(found);
    }
}
