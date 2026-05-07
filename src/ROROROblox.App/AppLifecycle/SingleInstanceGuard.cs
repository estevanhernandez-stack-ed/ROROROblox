using System.IO;
using System.IO.Pipes;
using System.Windows;
using System.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ROROROblox.App.AppLifecycle;

internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexNameTemplate = @"Local\{0}";
    private const string PipeNameTemplate = "{0}-show-window";
    private const string ShowWindowMessage = "SHOW";
    // Pipe protocol: JOIN <uri> on a single line, where <uri> is the discord-{appId}://...
    // string Discord dispatched to the second instance via URI scheme registration.
    private const string JoinMessagePrefix = "JOIN ";

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

    /// <summary>
    /// Acquire the mutex (first instance) OR signal the running first instance and bail.
    /// When <paramref name="discordJoinUri"/> is non-null on the second-instance path, the
    /// signal carries the URI so the first instance can dispatch the Discord Join. Without
    /// this hand-off the URI args from Discord's URI-scheme dispatch get silently dropped
    /// when the second exe quits.
    /// </summary>
    public bool AcquireOrSignalExisting(string? discordJoinUri = null)
    {
        if (_ownsMutex)
        {
            return true;
        }
        SignalExisting(discordJoinUri);
        return false;
    }

    private void SignalExisting(string? discordJoinUri)
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
            if (!string.IsNullOrEmpty(discordJoinUri))
            {
                writer.WriteLine(JoinMessagePrefix + discordJoinUri);
            }
            else
            {
                writer.WriteLine(ShowWindowMessage);
            }
        }
        catch (TimeoutException)
        {
        }
        catch (IOException)
        {
        }
    }

    public void StartListening(Window mainWindow, Action<string>? joinUriHandler = null)
    {
        _listenerCts = new CancellationTokenSource();
        _listenerTask = Task.Run(() => ListenLoopAsync(mainWindow, joinUriHandler, _listenerCts.Token));
    }

    private async Task ListenLoopAsync(Window mainWindow, Action<string>? joinUriHandler, CancellationToken cancellationToken)
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
                    var uri = message.Substring(JoinMessagePrefix.Length);
                    if (joinUriHandler is not null && !string.IsNullOrEmpty(uri))
                    {
                        // Surface the window too so the user has visual confirmation, then
                        // dispatch the join handler off the listener loop thread.
                        mainWindow.Dispatcher.Invoke(() => SurfaceWindow(mainWindow));
                        try { joinUriHandler(uri); } catch { /* handler must not break the loop */ }
                    }
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
