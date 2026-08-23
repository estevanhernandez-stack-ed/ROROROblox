using System.Windows.Threading;
using ROROROblox.App;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// F-122: TrayService's constructor builds WPF visuals (TaskbarIcon, ContextMenu, MenuItem), and
/// it is registered as a plain singleton — so whichever caller resolves it FIRST constructs it on
/// whatever thread that caller is on. On 2026-08-20 that caller was StartPluginHostListener's
/// threadpool continuation resolving AlertDispatcher, and startup died on
/// "Cannot access Freezable 'SolidColorBrush' across threads".
///
/// <para>The fix is structural rather than ordering: the factory itself marshals construction to
/// the UI dispatcher, so no future background resolve can recreate the crash. These tests pin the
/// marshalling decision, which is why it lives in a helper instead of inline in a DI lambda.</para>
/// </summary>
public class UiBoundFactoryTests
{
    [Fact]
    public void WithNoDispatcherConstructionRunsInline()
    {
        // Headless tests and unit contexts have no Application — construction must still work.
        var created = UiBoundFactory.Create(dispatcher: null, () => Environment.CurrentManagedThreadId);

        Assert.Equal(Environment.CurrentManagedThreadId, created);
    }

    [Fact]
    public void OnTheDispatcherThreadConstructionRunsInline()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;

        var created = UiBoundFactory.Create(dispatcher, () => Environment.CurrentManagedThreadId);

        Assert.Equal(Environment.CurrentManagedThreadId, created);
    }

    [Fact]
    public void OffTheDispatcherThreadConstructionMarshalsToIt()
    {
        // A real second dispatcher on its own thread, the shape of the crash: resolve requested
        // from one thread while the UI dispatcher lives on another.
        Dispatcher? uiDispatcher = null;
        int uiThreadId = 0;
        using var ready = new ManualResetEventSlim(false);

        var uiThread = new Thread(() =>
        {
            uiDispatcher = Dispatcher.CurrentDispatcher;
            uiThreadId = Environment.CurrentManagedThreadId;
            ready.Set();
            Dispatcher.Run();
        });
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.IsBackground = true;
        uiThread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
        Assert.NotNull(uiDispatcher);

        try
        {
            var constructedOn = UiBoundFactory.Create(uiDispatcher, () => Environment.CurrentManagedThreadId);

            Assert.Equal(uiThreadId, constructedOn);
            Assert.NotEqual(Environment.CurrentManagedThreadId, constructedOn);
        }
        finally
        {
            uiDispatcher!.InvokeShutdown();
        }
    }
}
