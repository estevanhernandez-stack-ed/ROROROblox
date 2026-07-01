using System;
using System.Collections.Generic;
using System.Linq;
using ROROROblox.App.Plugins;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

public class ActivitySnapshotProviderTests
{
    private sealed class StubMonitor : IActivityMonitor
    {
        public List<AccountActivity> Items = new();
        public TimeSpan WarnThreshold { get; set; } = TimeSpan.FromMinutes(15);
        public event EventHandler<IReadOnlyList<Guid>>? WarnThresholdCrossed { add { } remove { } }
        public void OnAccountLaunched(Guid accountId) { }
        public void OnAccountExited(Guid accountId) { }
        public void Sample() { }
        public void Start() { }
        public void Stop() { }
        public IReadOnlyList<AccountActivity> GetSnapshot() => Items;
    }

    [Fact]
    public void Snapshot_ProjectsGuidToStringAndTimesToUnixAndSeconds()
    {
        var id = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        var monitor = new StubMonitor
        {
            Items = { new AccountActivity(id, at, TimeSpan.FromSeconds(300)) },
        };
        var provider = new ActivitySnapshotProvider(monitor);

        var item = provider.Snapshot().Single();
        Assert.Equal(id.ToString(), item.AccountId);
        Assert.Equal(at.ToUnixTimeMilliseconds(), item.LastActivityUnixMs);
        Assert.Equal(300, item.SecondsSinceActivity);
    }

    [Fact]
    public void Snapshot_ClampsNegativeSecondsToZero()
    {
        var monitor = new StubMonitor
        {
            Items = { new AccountActivity(Guid.NewGuid(), DateTimeOffset.UtcNow, TimeSpan.FromSeconds(-5)) },
        };
        var provider = new ActivitySnapshotProvider(monitor);
        Assert.Equal(0, provider.Snapshot().Single().SecondsSinceActivity);
    }
}
