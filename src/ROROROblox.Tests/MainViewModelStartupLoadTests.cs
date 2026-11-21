using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// <c>MainWindow.OnLoaded</c> is an <c>async void</c> handler: an exception out of it reaches the
/// dispatcher and ends the app on every start. The handler now awaits <c>RunStartupLoadAsync</c>, which
/// never throws, and <c>LoadAsync</c> itself survives a store that fails in a way nobody planned. The
/// account list stays empty in that case, and the tests pin what protects the real vault: the store is
/// the only writer, and every write it makes begins with its own read.
/// </summary>
public sealed class MainViewModelStartupLoadTests
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

    /// <summary>Records every call; ListAsync throws a type no catch in the view model names.</summary>
    public class RecordingStore : DispatchProxy
    {
        public List<string> Calls { get; } = new();
        public Exception? ListFailure { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add(targetMethod!.Name);
            if (targetMethod.Name == nameof(IAccountStore.ListAsync) && ListFailure is { } ex)
            {
                return Task.FromException<IReadOnlyList<Account>>(ex);
            }
            return targetMethod.ReturnType == typeof(Task) ? Task.CompletedTask : throw new NotSupportedException(targetMethod.Name);
        }
    }

    private static (IAccountStore Store, RecordingStore Recorder) FailingListStore(Exception failure)
    {
        var proxy = DispatchProxy.Create<IAccountStore, RecordingStore>();
        var recorder = (RecordingStore)(object)proxy;
        recorder.ListFailure = failure;
        return (proxy, recorder);
    }

    private sealed class ThrowingFavorites : IFavoriteGameStore
    {
        public event EventHandler? DefaultChanged { add { } remove { } }
        public Task<IReadOnlyList<FavoriteGame>> ListAsync() => throw new InvalidOperationException("simulated favorites failure");
        public Task<FavoriteGame?> GetDefaultAsync() => throw new InvalidOperationException();
        public Task<FavoriteGame> AddAsync(long placeId, long universeId, string name, string thumbnailUrl) => throw new InvalidOperationException();
        public Task RemoveAsync(long placeId) => throw new InvalidOperationException();
        public Task SetDefaultAsync(long placeId) => throw new InvalidOperationException();
        public Task ClearDefaultAsync() => throw new InvalidOperationException();
        public Task UpdateLocalNameAsync(long placeId, string? localName) => throw new InvalidOperationException();
    }

    [Fact]
    public async Task LoadAsync_SurvivesAStoreThatThrowsAnUnexpectedType_LogsIt_AndWritesNothing()
    {
        var (store, recorder) = FailingListStore(new NullReferenceException("simulated unplanned failure"));
        var log = new CapturingLog();
        var (vm, _, _, path) = MainViewModelTests.Build(wrapStore: _ => store, log: log);

        var ex = await Record.ExceptionAsync(() => vm.LoadAsync());

        Assert.Null(ex);
        Assert.Empty(vm.Accounts);
        Assert.True(vm.AccountsLoaded, "a plugin waiting on the roster must be released, not hung");
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Error && e.Ex is NullReferenceException);
        // The only thing the view model asked the store for was a read: no write can follow from here.
        Assert.NotEmpty(recorder.Calls);
        Assert.All(recorder.Calls, c => Assert.Equal(nameof(IAccountStore.ListAsync), c));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task RunStartupLoadAsync_NeverThrows_WhenTheStoreFailsUnexpectedly()
    {
        var (store, _) = FailingListStore(new InvalidCastException("simulated"));
        var (vm, _, _, _) = MainViewModelTests.Build(wrapStore: _ => store);

        var placement = default(WindowPlacement?);
        var ex = await Record.ExceptionAsync(async () => placement = await vm.RunStartupLoadAsync());

        Assert.Null(ex);
        Assert.Null(placement);
    }

    [Fact]
    public async Task LoadAsync_SurvivesAGamesStoreThatThrows_AndStillLoadsTheAccounts()
    {
        var (vm, store, _, path) = MainViewModelTests.Build(favorites: new ThrowingFavorites());
        try
        {
            await store.AddAsync("Alt", "", "FAKE-COOKIE-alt");

            var ex = await Record.ExceptionAsync(() => vm.LoadAsync());

            Assert.Null(ex);
            Assert.Single(vm.Accounts);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"version":1}""")]
    [InlineData("null")]
    public async Task WrongShapeVault_WithNoBackup_TakesTheCorruptPath_AndLeavesTheFileAlone(string shape)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rororo-mvm-shape-{Guid.NewGuid():N}.dat");
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(shape), optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, bytes);
        var (vm, _, _, _) = MainViewModelTests.Build(accountStorePath: path);
        try
        {
            var prompted = 0;
            var quit = false;
            vm.DpapiCorruptPrompt = () => { prompted++; return false; };
            vm.QuitApplication = () => quit = true;

            var ex = await Record.ExceptionAsync(() => vm.RunStartupLoadAsync());

            Assert.Null(ex);
            Assert.Equal(1, prompted); // the existing corrupt-vault dialog, not a crash and not an empty list
            Assert.True(quit);
            Assert.Empty(vm.Accounts);
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.False(File.Exists(path + ".bak"));
        }
        finally
        {
            foreach (var f in new[] { path, path + ".bak", path + ".tmp" }) { if (File.Exists(f)) File.Delete(f); }
        }
    }

    [Fact]
    public async Task WrongShapeVault_WithAGoodBackup_LoadsTheBackupsAccounts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rororo-mvm-shape-{Guid.NewGuid():N}.dat");
        var (vm, store, _, _) = MainViewModelTests.Build(accountStorePath: path);
        try
        {
            var a = await store.AddAsync("UserA", "", "FAKE-COOKIE-a");
            await store.AddAsync("UserB", "", "FAKE-COOKIE-b");
            File.WriteAllBytes(path, ProtectedData.Protect(Encoding.UTF8.GetBytes("""{"version":1}"""), optionalEntropy: null, DataProtectionScope.CurrentUser));
            vm.DpapiCorruptPrompt = () => throw new Xunit.Sdk.XunitException("a good backup must not raise the corrupt dialog");

            await vm.RunStartupLoadAsync();

            Assert.Equal(a.Id, Assert.Single(vm.Accounts).Id);
        }
        finally
        {
            foreach (var f in new[] { path, path + ".bak", path + ".tmp" }) { if (File.Exists(f)) File.Delete(f); }
        }
    }

    // The corrupt dialog also appears over a vault that was only locked for a moment. Start fresh then
    // finds the store readable and sets nothing aside, so the window must show the accounts, not an
    // empty list under "Started fresh".
    [Fact]
    public async Task AfterStartFresh_OverAStoreThatReadsCorrectly_TheAccountsAreShown_NotAnEmptyList()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rororo-mvm-fresh-{Guid.NewGuid():N}.dat");
        var (vm, store, _, _) = MainViewModelTests.Build(accountStorePath: path);
        try
        {
            var a = await store.AddAsync("UserA", "", "FAKE-COOKIE-a");
            vm.DpapiCorruptPrompt = () => true; // the user clicked Start fresh; the store set nothing aside

            vm.ShowDpapiCorruptModal();
            await vm.StartFreshReload; // the one re-read the handler started; a second one races it

            Assert.Equal(a.Id, Assert.Single(vm.Accounts).Id);
            Assert.Equal(string.Empty, vm.StatusBanner);
        }
        finally
        {
            foreach (var f in new[] { path, path + ".bak", path + ".tmp" }) { if (File.Exists(f)) File.Delete(f); }
        }
    }

    [Fact]
    public async Task AfterStartFresh_OverAnEmptyStore_TheListStaysEmpty_AndSaysSo()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rororo-mvm-fresh-{Guid.NewGuid():N}.dat");
        var (vm, _, _, _) = MainViewModelTests.Build(accountStorePath: path);
        try
        {
            vm.DpapiCorruptPrompt = () => true;

            vm.ShowDpapiCorruptModal();
            await vm.StartFreshReload; // the one re-read the handler started; a second one races it

            Assert.Empty(vm.Accounts);
            Assert.NotEqual(string.Empty, vm.StatusBanner);
        }
        finally
        {
            foreach (var f in new[] { path, path + ".bak", path + ".tmp" }) { if (File.Exists(f)) File.Delete(f); }
        }
    }
}
