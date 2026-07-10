using ROROROblox.Core;
using ROROROblox.Core.StreamerMode;

namespace ROROROblox.Tests;

public class StreamerIdentityProviderTests
{
    private static StreamerIdentityProvider Make(bool active = false)
    {
        var settings = new FakeSettings(active);
        var provider = new StreamerIdentityProvider(
            new StreamerNamePool(new[] { "CaptainNoodle", "SirRerollington", "LadyPixel" }),
            new StreamerAvatarPool(new[] { "noodle", "duck", "potato" }),
            new InMemoryIdentityStore(),
            settings,
            persistAccount: (_, _) => Task.CompletedTask);
        provider.InitializeAsync(System.Array.Empty<(Guid, StreamerIdentity)>()).GetAwaiter().GetResult();
        return provider;
    }

    private static readonly Guid A = Guid.NewGuid();

    [Fact]
    public async Task Inactive_ReturnsRealIdentityVerbatim()
    {
        var p = Make(active: false);
        var id = p.ForAccount(A, "RealName", "https://real/avatar.png");
        Assert.Equal("RealName", id.Name);
        Assert.Equal("https://real/avatar.png", id.AvatarSource);
    }

    [Fact]
    public async Task Active_LeakScan_NeverReturnsRealNameAvatarOrUrl()
    {
        var p = Make(active: true);
        var id = p.ForAccount(A, "RealName", "https://real/avatar.png");
        Assert.NotEqual("RealName", id.Name);
        Assert.DoesNotContain("real", id.AvatarSource, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", id.AvatarSource, System.StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("pack://", id.AvatarSource);
    }

    [Fact]
    public async Task Active_SameAccount_StableAcrossCalls()
    {
        var p = Make(active: true);
        var first = p.ForAccount(A, "RealName", "x");
        var second = p.ForAccount(A, "RealName", "x");
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Reroll_ChangesTheIdentity_AndRaisesChanged()
    {
        var p = Make(active: true);
        var before = p.ForAccount(A, "RealName", "x");
        var raised = false; p.Changed += (_, _) => raised = true;
        await p.RerollAsync($"account:{A}");
        var after = p.ForAccount(A, "RealName", "x");
        Assert.True(raised);
        Assert.NotEqual(before.Name, after.Name);
    }

    [Fact]
    public async Task SetActive_PersistsToSettings_AndRaisesChanged()
    {
        var p = Make(active: false);
        var raised = false; p.Changed += (_, _) => raised = true;
        await p.SetActiveAsync(true);
        Assert.True(p.IsActive);
        Assert.True(raised);
    }

    private sealed class FakeSettings : IAppSettings
    {
        private bool _on;
        public FakeSettings(bool on) => _on = on;
        public Task<bool> GetStreamerModeAsync() => Task.FromResult(_on);
        public Task SetStreamerModeAsync(bool on) { _on = on; return Task.CompletedTask; }
        // Remaining IAppSettings members throw NotImplementedException — not exercised here.
        public Task<string?> GetDefaultPlaceUrlAsync() => throw new NotImplementedException();
        public Task SetDefaultPlaceUrlAsync(string url) => throw new NotImplementedException();
        public Task<bool> GetLaunchMainOnStartupAsync() => throw new NotImplementedException();
        public Task SetLaunchMainOnStartupAsync(bool e) => throw new NotImplementedException();
        public Task<string?> GetActiveThemeIdAsync() => throw new NotImplementedException();
        public Task SetActiveThemeIdAsync(string t) => throw new NotImplementedException();
        public Task<bool> GetBloxstrapWarningDismissedAsync() => throw new NotImplementedException();
        public Task SetBloxstrapWarningDismissedAsync(bool v) => throw new NotImplementedException();
        public Task<bool> GetMuteIdleAlertsAsync() => throw new NotImplementedException();
        public Task SetMuteIdleAlertsAsync(bool m) => throw new NotImplementedException();
        public Task<int> GetIdleWarnThresholdMinutesAsync() => throw new NotImplementedException();
        public Task SetIdleWarnThresholdMinutesAsync(int m) => throw new NotImplementedException();
        public Task<bool> GetCarefulSquadLaunchAsync() => throw new NotImplementedException();
        public Task SetCarefulSquadLaunchAsync(bool c) => throw new NotImplementedException();
    }

    private sealed class InMemoryIdentityStore : IStreamerIdentityStore
    {
        private readonly Dictionary<string, StreamerIdentity> _m = new();
        public Task<IReadOnlyDictionary<string, StreamerIdentity>> LoadAllAsync()
            => Task.FromResult<IReadOnlyDictionary<string, StreamerIdentity>>(_m);
        public Task SaveAsync(string key, StreamerIdentity id) { _m[key] = id; return Task.CompletedTask; }
    }
}
