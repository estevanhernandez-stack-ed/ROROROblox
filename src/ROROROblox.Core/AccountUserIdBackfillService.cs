using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ROROROblox.Core;

/// <summary>
/// One-time eager backfill for the cycle-5 <see cref="Account.RobloxUserId"/> field. Runs as a
/// fire-and-forget worker triggered ~5s after MainWindow.Show from <c>App.RunStartupChecksAsync</c>.
/// For each account where <c>RobloxUserId is null</c>: retrieve cookie → resolve via
/// <see cref="IRobloxApi.GetUserProfileAsync"/> → persist via
/// <see cref="IAccountStore.UpdateRobloxUserIdAsync"/>. Idempotent: once an account is backfilled,
/// subsequent runs see <c>RobloxUserId is not null</c> and skip.
/// </summary>
/// <remarks>
/// <para>Anti-fraud discipline (spec §5): sequential, staggered with ±jitter, post-paint, scoped
/// to missing-only. Diverges from the v1.1.2.0 startup-validation trip-wire on five dimensions
/// — see canonical spec for full rationale.</para>
/// <para>Lives in Core (not App) so the unit-test project can reach it without taking a WPF
/// dependency. Same pattern as cycle 4's <see cref="Diagnostics.StartupGate"/>.</para>
/// </remarks>
public sealed class AccountUserIdBackfillService
{
    private readonly IAccountStore _store;
    private readonly IRobloxApi _api;
    private readonly ILogger<AccountUserIdBackfillService> _log;
    private readonly int _interAccountDelayMs;

    /// <summary>
    /// Raised after each <see cref="IAccountStore.UpdateRobloxUserIdAsync"/> that persisted a
    /// resolved id, so the live row and the presence poll's target list learn it in the same
    /// session instead of after a restart (spec 2026-11-01 §2.6). Raised on the worker thread;
    /// subscribers marshal. A throwing subscriber never fails the backfill.
    /// </summary>
    public event EventHandler<AccountUserIdResolvedEventArgs>? AccountUserIdResolved;

    /// <summary>
    /// Production constructor — stagger <see cref="BackfillPacing.InterAccountDelayMs"/> between accounts
    /// (±<see cref="BackfillPacing.JitterMs"/> jitter applied at the call site). Tests pass <paramref name="interAccountDelayMs"/>=0 to skip the wait.
    /// </summary>
    public AccountUserIdBackfillService(
        IAccountStore store,
        IRobloxApi api,
        ILogger<AccountUserIdBackfillService>? log = null,
        int interAccountDelayMs = BackfillPacing.InterAccountDelayMs)
    {
        _store = store;
        _api = api;
        _log = log ?? NullLogger<AccountUserIdBackfillService>.Instance;
        _interAccountDelayMs = interAccountDelayMs;
    }

    /// <summary>
    /// Run the backfill once. Soft-fails per account — exceptions on cookie retrieval, API call,
    /// or persist DO NOT bubble; the loop continues to the next account. <see cref="OperationCanceledException"/>
    /// from the cancellation token DOES bubble (so the caller knows we exited cleanly mid-loop).
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        IReadOnlyList<Account> accounts;
        try
        {
            accounts = await _store.ListAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Backfill: ListAsync threw; skipping pass.");
            return;
        }

        var missing = accounts.Where(a => a.RobloxUserId is null).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        _log.LogInformation("Backfill: {Count} accounts missing RobloxUserId; resolving sequentially.", missing.Count);

        for (var i = 0; i < missing.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var account = missing[i];
            try
            {
                var cookie = await _store.RetrieveCookieAsync(account.Id).ConfigureAwait(false);
                if (string.IsNullOrEmpty(cookie))
                {
                    _log.LogDebug("Backfill skipped {AccountId}: empty cookie.", account.Id);
                    continue;
                }
                var profile = await _api.GetUserProfileAsync(cookie).ConfigureAwait(false);
                if (profile.UserId <= 0)
                {
                    _log.LogDebug("Backfill skipped {AccountId}: GetUserProfileAsync returned non-positive UserId.", account.Id);
                    continue;
                }
                await _store.UpdateRobloxUserIdAsync(account.Id, profile.UserId).ConfigureAwait(false);
                _log.LogDebug("Backfilled RobloxUserId={UserId} for {AccountId}", profile.UserId, account.Id);
                RaiseResolved(account.Id, profile.UserId);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Backfill skipped {AccountId}; will retry next session.", account.Id);
            }

            // Stagger between accounts. Skip after the last account so we don't waste 2.5s
            // on an empty trailing wait. ±500ms jitter avoids exactly-rhythmic API calls.
            if (i < missing.Count - 1 && _interAccountDelayMs > 0)
            {
                var jittered = _interAccountDelayMs + Random.Shared.Next(-BackfillPacing.JitterMs, BackfillPacing.JitterMs);
                await Task.Delay(Math.Max(0, jittered), ct).ConfigureAwait(false);
            }
        }
    }

    private void RaiseResolved(Guid accountId, long userId)
    {
        try
        {
            AccountUserIdResolved?.Invoke(this, new AccountUserIdResolvedEventArgs(accountId, userId));
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Backfill: an AccountUserIdResolved subscriber threw for {AccountId}; continuing.", accountId);
        }
    }
}

/// <summary>Payload for <see cref="AccountUserIdBackfillService.AccountUserIdResolved"/>.</summary>
public sealed class AccountUserIdResolvedEventArgs : EventArgs
{
    public AccountUserIdResolvedEventArgs(Guid accountId, long userId)
    {
        AccountId = accountId;
        UserId = userId;
    }

    public Guid AccountId { get; }
    public long UserId { get; }
}
