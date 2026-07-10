using ROROROblox.App.Tray;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.StreamerMode;

namespace ROROROblox.Tests;

/// <summary>
/// Regression coverage for <see cref="RunningRobloxScanner"/>'s restart re-attach match rule.
/// Before v1.10 the scanner matched a parsed window-title name against each account's raw
/// <c>DisplayName</c>. When the decorator started writing the LocalName-nickname / streamer-mode
/// fake name instead (v1.10), that match silently failed and running windows stopped being
/// re-attached across an app restart — on EVERY restart for nickname users, and whenever streamer
/// mode was active for streamer users. The scanner now resolves each account's expected title name
/// the SAME way the decorator writes it (<see cref="RobloxWindowTitle.ResolveName"/>). These tests
/// pin that agreement. <see cref="RunningRobloxScanner.Scan"/> itself enumerates the OS process
/// table and can't be unit-tested, so we exercise the extracted
/// <see cref="RunningRobloxScanner.MatchAccountByTitleName"/> rule plus a format-then-parse
/// round-trip that guards against the two sides drifting apart again.
/// </summary>
public class RunningRobloxScannerTests
{
    private static AccountSummary Summary(string displayName, string? localName = null, Guid? id = null)
    {
        var account = new Account(
            Id: id ?? Guid.NewGuid(),
            DisplayName: displayName,
            AvatarUrl: "https://avatar.example/x.png",
            CreatedAt: DateTimeOffset.UtcNow,
            LastLaunchedAt: null,
            LocalName: localName);
        return new AccountSummary(account);
    }

    // === The headline regression: LocalName nickname, streamer mode OFF ===

    [Fact]
    public void NicknameWindow_StreamerOff_MatchesTheAccount()
    {
        var acct = Summary(displayName: "RealAlt_9182", localName: "Sneaky");
        var accounts = new[] { acct, Summary("SomeoneElse") };

        // The decorator writes "Roblox - Sneaky" for this account when streamer mode is off.
        var match = RunningRobloxScanner.MatchAccountByTitleName(accounts, "Sneaky", identity: null);

        Assert.Same(acct, match);
    }

    [Fact]
    public void NicknameWindow_StreamerOff_DoesNotMatchOnRawDisplayName()
    {
        // The pre-v1.10 bug in reverse: a window titled with the raw DisplayName is no longer what
        // the decorator writes for a nicknamed account (it writes the LocalName), so it must not
        // match. Matching on DisplayName here is exactly what broke re-attach.
        var acct = Summary(displayName: "RealAlt_9182", localName: "Sneaky");
        var accounts = new[] { acct };

        var match = RunningRobloxScanner.MatchAccountByTitleName(accounts, "RealAlt_9182", identity: null);

        Assert.Null(match);
    }

    [Fact]
    public void NoNickname_StreamerOff_MatchesByDisplayName()
    {
        // Base case still works: no LocalName, no provider → title name is the DisplayName.
        var acct = Summary(displayName: "PlainAlt");
        var accounts = new[] { acct };

        var match = RunningRobloxScanner.MatchAccountByTitleName(accounts, "PlainAlt", identity: null);

        Assert.Same(acct, match);
    }

    [Fact]
    public void InactiveProvider_MatchesByRenderName()
    {
        // A real (non-null) but inactive provider behaves like no provider: title name = LocalName.
        // This is the runtime shape when a nickname user isn't streaming — the case that broke daily.
        var acct = Summary(displayName: "RealAlt", localName: "Nick");
        var accounts = new[] { acct };
        var provider = new FakeStreamerIdentityProvider(active: false);

        Assert.Same(acct, RunningRobloxScanner.MatchAccountByTitleName(accounts, "Nick", provider));
        Assert.Null(RunningRobloxScanner.MatchAccountByTitleName(accounts, "RealAlt", provider));
    }

    // === Streamer mode ON: match on the fake name, not the real names ===

    [Fact]
    public void StreamerModeActive_MatchesByFakeName_NotRealNames()
    {
        var id = Guid.NewGuid();
        var acct = Summary(displayName: "RealAlt_9182", localName: "Sneaky", id: id);
        var accounts = new[] { acct };
        var provider = new FakeStreamerIdentityProvider(
            active: true,
            fakeNames: new Dictionary<Guid, string> { [id] = "CaptainNoodle" });

        // Decorator writes "Roblox - CaptainNoodle" while active → scanner re-attaches on it.
        Assert.Same(acct, RunningRobloxScanner.MatchAccountByTitleName(accounts, "CaptainNoodle", provider));
        // Neither real name is on the window, so neither may match — no leak, no false re-attach.
        Assert.Null(RunningRobloxScanner.MatchAccountByTitleName(accounts, "RealAlt_9182", provider));
        Assert.Null(RunningRobloxScanner.MatchAccountByTitleName(accounts, "Sneaky", provider));
    }

    // === The invariant that guards against future drift: decorator-format round-trips to scanner-parse ===

    [Theory]
    [InlineData("PlainAlt", null, false)]        // no nickname, streamer off
    [InlineData("RealAlt_9182", "Sneaky", false)] // nickname, streamer off (the daily-break case)
    [InlineData("RealAlt_9182", "Sneaky", true)]  // nickname + streamer active (the across-restart case)
    public void DecoratorTitle_RoundTripsThroughScannerMatch(string displayName, string? localName, bool streamerActive)
    {
        var id = Guid.NewGuid();
        var acct = Summary(displayName, localName, id);
        var accounts = new[] { acct };
        IStreamerIdentityProvider? provider = streamerActive
            ? new FakeStreamerIdentityProvider(true, new Dictionary<Guid, string> { [id] = "CaptainNoodle" })
            : null;

        // Exactly what the decorator stamps on the window.
        var title = RobloxWindowTitle.Format(provider, id, localName ?? displayName, acct.AvatarUrl);

        // Exactly what the scanner does: regex-parse the title, then match the parsed name.
        var m = RobloxWindowTitle.Pattern.Match(title);
        Assert.True(m.Success);
        var match = RunningRobloxScanner.MatchAccountByTitleName(accounts, m.Groups[1].Value, provider);

        Assert.Same(acct, match);
    }

    /// <summary>
    /// Minimal <see cref="IStreamerIdentityProvider"/> fake — returns the mapped fake name for a
    /// known account id while active, otherwise passes the real name through (mirroring the real
    /// provider's inactive/unknown behavior). Only the members the scanner touches do real work.
    /// </summary>
    private sealed class FakeStreamerIdentityProvider : IStreamerIdentityProvider
    {
        private readonly IReadOnlyDictionary<Guid, string> _fakeNames;

        public FakeStreamerIdentityProvider(bool active, IReadOnlyDictionary<Guid, string>? fakeNames = null)
        {
            IsActive = active;
            _fakeNames = fakeNames ?? new Dictionary<Guid, string>();
        }

        public bool IsActive { get; }
        public event EventHandler? Changed;

        public DisplayIdentity ForAccount(Guid accountId, string realName, string realAvatarUrl)
            => IsActive && _fakeNames.TryGetValue(accountId, out var fake)
                ? new DisplayIdentity(fake, "pack://app/fake.png")
                : new DisplayIdentity(realName, realAvatarUrl);

        public DisplayIdentity ForFriend(long robloxUserId, string realName, string realAvatarUrl)
            => new(realName, realAvatarUrl);

        public Task InitializeAsync(IReadOnlyCollection<(Guid accountId, StreamerIdentity identity)> accountIdentities) => Task.CompletedTask;
        public Task SetActiveAsync(bool active) => Task.CompletedTask;
        public Task RerollAsync(string identityKey) => Task.CompletedTask;
        public Task RerollAllAsync() => Task.CompletedTask;
    }
}
