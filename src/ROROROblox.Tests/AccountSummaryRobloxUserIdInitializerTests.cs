using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.Tests;

public class AccountSummaryRobloxUserIdInitializerTests
{
    [Fact]
    public void Constructor_CopiesRobloxUserIdFromAccount()
    {
        var account = new Account(
            Id: Guid.NewGuid(),
            DisplayName: "TestAlt",
            AvatarUrl: "https://example.com/avatar.png",
            CreatedAt: DateTimeOffset.UtcNow,
            LastLaunchedAt: null,
            RobloxUserId: 47821334L);

        var summary = new AccountSummary(account);

        Assert.Equal(47821334L, summary.RobloxUserId);
    }

    [Fact]
    public void Constructor_NullRobloxUserId_LeavesSummaryNull()
    {
        var account = new Account(
            Id: Guid.NewGuid(),
            DisplayName: "FreshAlt",
            AvatarUrl: "https://example.com/avatar.png",
            CreatedAt: DateTimeOffset.UtcNow,
            LastLaunchedAt: null,
            RobloxUserId: null);

        var summary = new AccountSummary(account);

        Assert.Null(summary.RobloxUserId);
    }
}
