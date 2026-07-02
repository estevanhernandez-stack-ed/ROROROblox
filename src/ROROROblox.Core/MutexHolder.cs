using System.Runtime.InteropServices;
using System.Timers;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Timer = System.Timers.Timer;

namespace ROROROblox.Core;

/// <summary>
/// Holds an OS handle on a named mutex via Win32 <c>CreateMutex</c>. Use of
/// <c>System.Threading.Mutex</c>'s named-mutex surface is intentionally avoided — we need
/// explicit handle lifetime control across Acquire / Release / Dispose, including the watchdog
/// that fires <see cref="IMutexHolder.MutexLost"/> when an external close invalidates our handle.
/// </summary>
public sealed class MutexHolder : IMutexHolder, IDisposable
{
    /// <summary>
    /// Production name. Roblox checks this object on launch; holding it allows multi-instance.
    /// Lives in the Local kernel namespace (per-logon-session).
    /// </summary>
    public const string DefaultMutexName = @"Local\ROBLOX_singletonEvent";

    /// <summary>Win32 <c>CreateMutex</c> caps <c>lpName</c> at MAX_PATH characters.</summary>
    private const int MaxNameLength = 260;

    private const uint ErrorAlreadyExists = 0xB7;
    private const double WatchdogIntervalMs = 5_000;

    /// <summary>SYNCHRONIZE — the minimal access right needed to probe a mutex's existence via OpenMutex.</summary>
    private const uint SynchronizeAccess = 0x00100000;

    private readonly string _mutexName;
    private readonly object _lock = new();
    private readonly Timer _watchdog;

    private SafeFileHandle? _handle;
    private bool _disposed;

    public MutexHolder() : this(DefaultMutexName) { }

    public MutexHolder(string mutexName)
    {
        if (string.IsNullOrWhiteSpace(mutexName))
        {
            throw new ArgumentException("Mutex name must not be empty.", nameof(mutexName));
        }
        _mutexName = mutexName;
        _watchdog = new Timer(WatchdogIntervalMs) { AutoReset = true };
        _watchdog.Elapsed += OnWatchdogTick;
    }

    /// <summary>
    /// True when <paramref name="name"/> is safe to pass to <see cref="MutexHolder(string)"/> and on
    /// to Win32 <c>CreateMutex</c>. A strict SUPERSET of the ctor's reject conditions
    /// (null / empty / whitespace) — also rejects an embedded NUL and an over-length name. The
    /// config-driven mutex-name resolver gates every candidate through this and falls back to
    /// <see cref="DefaultMutexName"/> on false, so a malformed roblox-compat.json value never reaches
    /// the throwing ctor and never bricks multi-instance.
    /// </summary>
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }
        if (name.Contains('\0'))
        {
            return false;
        }
        return name.Length <= MaxNameLength;
    }

    /// <summary>The resolved mutex name this holder was constructed with. See <see cref="IMutexHolder.MutexName"/>.</summary>
    public string MutexName => _mutexName;

    public bool IsHeld
    {
        get
        {
            lock (_lock)
            {
                return _handle is { IsInvalid: false };
            }
        }
    }

    public event EventHandler? MutexLost;

    public bool Acquire()
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            if (_handle is { IsInvalid: false })
            {
                return true;
            }

            SafeFileHandle handle;
            unsafe
            {
                handle = PInvoke.CreateMutex(
                    lpMutexAttributes: null,
                    bInitialOwner: true,
                    lpName: _mutexName);
            }
            var lastError = (uint)Marshal.GetLastPInvokeError();

            if (handle.IsInvalid)
            {
                handle.Dispose();
                return false;
            }

            if (lastError == ErrorAlreadyExists)
            {
                // Another process already holds the named mutex — `bInitialOwner: true` was ignored.
                // Drop our handle and report acquisition failure.
                handle.Dispose();
                return false;
            }

            _handle = handle;
            _watchdog.Start();
            return true;
        }
    }

    public void Release()
    {
        lock (_lock)
        {
            if (_handle is null || _handle.IsInvalid)
            {
                return;
            }

            _watchdog.Stop();
            try
            {
                PInvoke.ReleaseMutex(_handle);
            }
            catch
            {
                // Best-effort: if Release fails (already released, or handle invalid), close anyway.
            }
            _handle.Dispose();
            _handle = null;
        }
    }

    /// <summary>
    /// Non-acquiring probe via Win32 <c>OpenMutex</c>. Never waits, never mutates <see cref="_handle"/>.
    /// If we already hold the mutex, that's ownership, not contention — return false without touching
    /// the OS. Otherwise attempt to open the named mutex with only <c>SYNCHRONIZE</c> access: success
    /// means some other process holds it (elsewhere), and OpenMutex failing (e.g. ERROR_FILE_NOT_FOUND
    /// when nobody holds it) returns an invalid handle rather than throwing. Any unexpected failure is
    /// swallowed and treated as "not elsewhere" — fail-safe, so a probe glitch never raises a false
    /// contested alarm.
    /// </summary>
    public bool IsHeldElsewhere()
    {
        lock (_lock)
        {
            if (_handle is { IsInvalid: false })
            {
                return false; // we hold it — not "elsewhere"
            }
        }

        SafeFileHandle probe;
        try
        {
            unsafe
            {
                probe = PInvoke.OpenMutex(
                    (SYNCHRONIZATION_ACCESS_RIGHTS)SynchronizeAccess,
                    bInheritHandle: false,
                    lpName: _mutexName);
            }
        }
        catch
        {
            return false; // probe failure → treat as not contested (fail-safe: no false banner)
        }

        try
        {
            return !probe.IsInvalid; // opened successfully → the mutex exists, held by someone else
        }
        finally
        {
            probe.Dispose();
        }
    }

    private void OnWatchdogTick(object? sender, ElapsedEventArgs e)
    {
        bool lost;
        lock (_lock)
        {
            if (_handle is null || _handle.IsInvalid)
            {
                return;
            }

            // Per spec §5.1: probe the handle via WaitForSingleObject(timeout: 0).
            // Owning thread already holds the mutex → wait succeeds with WAIT_OBJECT_0
            // and the recursive lock count increments by one; we balance with ReleaseMutex.
            // An invalidated handle returns WAIT_FAILED.
            var waitResult = PInvoke.WaitForSingleObject(_handle, dwMilliseconds: 0);
            switch (waitResult)
            {
                case WAIT_EVENT.WAIT_OBJECT_0:
                case WAIT_EVENT.WAIT_ABANDONED:
                    PInvoke.ReleaseMutex(_handle);
                    lost = false;
                    break;
                case WAIT_EVENT.WAIT_FAILED:
                    _handle.Dispose();
                    _handle = null;
                    _watchdog.Stop();
                    lost = true;
                    break;
                default:
                    // WAIT_TIMEOUT shouldn't happen for a mutex we own; treat conservatively as alive.
                    lost = false;
                    break;
            }
        }

        if (lost)
        {
            MutexLost?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        Release();
        _watchdog.Stop();
        _watchdog.Elapsed -= OnWatchdogTick;
        _watchdog.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MutexHolder));
        }
    }
}
