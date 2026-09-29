using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using ROROROblox.App.ViewModels;

namespace ROROROblox.App.Preferences;

/// <summary>
/// Calls back whenever the answer to "does any account have auto-rejoin on?" could have changed:
/// a row's <see cref="AccountSummary.AutoRejoin"/> flips (the row menu, or a pause turning it off),
/// or a row is added or removed. <see cref="SettingsPage"/> is a non-modal shell page (F-013), so
/// all of those can happen while it is open, and its alerts status line depends on the answer.
/// Watches rows added after construction and lets go of removed ones. Dispose unsubscribes
/// everything. UI thread only, like the collection it watches.
/// </summary>
internal sealed class AutoRejoinUsageWatcher : IDisposable
{
    private readonly ObservableCollection<AccountSummary> _rows;
    private readonly Action _changed;
    private readonly HashSet<AccountSummary> _watched = [];
    private bool _disposed;

    public AutoRejoinUsageWatcher(ObservableCollection<AccountSummary> rows, Action changed)
    {
        _rows = rows ?? throw new ArgumentNullException(nameof(rows));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _rows.CollectionChanged += OnRowsChanged;
        foreach (var row in _rows)
        {
            Watch(row);
        }
    }

    private void Watch(AccountSummary row)
    {
        if (_watched.Add(row))
        {
            row.PropertyChanged += OnRowPropertyChanged;
        }
    }

    private void Unwatch(AccountSummary row)
    {
        if (_watched.Remove(row))
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_disposed) return;

        // Reset (Clear) carries no OldItems, so re-sync against the collection itself.
        foreach (var row in _watched.Where(r => !_rows.Contains(r)).ToList())
        {
            Unwatch(row);
        }
        foreach (var row in _rows)
        {
            Watch(row);
        }
        _changed();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed) return;
        if (e.PropertyName is nameof(AccountSummary.AutoRejoin) or null or "")
        {
            _changed();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _rows.CollectionChanged -= OnRowsChanged;
        foreach (var row in _watched.ToList())
        {
            Unwatch(row);
        }
    }
}
