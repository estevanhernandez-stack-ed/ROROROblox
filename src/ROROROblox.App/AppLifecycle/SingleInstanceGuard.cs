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

    public bool AcquireOrSignalExisting()
    {
        if (_ownsMutex)
        {
            return true;
        }
        SignalExisting();
        return false;
    }

    private void SignalExisting()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect(timeout: 1000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(ShowWindowMessage);
        }
        catch (TimeoutException)
        {
        }
        catch (IOException)
        {
        }
    }

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

        // Belt-and-suspenders: WPF's Activate() doesn't always grab focus when the foreground-set
        // restriction is enforced (cross-process focus steal). SetForegroundWindow + restore-if-iconic
        // is the documented recovery.
        var helper = new WindowInteropHelper(window);
        if (helper.Handle != IntPtr.Zero)
        {
            var hwnd = new HWND(helper.Handle);
            if (PInvoke.IsIconic(hwnd))
            {
                PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_RESTORE);
            }
            PInvoke.SetForegroundWindow(hwnd);
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
