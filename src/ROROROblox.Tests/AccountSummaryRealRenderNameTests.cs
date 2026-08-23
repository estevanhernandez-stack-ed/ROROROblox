using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.StreamerMode;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// F-123: the history writer persisted raw <c>DisplayName</c> while the app's rule everywhere
/// else was <c>LocalName ?? DisplayName</c> — the third site of the drift the v1.10 window-title
/// fix closed twice. The fix is a shared property rather than a third inline expression, and
/// these tests pin the two rules that make it correct: local rename wins, and streamer mode
/// does NOT leak into the persisted value.
/// </summary>
public class AccountSummaryRealRenderNameTests
{
    private static AccountSummary NewSummary(string displayName = "OldRobloxName", string? localName = null)
    {
        var account = new Account(
            Id: Guid.NewGuid(),
            DisplayName: displayName,
            AvatarUrl: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastLaunchedAt: null,
            Tags: null);
        return new AccountSummary(account) { LocalName = localName };
    }

    [Fact]
    public void ALocalRenameWins()
        => Assert.Equal("Grinder", NewSummary(localName: "Grinder").RealRenderName);

    [Fact]
    public void WithoutARenameTheRobloxNameStands()
        => Assert.Equal("OldRobloxName", NewSummary().RealRenderName);

    /// <summary>Substitutes a fixed fake for every account — an always-active streamer provider.</summary>
    private sealed class AlwaysFakeIdentity : IStreamerIdentityProvider
    {
        public bool IsActive => true;
        public Task InitializeAsync(IReadOnlyCollection<(Guid accountId, StreamerIdentity identity)> accountIdentities)
            => Task.CompletedTask;
        public Task SetActiveAsync(bool active) => Task.CompletedTask;
        public DisplayIdentity ForAccount(Guid accountId, string realName, string realAvatarUrl)
            => new("StreamerFake", realAvatarUrl);
        public DisplayIdentity ForFriend(long robloxUserId, string realName, string realAvatarUrl)
            => new("StreamerFake", realAvatarUrl);
        public Task RerollAsync(string identityKey) => Task.CompletedTask;
        public Task RerollAllAsync() => Task.CompletedTask;
        public event EventHandler? Changed { add { } remove { } }
    }

    [Fact]
    public void StreamerModeDoesNotLeakIntoTheRealName()
    {
        // RenderName is the on-screen value and follows the provider; RealRenderName is the
        // PERSISTED value and must not — a history row written mid-stream would otherwise bake
        // the fake name into the file permanently.
        var summary = NewSummary(localName: "Grinder");
        summary.AttachIdentityProvider(new AlwaysFakeIdentity());

        Assert.Equal("StreamerFake", summary.RenderName);
        Assert.Equal("Grinder", summary.RealRenderName);
    }
}
