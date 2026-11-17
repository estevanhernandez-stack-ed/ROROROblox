using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Services.Store;

namespace ROROROblox.App.Updates;

/// <summary>
/// The real <see cref="IStoreUpdateProbe"/>: asks <c>StoreContext.GetAppAndOptionalStorePackageUpdatesAsync</c>.
/// Reached only on a packaged install (<see cref="UpdateAvailability"/> never calls a probe otherwise).
/// <para>
/// Every failure reads as "none" and is logged once at Debug: no package identity on a sideloaded build
/// (<c>InvalidOperationException</c>), no network, the Store service off, a COM error
/// (<c>COMException</c>, <c>UnauthorizedAccessException</c>) are all expected somewhere in the field and none
/// is worth a Warning. No <c>Request...</c> method is called: those show Store UI and need a window handle;
/// the update check needs neither.
/// </para>
/// <para>
/// The WinRT call is a constructor seam so the catch-and-log shape is testable. The call itself cannot be
/// exercised without a Store-installed build and was not verified live when this was written.
/// </para>
/// </summary>
internal sealed class StoreContextUpdateProbe : IStoreUpdateProbe
{
    private readonly Func<CancellationToken, Task<int>> _countUpdates;
    private readonly ILogger _log;
    private bool _loggedFailure;

    public StoreContextUpdateProbe(ILogger<StoreContextUpdateProbe>? log = null)
        : this(CountFromStore, log)
    {
    }

    internal StoreContextUpdateProbe(Func<CancellationToken, Task<int>> countUpdates, ILogger? log = null)
    {
        _countUpdates = countUpdates ?? throw new ArgumentNullException(nameof(countUpdates));
        _log = log ?? NullLogger.Instance;
    }

    public async Task<bool> IsUpdateWaitingAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _countUpdates(cancellationToken).ConfigureAwait(false) > 0;
        }
        catch (Exception ex)
        {
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                _log.LogDebug(ex, "The Store update query failed ({ExceptionType}); reading it as no update.", ex.GetType().Name);
            }

            return false;
        }
    }

    private static async Task<int> CountFromStore(CancellationToken cancellationToken)
    {
        var updates = await StoreContext.GetDefault()
            .GetAppAndOptionalStorePackageUpdatesAsync()
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        return updates.Count;
    }
}
