using System.Collections.ObjectModel;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// 2026-06-12 review: five off-UI-thread readers (presence snapshot delegate, two plugin
/// adapters, two App event bridges) LINQ-enumerated MainViewModel.Accounts — a UI-owned
/// ObservableCollection — from threadpool threads. Concurrent enumeration during a UI-thread
/// Add/Remove throws "Collection was modified". The mirror republishes an immutable snapshot
/// on every mutation (on the mutating thread) so off-thread readers get a stable
/// point-in-time list, lock-free.
/// </summary>
public class ObservableCollectionMirrorTests
{
    [Fact]
    public void Snapshot_ReflectsInitialContents()
    {
        var source = new ObservableCollection<string> { "a", "b" };
        using var mirror = new ObservableCollectionMirror<string>(source);

        Assert.Equal(["a", "b"], mirror.Snapshot);
    }

    [Fact]
    public void Snapshot_TracksAddRemoveMoveClear()
    {
        var source = new ObservableCollection<string> { "a" };
        using var mirror = new ObservableCollectionMirror<string>(source);

        source.Add("b");
        Assert.Equal(["a", "b"], mirror.Snapshot);

        source.Insert(0, "z");
        Assert.Equal(["z", "a", "b"], mirror.Snapshot);

        source.Move(0, 2);
        Assert.Equal(["a", "b", "z"], mirror.Snapshot);

        source.Remove("b");
        Assert.Equal(["a", "z"], mirror.Snapshot);

        source.Clear();
        Assert.Empty(mirror.Snapshot);
    }

    [Fact]
    public void Snapshot_IsPointInTime_CapturedReferenceDoesNotChangeUnderTheReader()
    {
        var source = new ObservableCollection<string> { "a" };
        using var mirror = new ObservableCollectionMirror<string>(source);

        var captured = mirror.Snapshot;
        source.Add("b");

        // The reader that grabbed the snapshot before the mutation keeps the old list —
        // that's the whole contract: no enumeration ever observes a mid-mutation state.
        Assert.Equal(["a"], captured);
        Assert.Equal(["a", "b"], mirror.Snapshot);
    }

    [Fact]
    public void Dispose_DetachesFromSource()
    {
        var source = new ObservableCollection<string> { "a" };
        var mirror = new ObservableCollectionMirror<string>(source);
        mirror.Dispose();

        source.Add("b");

        Assert.Equal(["a"], mirror.Snapshot); // frozen at dispose time
    }

    [Fact]
    public async Task ConcurrentReadersDuringMutation_NeverThrow()
    {
        // The failure mode this class kills: enumeration on one thread while another mutates.
        var source = new ObservableCollection<int>(Enumerable.Range(0, 50));
        using var mirror = new ObservableCollectionMirror<int>(source);
        using var stop = new CancellationTokenSource();

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                foreach (var item in mirror.Snapshot)
                {
                    _ = item;
                }
            }
        })).ToArray();

        for (var i = 0; i < 2_000; i++)
        {
            source.Add(i);
            source.RemoveAt(0);
            if (i % 100 == 0) source.Move(0, source.Count - 1);
        }

        stop.Cancel();
        await Task.WhenAll(readers); // a thrown enumeration would fault its reader task
    }
}
