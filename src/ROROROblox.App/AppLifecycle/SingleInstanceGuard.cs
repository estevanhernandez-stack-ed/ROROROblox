using System.IO;
using System.IO.Pipes;
using System.Windows;
using System.Windows.Interop;
using ROROROblox.App.Discord;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ROROROblox.App.AppLifecycle;

internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexNameTemplate = @"Local\{0}";
    private const string PipeNameTemplate = "{0}-show-window";
    private const string ShowWindowMessage = "SHOW";

    // Discord Join relay: a second instance launched with a roblox-rororo://join/ URI (Discord
    // hands the URI to whichever process the registry's "%1" command starts) forwards it to the
    // already-running instance over the same pipe, instead of inventing a second channel. Reuses
    // the existing single-line protocol — "SHOW" for a bare focus request, "JOIN:<uri>" to also
    // carry the join URI — rather than opening a parallel pipe/mechanism for one more message type.
    private const string JoinMessagePrefix = "JOIN:";

    private readonly string _pipeName;
    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;

    public SingleInstanceGuard(string id)
    {
        var mutexName = string.Format(MutexNameTemplate, id);
        _pipeName = string.Format(PipeNameTemplate, id);
        _mutex = new Mutex(initiallyOwned: true, name: mutexName, createdNew: out _ownsMutex);
    }

    /// <param name="joinUri">
    /// The raw <c>roblox-rororo://join/...</c> argument this (second) process was launched with,
    /// if any. Null for a normal "just bring the window forward" relaunch.
    /// </param>
    public bool AcquireOrSignalExisting(string? joinUri = null)
    {
        if (_ownsMutex)
        {
            return true;
        }
        SignalExisting(joinUri);
        return false;
    }

    private void SignalExisting(string? joinUri)
    {
        // Grant the first instance permission to take foreground. Without this, Windows
        // blocks SetForegroundWindow as a cross-process focus steal and only flashes the
        // taskbar. ASFW_ANY = 0xFFFFFFFF lets the next call from any process succeed within
        // the brief permission window. Second instance has foreground right now (it's the
        // process the user just launched), so it has the authority to grant.
        const uint ASFW_ANY = 0xFFFFFFFF;
        PInvoke.AllowSetForegroundWindow(ASFW_ANY);

        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect(timeout: 1000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(joinUri is null ? ShowWindowMessage : JoinMessagePrefix + joinUri);
        }
        catch (TimeoutException)
        {
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// Fires when a second instance relayed a <c>roblox-rororo://join/...</c> URI (raw, still
    /// url-encoded) instead of a bare focus request. Always raised on the UI thread — the
    /// listener's caller (<see cref="StartListening"/>) passes <paramref name="mainWindow"/>'s
    /// dispatcher for exactly this. Callers turn the raw URI into a <c>LaunchTarget</c> via
    /// <see cref="JoinUriParser"/> themselves; this class stays agnostic of that decode.
    /// </summary>
    public event Action<string>? JoinUriReceived;

    public void StartListening(Window mainWindow)
    {
        _listenerCts = new CancellationTokenSource();
        _listenerTask = Task.Run(() => ListenLoopAsync(mainWindow, _listenerCts.Token));
    }

    private async Task ListenLoopAsync(Window mainWindow, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server);
                var message = await reader.ReadLineAsync(cancellationToken);
                if (message == ShowWindowMessage)
                {
                    mainWindow.Dispatcher.Invoke(() => SurfaceWindow(mainWindow));
                }
                else if (message is not null && message.StartsWith(JoinMessagePrefix, StringComparison.Ordinal))
                {
                    // A relayed join still surfaces the window — the user clicked Join and should
                    // see RoRoRo, same as a bare focus request — then hands the raw URI onward.
                    var joinUri = message[JoinMessagePrefix.Length..];
                    mainWindow.Dispatcher.Invoke(() =>
                    {
                        SurfaceWindow(mainWindow);
                        JoinUriReceived?.Invoke(joinUri);
                    });
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
            }
        }
    }

    private static void SurfaceWindow(Window window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }
        window.Activate();

        var helper = new WindowInteropHelper(window);
        if (helper.Handle == IntPtr.Zero)
        {
            return;
        }

        var hwnd = new HWND(helper.Handle);
        if (PInvoke.IsIconic(hwnd))
        {
            PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_RESTORE);
        }

        // AttachThreadInput trick: temporarily attach our message-pump thread to the foreground
        // thread's input queue, then SetForegroundWindow. Windows treats this as legitimate input
        // (the focus change rides on the same input context as the user's last action) instead of
        // a rude cross-process steal. This is the documented recovery for the AllowSetForegroundWindow
        // permission window the second instance opened for us.
        var foregroundHwnd = PInvoke.GetForegroundWindow();
        var foregroundThreadId = PInvoke.GetWindowThreadProcessId(foregroundHwnd, out _);
        var currentThreadId = PInvoke.GetCurrentThreadId();

        var attached = false;
        if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
        {
            attached = PInvoke.AttachThreadInput(currentThreadId, foregroundThreadId, fAttach: true);
        }

        try
        {
            PInvoke.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                PInvoke.AttachThreadInput(currentThreadId, foregroundThreadId, fAttach: false);
            }
        }
    }

    public void Dispose()
    {
        _listenerCts?.Cancel();
        try
        {
            _listenerTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
        _listenerCts?.Dispose();

        if (_ownsMutex)
        {
            try { _mutex.ReleaseMutex(); } catch { }
        }
        _mutex.Dispose();
    }
}
