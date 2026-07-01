using System;
using System.Collections.Generic;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

public class ActivitySnapshotApplierTests
{
    private static AccountSummary Row(Guid id)
    {
        var account = new Account(
            Id: id,
            DisplayName: "Alt",
            AvatarUrl: "https://x/a.png",
            CreatedAt: DateTimeOffset.UtcNow,
            LastLaunchedAt: null,
            RobloxUserId: 1L);
        return new AccountSummary(account) { IsRunning = true };
    }

    [Fact]
    public void Apply_MatchingId_SetsSinceAndWarn()
    {
        var id = Guid.NewGuid();
        var row = Row(id);
        var snap = new List<AccountActivity>
        {
            new(id, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(20)),
        };

        ActivitySnapshotApplier.Apply(new[] { row }, snap, TimeSpan.FromMinutes(15));

        Assert.Equal(TimeSpan.FromMinutes(20), row.SinceActivity);
        Assert.True(row.IdleWarn);
    }

    [Fact]
    public void Apply_BelowThreshold_WarnFalse()
    {
        var id = Guid.NewGuid();
        var row = Row(id);
        var snap = new List<AccountActivity>
        {
            new(id, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5)),
        };

        ActivitySnapshotApplier.Apply(new[] { row }, snap, TimeSpan.FromMinutes(15));

        Assert.Equal(TimeSpan.FromMinutes(5), row.SinceActivity);
        Assert.False(row.IdleWarn);
    }

    [Fact]
    public void Apply_UnknownRow_ClearsIdle()
    {
        var row = Row(Guid.NewGuid());
        row.SinceActivity = TimeSpan.FromMinutes(30);
        row.IdleWarn = true;

        ActivitySnapshotApplier.Apply(new[] { row }, new List<AccountActivity>(), TimeSpan.FromMinutes(15));

        Assert.Null(row.SinceActivity);
        Assert.False(row.IdleWarn);
    }
}
