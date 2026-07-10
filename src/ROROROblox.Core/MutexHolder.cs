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
///
/// <para><b>It is a name race, not a lock.</b> Roblox creates an <i>Event</i> under this name;
/// RoRoRo creates a <i>Mutex</i>. Whoever creates it first wins, and the loser's create fails
/// because the object already exists under a different type. Winning is what disables Roblox's
/// single-instance enforcement. See <see cref="MutexAcquireOutcome"/>.</para>
/// </summary>
public sealed class MutexHolder : IMutexHolder, IDisposable
{
    /// <summary>
    /// Production name. Roblox checks this object on launch; winning it allows multi-instance.
    /// Lives in the Local kernel namespace (per-logon-session).
    /// </summary>
    public const string DefaultMutexName = @"Local\ROBLOX_singletonEvent";

    /// <summary>Win32 <c>CreateMutex</c> caps <c>lpName</c> at MAX_PATH characters.</summary>
    private const int MaxNameLength = 260;

    /// <summary>
    /// <c>ERROR_ALREADY_EXISTS</c>. CreateMutex returns a VALID handle plus this code when a Mutex
    /// of the same name already exists — i.e. another process created it the way we do. Roblox
    /// never lands here: it creates an Event, so we get <see cref="ErrorInvalidHandle"/> instead.
    /// </summary>
    private const uint ErrorAlreadyExists = 0xB7;

    /// <summary>
    /// <c>ERROR_INVALID_HANDLE</c>. CreateMutex fails with this when the name already exists as a
    /// kernel object of a DIFFERENT type — precisely what Roblox's <c>ROBLOX_singletonEvent</c> is.
    /// This, not ERROR_ALREADY_EXISTS, is the code the Roblox case actually hits.
    /// </summary>
    private const uint ErrorInvalidHandle = 0x6;

    private const double WatchdogIntervalMs = 5_000;

    /// <summary>SYNCHRONIZE — the minimal access right needed to probe an object's existence via OpenMutex / OpenEvent.</summary>
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

    public bool Acquire() => TryAcquire() == MutexAcquireOutcome.Acquired;

    /// <summary>
    /// One attempt on the singleton name, reporting who owns it when we lose. See
    /// <see cref="MutexAcquireOutcome"/> — the two failure modes call for opposite user-facing
    /// behavior, and returning a bare false is what conflated them.
    /// </summary>
    public MutexAcquireOutcome TryAcquire()
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            if (_handle is { IsInvalid: false })
            {
                return MutexAcquireOutcome.Acquired;
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
                // ERROR_INVALID_HANDLE => the name is taken by a non-mutex object. In production
                // that object is Roblox's Event, and no amount of retrying wins it back until
                // every Roblox process exits and its last handle closes.
                return lastError == ErrorInvalidHandle
                    ? MutexAcquireOutcome.HeldByRoblox
                    : MutexAcquireOutcome.Failed;
            }

            if (lastError == ErrorAlreadyExists)
            {
                // A Mutex of this name already exists, so its creator squats the name the same way
                // we do: another RoRoRo, or a compatible multi-instance tool. `bInitialOwner: true`
                // was ignored and the handle we got back is to THEIR object. Drop it — we don't own
                // the name — but multi-instance still works, because Roblox lost the name to them.
                handle.Dispose();
                return MutexAcquireOutcome.HeldByCompatibleTool;
            }

            _handle = handle;
            _watchdog.Start();
            return MutexAcquireOutcome.Acquired;
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
    /// Non-acquiring probe. Never waits, never mutates <see cref="_handle"/>. If we already hold the
    /// name, that's ownership, not contention — return false without touching the OS.
    ///
    /// <para>Probes BOTH object types. Opening only as a Mutex was a real bug: Roblox's
    /// <c>ROBLOX_singletonEvent</c> is an Event, so <c>OpenMutex</c> fails it with
    /// ERROR_INVALID_HANDLE and this returned false — meaning the contested banner never fired in
    /// the one case it exists for. The old unit tests all held the name with another
    /// <see cref="MutexHolder"/>, which is the compatible-tool case, so they passed throughout.</para>
    ///
    /// <para>Any unexpected failure is swallowed and treated as "not elsewhere" — fail-safe, so a
    /// probe glitch never raises a false contested alarm.</para>
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

        return ExistsAsMutex() || ExistsAsEvent();
    }

    /// <summary>A compatible tool (or another RoRoRo) squatting the name the way we do.</summary>
    private bool ExistsAsMutex()
    {
        try
        {
            SafeFileHandle probe;
            unsafe
            {
                probe = PInvoke.OpenMutex(
                    (SYNCHRONIZATION_ACCESS_RIGHTS)SynchronizeAccess,
                    bInheritHandle: false,
                    lpName: _mutexName);
            }
            using (probe)
            {
                return !probe.IsInvalid;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Roblox itself: the singleton is an Event, and OpenMutex is blind to it.</summary>
    private bool ExistsAsEvent()
    {
        try
        {
            SafeFileHandle probe;
            unsafe
            {
                probe = PInvoke.OpenEvent(
                    (SYNCHRONIZATION_ACCESS_RIGHTS)SynchronizeAccess,
                    bInheritHandle: false,
                    lpName: _mutexName);
            }
            using (probe)
            {
                return !probe.IsInvalid;
            }
        }
        catch
        {
            return false;
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
