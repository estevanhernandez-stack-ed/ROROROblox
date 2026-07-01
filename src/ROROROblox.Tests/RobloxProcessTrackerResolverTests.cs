using System;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

public class RobloxProcessTrackerResolverTests
{
    [Fact]
    public void TryResolveAccountByPid_UnknownPid_ReturnsFalse()
    {
        using var tracker = new RobloxProcessTracker(NullLogger<RobloxProcessTracker>.Instance);
        IForegroundAccountResolver resolver = tracker;

        var found = resolver.TryResolveAccountByPid(999999, out var accountId);

        Assert.False(found);
        Assert.Equal(Guid.Empty, accountId);
    }
}
