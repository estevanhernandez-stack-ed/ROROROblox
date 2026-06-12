using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace ROROROblox.Core;

/// <summary>
/// Lock-free, point-in-time read model over an <see cref="ObservableCollection{T}"/> that is
/// mutated on a single owner thread (in RoRoRo: <c>MainViewModel.Accounts</c> on the WPF UI
/// thread) but read from arbitrary threads (presence poll loop, plugin gRPC handlers,
/// process-tracker event bridges).
///
/// Enumerating an ObservableCollection concurrently with a mutation throws
/// "Collection was modified" — and before this class existed, that fault was what silently
/// killed the v1.5 presence loop (2026-06-12 review). The mirror republishes an immutable
/// copy on every CollectionChanged (raised on the mutating thread, so the copy itself is
/// race-free); readers grab <see cref="Snapshot"/> and enumerate a list that can never change
/// under them.
/// </summary>
public sealed class ObservableCollectionMirror<T> : IDisposable
{
    private readonly ObservableCollection<T> _source;
    private volatile IReadOnlyList<T> _snapshot;
    private bool _disposed;

    public ObservableCollectionMirror(ObservableCollection<T> source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _snapshot = [.. source];
        _source.CollectionChanged += OnCollectionChanged;
    }

    /// <summary>The current point-in-time copy. Safe to enumerate from any thread.</summary>
    public IReadOnlyList<T> Snapshot => _snapshot;

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => _snapshot = [.. _source];

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _source.CollectionChanged -= OnCollectionChanged;
    }
}
