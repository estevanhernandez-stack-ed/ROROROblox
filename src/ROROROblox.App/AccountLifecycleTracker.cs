using Microsoft.Extensions.Logging;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.App;

/// <summary>
/// Adapter on top of <see cref="IRobloxProcessTracker"/>. Subscribes to ProcessAttached /
/// ProcessExited, looks up the matching <see cref="Account"/> via <see cref="IAccountStore"/>,
/// and re-emits the event with current-active-count enrichment for downstream consumers
/// (DiscordPresenceLifecycle in item 7).
///
/// Account lookup is cached so a tight burst of events doesn't re-read the (DPAPI-encrypted)
/// store every time. Cache invalidates whenever the underlying tracker tells us the account
/// stopped — the caller can re-add at next start.
/// </summary>
public sealed class AccountLifecycleTracker : IAccountLifecycle, IDisposable
{
    private readonly IRobloxProcessTracker _tracker;
    private readonly IAccountStore _accountStore;
    private readonly ILogger<AccountLifecycleTracker> _log;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Account> _accountCache = new();
    private bool _disposed;

    public AccountLifecycleTracker(
        IRobloxProcessTracker tracker,
        IAccountStore accountStore,
        ILogger<AccountLifecycleTracker> log)
    {
        _tracker = tracker;
        _accountStore = accountStore;
        _log = log;
        _tracker.ProcessAttached += OnProcessAttached;
        _tracker.ProcessExited += OnProcessExited;
    }

    public event EventHandler<AccountStartedEventArgs>? AccountStarted;
    public event EventHandler<AccountStoppedEventArgs>? AccountStopped;

    private async void OnProcessAttached(object? sender, RobloxProcessEventArgs e)
    {
        try
        {
            var account = await ResolveAccountAsync(e.AccountId).ConfigureAwait(false);
            if (account is null) return;

            // Tracker has already added the entry to Attached by the time it fires this event.
            var count = _tracker.Attached.Count;
            try
            {
                AccountStarted?.Invoke(this, new AccountStartedEventArgs(account, e.Pid, count));
            }
            catch (Exception handlerEx)
            {
                _log.LogWarning(handlerEx, "AccountStarted subscriber threw; ignoring.");
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "OnProcessAttached for {AccountId} threw; ignoring.", e.AccountId);
        }
    }

    private async void OnProcessExited(object? sender, RobloxProcessEventArgs e)
    {
        try
        {
            var account = await ResolveAccountAsync(e.AccountId).ConfigureAwait(false);
            // Tracker has already removed this entry from Attached by the time it fires Exited.
            var count = _tracker.Attached.Count;

            // Drop cached entry — if the account is removed from disk later, we won't keep
            // emitting events with the stale record.
            lock (_gate)
            {
                _accountCache.Remove(e.AccountId);
            }

            if (account is null) return;
            try
            {
                AccountStopped?.Invoke(this, new AccountStoppedEventArgs(account, e.Pid, count));
            }
            catch (Exception handlerEx)
            {
                _log.LogWarning(handlerEx, "AccountStopped subscriber threw; ignoring.");
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "OnProcessExited for {AccountId} threw; ignoring.", e.AccountId);
        }
    }

    private async Task<Account?> ResolveAccountAsync(Guid accountId)
    {
        lock (_gate)
        {
            if (_accountCache.TryGetValue(accountId, out var cached))
            {
                return cached;
            }
        }

        var list = await _accountStore.ListAsync().ConfigureAwait(false);
        var match = list.FirstOrDefault(a => a.Id == accountId);
        if (match is not null)
        {
            lock (_gate)
            {
                _accountCache[accountId] = match;
            }
        }
        return match;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _tracker.ProcessAttached -= OnProcessAttached;
            _tracker.ProcessExited -= OnProcessExited;
        }
        catch
        {
            // Dispose must not throw.
        }
        lock (_gate)
        {
            _accountCache.Clear();
        }
    }
}
