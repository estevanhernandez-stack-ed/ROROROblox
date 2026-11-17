using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ROROROblox.App.Updates;

/// <summary>Which kind of update, if any, is waiting for this install.</summary>
internal enum UpdateKind
{
    /// <summary>Nothing is waiting, the check failed, or this is a dev build.</summary>
    None,

    /// <summary>A Microsoft Store install and the Store has a newer package for it.</summary>
    Store,

    /// <summary>A direct-download install; Velopack has downloaded an update and staged it to apply on exit.</summary>
    Staged,
}

/// <summary>What <see cref="UpdateAvailability"/> currently knows. <see cref="Version"/> is set for <see cref="UpdateKind.Staged"/> only.</summary>
internal readonly record struct UpdateStatus(UpdateKind Kind, string? Version = null)
{
    public static UpdateStatus None { get; } = new(UpdateKind.None);
}

/// <summary>
/// One question, asked of the Store: is a newer package waiting for this install. A seam so no test
/// ever reaches the WinRT call, which needs package identity the test host does not have.
/// </summary>
internal interface IStoreUpdateProbe
{
    /// <summary>True when the Store has one or more updates for the app. Implementations must not throw.</summary>
    Task<bool> IsUpdateWaitingAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Holds "is an update waiting for this install, and which kind", and says when that changes. Singleton.
/// <para>
/// A packaged (Store) install asks <see cref="IStoreUpdateProbe"/> once when <see cref="Start"/> is called,
/// which the app does after startup has finished, then every six hours: the case that prompted the button
/// is an app left running for days, and a running app holds a Store update back. A direct-download
/// install never asks anything here; <see cref="UpdateChecker"/> calls <see cref="ReportStaged"/> when it
/// has downloaded and staged an update. A dev build is neither, so nothing is ever waiting.
/// </para>
/// <para>
/// Never throws and never sits on the startup path. <see cref="Changed"/> may fire on any thread; the view
/// model marshals it. A failed or timed-out check changes nothing: an update already seen does not vanish
/// because one later check hit a bad network, and a first failure leaves the state at none.
/// </para>
/// </summary>
internal sealed class UpdateAvailability : IDisposable
{
    internal static readonly TimeSpan DefaultRecheckInterval = TimeSpan.FromHours(6);
    internal static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>RoRoRo's Microsoft Store page (product id from the keystone). The Store button opens this; it never installs in place.</summary>
    internal const string StoreListingUri = "ms-windows-store://pdp/?productid=9NMJCS390KWB";

    private readonly bool _isPackaged;
    private readonly IStoreUpdateProbe _probe;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _probeTimeout;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _checking = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();

    private UpdateStatus _status = UpdateStatus.None;
    private ITimer? _timer;
    private bool _started;
    private bool _disposed;
    private bool _loggedFailure;

    public UpdateAvailability(
        bool isPackaged,
        IStoreUpdateProbe probe,
        TimeProvider? time = null,
        ILogger<UpdateAvailability>? log = null,
        TimeSpan? recheckInterval = null,
        TimeSpan? probeTimeout = null)
    {
        _isPackaged = isPackaged;
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _time = time ?? TimeProvider.System;
        _log = (ILogger?)log ?? NullLogger.Instance;
        _interval = recheckInterval ?? DefaultRecheckInterval;
        _probeTimeout = probeTimeout ?? DefaultProbeTimeout;
    }

    /// <summary>Raised after <see cref="Current"/> changes, on whichever thread caused it.</summary>
    public event EventHandler? Changed;

    public UpdateStatus Current
    {
        get { lock (_gate) { return _status; } }
    }

    /// <summary>
    /// Begins the Store checks: one now, off the caller's thread, then one per interval. A no-op on an
    /// unpackaged build (the probe is never touched) and on a second call.
    /// </summary>
    public void Start()
    {
        if (!_isPackaged)
        {
            return;
        }

        lock (_gate)
        {
            if (_started || _disposed)
            {
                return;
            }

            _started = true;
            _timer = _time.CreateTimer(_ => _ = CheckNowAsync(), state: null, dueTime: _interval, period: _interval);
        }

        _ = CheckNowAsync();
    }

    /// <summary>
    /// Direct-download installs: <see cref="UpdateChecker"/> has downloaded and staged an update that
    /// applies when RoRoRo closes. Safe from any thread.
    /// </summary>
    public void ReportStaged(string version)
    {
        if (_isPackaged)
        {
            // A packaged build never stages a Velopack update; ignore rather than show a button that lies.
            return;
        }

        Set(new UpdateStatus(UpdateKind.Staged, string.IsNullOrWhiteSpace(version) ? null : version));
    }

    /// <summary>
    /// Runs one Store check now. Public to the tests so they await it; the timer calls it fire-and-forget.
    /// Never throws. Overlapping calls collapse: a check already running wins.
    /// </summary>
    internal async Task CheckNowAsync()
    {
        if (!_isPackaged || _disposed)
        {
            return;
        }

        if (!await _checking.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            // On the pool, so a probe that blocks before its first await cannot hold the caller.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
            var deadline = Task.Delay(_probeTimeout, _time, timeout.Token);
            var probe = Task.Run(() => _probe.IsUpdateWaitingAsync(_stopping.Token));
            var first = await Task.WhenAny(probe, deadline).ConfigureAwait(false);
            if (first != probe)
            {
                // The WinRT call cannot be cancelled, so it is left to finish on its own.
                _ = probe.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                LogFailureOnce("the Store update check did not answer in time");
                return;
            }

            timeout.Cancel();
            var waiting = await probe.ConfigureAwait(false);
            _loggedFailure = false;
            Set(waiting ? new UpdateStatus(UpdateKind.Store) : UpdateStatus.None);
        }
        catch (Exception ex)
        {
            LogFailureOnce("the Store update check threw", ex);
        }
        finally
        {
            try { _checking.Release(); }
            catch (ObjectDisposedException) { /* shut down while a check was in flight */ }
        }
    }

    private void LogFailureOnce(string what, Exception? ex = null)
    {
        if (_loggedFailure)
        {
            return;
        }

        _loggedFailure = true;
        _log.LogDebug(ex, "{What}; treating it as no update waiting.", what);
    }

    private void Set(UpdateStatus next)
    {
        lock (_gate)
        {
            if (_disposed || _status == next)
            {
                return;
            }

            _status = next;
        }

        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "An update-availability subscriber threw; ignoring.");
        }
    }

    /// <summary>Stops the timer and cancels a check in flight. Idempotent.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }

        try { _stopping.Cancel(); }
        catch (ObjectDisposedException) { }
    }
}
