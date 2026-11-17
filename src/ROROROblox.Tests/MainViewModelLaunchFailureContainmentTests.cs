using Microsoft.Extensions.Logging;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Transport;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// The follow chip (<c>MainWindow.OnFollowChipClick</c>) and the pasted-link modal
/// (<c>OnGameComboBoxSelectionChanged</c>) are <c>async void</c> handlers that await
/// <c>FollowAltAsync</c> / <c>OpenJoinByLinkAsync</c>; both end in <c>LaunchAccountAsync</c>, which
/// was try/finally with no catch. An account-store failure there escaped to the dispatcher, where
/// App.xaml.cs leaves DispatcherUnhandledException unhandled on purpose, and the app ended. From the
/// Launch button the same exception was swallowed by RelayCommand with no log and the row left on
/// "Launching...". These tests drive the two awaited entry points, which is exactly what the
/// handlers await: if they complete, the handlers cannot throw.
/// </summary>
public class MainViewModelLaunchFailureContainmentTests
{
    private sealed class CapturingLog : ILogger<MainViewModel>
    {
        public List<(LogLevel Level, string Message, Exception? Ex)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception), exception));
    }

    private sealed class StartedLauncher : IRobloxLauncher
    {
        public Task<LaunchResult> LaunchAsync(string cookie, LaunchTarget target, int? fpsCap = null, long? browserTrackerId = null)
            => Task.FromResult<LaunchResult>(new LaunchResult.Started(4242, DateTimeOffset.UtcNow));
        public Task<LaunchResult> LaunchAsync(string cookie, string? placeUrl = null, int? fpsCap = null, long? browserTrackerId = null)
            => throw new NotImplementedException();
    }

    /// <summary>Real store, except the cookie read (or the last-launched write) throws.</summary>
    private sealed class FailingStore(IAccountStore inner, bool failRead, bool failTouch) : IAccountStore
    {
        public Task<string> RetrieveCookieAsync(Guid id)
            => failRead ? throw new IOException("simulated vault read failure") : inner.RetrieveCookieAsync(id);
        public Task TouchLastLaunchedAsync(Guid id)
            => failTouch ? throw new IOException("simulated vault write failure") : inner.TouchLastLaunchedAsync(id);

        public Task UpdateRobloxUserIdAsync(Guid accountId, long userId) => inner.UpdateRobloxUserIdAsync(accountId, userId);
        public Task UpdateBrowserTrackerIdAsync(Guid accountId, long browserTrackerId) => inner.UpdateBrowserTrackerIdAsync(accountId, browserTrackerId);
        public int GetCookieGeneration(Guid id) => inner.GetCookieGeneration(id);
        public Task<IReadOnlyList<Account>> ListAsync() => inner.ListAsync();
        public Task<Account> AddAsync(string displayName, string avatarUrl, string cookie) => inner.AddAsync(displayName, avatarUrl, cookie);
        public Task RemoveAsync(Guid id) => inner.RemoveAsync(id);
        public Task UpdateCookieAsync(Guid id, string newCookie) => inner.UpdateCookieAsync(id, newCookie);
        public Task SetMainAsync(Guid id) => inner.SetMainAsync(id);
        public Task UpdateSortOrderAsync(IReadOnlyList<Guid> idsInOrder) => inner.UpdateSortOrderAsync(idsInOrder);
        public Task SetSelectedAsync(Guid id, bool isSelected) => inner.SetSelectedAsync(id, isSelected);
        public Task SetJoinViaFriendAsync(Guid id, bool joinViaFriend) => inner.SetJoinViaFriendAsync(id, joinViaFriend);
        public Task SetAutoRejoinAsync(Guid id, bool autoRejoin) => inner.SetAutoRejoinAsync(id, autoRejoin);
        public Task SetCaptionColorAsync(Guid id, string? hex) => inner.SetCaptionColorAsync(id, hex);
        public Task SetFpsCapAsync(Guid id, int? fps) => inner.SetFpsCapAsync(id, fps);
        public Task UpdateLocalNameAsync(Guid accountId, string? localName) => inner.UpdateLocalNameAsync(accountId, localName);
        public Task UpdateStreamerIdentityAsync(Guid accountId, string fakeName, string fakeAvatarId) => inner.UpdateStreamerIdentityAsync(accountId, fakeName, fakeAvatarId);
        public Task SetTagsAsync(Guid id, IReadOnlyList<string> tags) => inner.SetTagsAsync(id, tags);
        public Task<AccountExportResult> ExportAccountsAsync(IEnumerable<Guid> ids) => inner.ExportAccountsAsync(ids);
        public Task<ImportMergeResult> ImportMergeAsync(IReadOnlyList<AccountExportRecord> records) => inner.ImportMergeAsync(records);
        public Task SetFactsAsync(Guid id, AccountFacts facts) => inner.SetFactsAsync(id, facts);
        public Task UpdateNoteAsync(Guid id, string? note) => inner.UpdateNoteAsync(id, note);
        public Task SetPanelExpandedAsync(Guid id, bool expanded) => inner.SetPanelExpandedAsync(id, expanded);
    }

    private static async Task<(MainViewModel Vm, AccountSummary Row, CapturingLog Log, string Path)> BuildAsync(
        bool failRead, bool failTouch, IRobloxLauncher? launcher = null)
    {
        var log = new CapturingLog();
        var (vm, store, _, path) = MainViewModelTests.Build(
            launcher: launcher,
            wrapStore: inner => new FailingStore(inner, failRead, failTouch),
            log: log);
        var added = await store.AddAsync("Alt", "", "FAKE-COOKIE-alt");
        return (vm, new AccountSummary(added), log, path);
    }

    private static void AssertContained(AccountSummary row, CapturingLog log)
    {
        Assert.False(row.IsLaunching, "The busy state must clear after a failed launch.");
        // The same sentence a failed auth-ticket launch puts on the row (CoreMsg_Launch_AuthTicketFailed).
        Assert.Contains("sign this account in to Roblox", row.StatusText, StringComparison.Ordinal);
        // The type, never the message: a real store failure's message names a path under the user's profile.
        Assert.Contains(nameof(IOException), row.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("simulated vault", row.StatusText, StringComparison.Ordinal);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Ex is IOException);
    }

    [Fact]
    public async Task FollowAlt_StoreReadFails_IsContainedLoggedAndShownOnTheRow()
    {
        var (vm, source, log, path) = await BuildAsync(failRead: true, failTouch: false);
        try
        {
            var target = new AccountSummary(new Account(Guid.NewGuid(), "Friend", "", DateTimeOffset.UtcNow, null))
            {
                RobloxUserId = 777,
                PresenceState = UserPresenceType.InGame,
                CurrentPlaceId = 8737899170,
            };

            var ex = await Record.ExceptionAsync(() => vm.FollowAltAsync(source, target));

            Assert.Null(ex);
            AssertContained(source, log);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task OpenJoinByLink_StoreReadFails_IsContainedLoggedAndShownOnTheRow()
    {
        var (vm, row, log, path) = await BuildAsync(failRead: true, failTouch: false);
        try
        {
            vm.JoinByLinkPicker = _ => (new LaunchTarget.Place(8737899170), false);

            var ex = await Record.ExceptionAsync(() => vm.OpenJoinByLinkAsync(row));

            Assert.Null(ex);
            AssertContained(row, log);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task OpenJoinByLink_LastLaunchedWriteFails_ClientIsStillTrackedAndNothingEscapes()
    {
        // The write after a launch that started must not be reported as a failed launch: the
        // client is running, and the row must not say otherwise.
        var (vm, row, log, path) = await BuildAsync(failRead: false, failTouch: true, launcher: new StartedLauncher());
        try
        {
            vm.JoinByLinkPicker = _ => (new LaunchTarget.Place(8737899170), false);

            var ex = await Record.ExceptionAsync(() => vm.OpenJoinByLinkAsync(row));

            Assert.Null(ex);
            Assert.False(row.IsLaunching);
            Assert.DoesNotContain("sign this account in", row.StatusText, StringComparison.Ordinal);
            Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Ex is IOException);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
