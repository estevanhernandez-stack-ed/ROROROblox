using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// Adapter coverage. Drives a FakeProcessTracker + InMemoryAccountStore through the lifecycle
/// events and asserts AccountLifecycleTracker re-emits them with Account + count enrichment.
/// </summary>
public class AccountLifecycleTrackerTests
{
    private static readonly Account Account1 = new(
        Id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        DisplayName: "Player1",
        AvatarUrl: "https://x/1.png",
        CreatedAt: DateTimeOffset.UtcNow,
        LastLaunchedAt: null,
        IsMain: false,
        SortOrder: 0,
        IsSelected: true,
        CaptionColorHex: null);

    private static readonly Account Account2 = new(
        Id: Guid.Parse("22222222-2222-2222-2222-222222222222"),
        DisplayName: "Player2",
        AvatarUrl: "https://x/2.png",
        CreatedAt: DateTimeOffset.UtcNow,
        LastLaunchedAt: null,
        IsMain: false,
        SortOrder: 1,
        IsSelected: true,
        CaptionColorHex: null);

    [Fact]
    public async Task ProcessAttached_RaisesAccountStarted_WithCountOne()
    {
        var tracker = new FakeProcessTracker();
        var store = new InMemoryAccountStore(Account1);
        using var sut = new AccountLifecycleTracker(tracker, store, NullLogger<AccountLifecycleTracker>.Instance);

        var tcs = new TaskCompletionSource<AccountStartedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.AccountStarted += (_, args) => tcs.TrySetResult(args);

        tracker.SimulateAttached(Account1.Id, pid: 1234);

        var observed = await Wait(tcs.Task, TimeSpan.FromSeconds(2));
        Assert.NotNull(observed);
        Assert.Equal(Account1.Id, observed!.Account.Id);
        Assert.Equal(1234, observed.ProcessId);
        Assert.Equal(1, observed.CurrentActiveCount);
    }

    [Fact]
    public async Task ProcessExited_RaisesAccountStopped_WithCountReflectingRemoval()
    {
        var tracker = new FakeProcessTracker();
        var store = new InMemoryAccountStore(Account1, Account2);
        using var sut = new AccountLifecycleTracker(tracker, store, NullLogger<AccountLifecycleTracker>.Instance);

        // Two accounts up, one exits → count after exit = 1.
        tracker.SimulateAttached(Account1.Id, pid: 100);
        tracker.SimulateAttached(Account2.Id, pid: 200);

        var stoppedTcs = new TaskCompletionSource<AccountStoppedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.AccountStopped += (_, args) => stoppedTcs.TrySetResult(args);

        tracker.SimulateExited(Account2.Id, pid: 200);

        var observed = await Wait(stoppedTcs.Task, TimeSpan.FromSeconds(2));
        Assert.NotNull(observed);
        Assert.Equal(Account2.Id, observed!.Account.Id);
        Assert.Equal(200, observed.ProcessId);
        Assert.Equal(1, observed.CurrentActiveCount);
    }

    [Fact]
    public async Task ProcessAttached_ForUnknownAccount_DoesNotRaise()
    {
        var tracker = new FakeProcessTracker();
        var store = new InMemoryAccountStore(/* empty */);
        using var sut = new AccountLifecycleTracker(tracker, store, NullLogger<AccountLifecycleTracker>.Instance);

        var raised = false;
        sut.AccountStarted += (_, _) => raised = true;

        tracker.SimulateAttached(Guid.NewGuid(), pid: 1);
        await Task.Delay(150);

        Assert.False(raised);
    }

    [Fact]
    public async Task ParallelAttachAndExit_DoesNotDriftCount()
    {
        var tracker = new FakeProcessTracker();
        var store = new InMemoryAccountStore(Account1, Account2);
        using var sut = new AccountLifecycleTracker(tracker, store, NullLogger<AccountLifecycleTracker>.Instance);

        var startedCounts = new List<int>();
        var stoppedCounts = new List<int>();
        var startedGate = new SemaphoreSlim(1, 1);
        var stoppedGate = new SemaphoreSlim(1, 1);

        sut.AccountStarted += (_, args) =>
        {
            startedGate.Wait();
            try { startedCounts.Add(args.CurrentActiveCount); } finally { startedGate.Release(); }
        };
        sut.AccountStopped += (_, args) =>
        {
            stoppedGate.Wait();
            try { stoppedCounts.Add(args.CurrentActiveCount); } finally { stoppedGate.Release(); }
        };

        // Burst: 2 attaches then 2 exits
        tracker.SimulateAttached(Account1.Id, pid: 1);
        tracker.SimulateAttached(Account2.Id, pid: 2);
        await Task.Delay(100);
        tracker.SimulateExited(Account1.Id, pid: 1);
        tracker.SimulateExited(Account2.Id, pid: 2);
        await Task.Delay(150);

        // Counts should reflect the tracker's snapshot after each event.
        Assert.Equal(2, startedCounts.Count);
        Assert.Equal(2, stoppedCounts.Count);
        // After both exits, tracker.Attached.Count should be 0 → last stopped count = 0.
        Assert.Contains(0, stoppedCounts);
    }

    [Fact]
    public void Dispose_UnsubscribesFromTracker()
    {
        var tracker = new FakeProcessTracker();
        var store = new InMemoryAccountStore(Account1);
        var sut = new AccountLifecycleTracker(tracker, store, NullLogger<AccountLifecycleTracker>.Instance);

        var raised = false;
        sut.AccountStarted += (_, _) => raised = true;

        sut.Dispose();
        sut.Dispose(); // idempotent

        tracker.SimulateAttached(Account1.Id, pid: 1);
        // After dispose, we should not see the event.
        Assert.False(raised);
    }

    private static async Task<T?> Wait<T>(Task<T> task, TimeSpan budget)
    {
        var done = await Task.WhenAny(task, Task.Delay(budget));
        return done == task ? task.Result : default;
    }

    /// <summary>Fake — just exposes Simulate* helpers and tracks the Attached snapshot.</summary>
    private sealed class FakeProcessTracker : IRobloxProcessTracker
    {
        private readonly Dictionary<Guid, TrackedProcess> _attached = new();
        public IReadOnlyDictionary<Guid, TrackedProcess> Attached => _attached;

        public void SimulateAttached(Guid accountId, int pid)
        {
            _attached[accountId] = new TrackedProcess(pid, DateTimeOffset.UtcNow);
            ProcessAttached?.Invoke(this, new RobloxProcessEventArgs(accountId, pid));
        }

        public void SimulateExited(Guid accountId, int pid)
        {
            _attached.Remove(accountId);
            ProcessExited?.Invoke(this, new RobloxProcessEventArgs(accountId, pid));
        }

        public Task TrackLaunchAsync(Guid accountId, DateTimeOffset launchedAtUtc, CancellationToken ct = default) => Task.CompletedTask;
        public bool AttachExisting(Guid accountId, int pid) => false;
        public bool IsTracking(Guid accountId) => _attached.ContainsKey(accountId);
        public bool RequestClose(Guid accountId) => false;
        public bool Kill(Guid accountId) => false;

        public event EventHandler<RobloxProcessEventArgs>? ProcessAttached;
#pragma warning disable CS0067 // not driven by these tests, required by interface
        public event EventHandler<RobloxProcessEventArgs>? ProcessAttachFailed;
#pragma warning restore CS0067
        public event EventHandler<RobloxProcessEventArgs>? ProcessExited;
    }

    /// <summary>Minimal IAccountStore — list-only is all the adapter needs.</summary>
    private sealed class InMemoryAccountStore : IAccountStore
    {
        private readonly List<Account> _accounts;

        public InMemoryAccountStore(params Account[] accounts)
        {
            _accounts = [.. accounts];
        }

        public Task<IReadOnlyList<Account>> ListAsync() =>
            Task.FromResult<IReadOnlyList<Account>>(_accounts);

        public Task<Account> AddAsync(string displayName, string avatarUrl, string cookie) =>
            throw new NotImplementedException();
        public Task RemoveAsync(Guid id) => throw new NotImplementedException();
        public Task<string> RetrieveCookieAsync(Guid id) => throw new NotImplementedException();
        public Task UpdateCookieAsync(Guid id, string newCookie) => throw new NotImplementedException();
        public Task TouchLastLaunchedAsync(Guid id) => throw new NotImplementedException();
        public Task SetMainAsync(Guid id) => throw new NotImplementedException();
        public Task UpdateSortOrderAsync(IReadOnlyList<Guid> idsInOrder) => throw new NotImplementedException();
        public Task SetSelectedAsync(Guid id, bool isSelected) => throw new NotImplementedException();
        public Task SetCaptionColorAsync(Guid id, string? hex) => throw new NotImplementedException();
    }
}
