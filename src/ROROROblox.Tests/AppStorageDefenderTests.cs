using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ROROROblox.App.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// Tests for the install-resilient appStorage identity defender (v1.6.0 item 9).
///
/// IDENTITY-SENSITIVE: a bug here launches the WRONG Roblox account. The defender's
/// contract is "defend the launching account's identity in appStorage.json until the
/// real client consumes it (NotifyConsumed) or a generous max cap expires" — NOT a
/// fixed 12s window that a Roblox install can outlast.
///
/// FSW timing can be flaky on CI. The drift-restamp test gives the watcher a generous
/// poll budget; a few asserts drive the re-stamp path directly via a fresh write rather
/// than racing the OS event so the suite stays deterministic.
/// </summary>
/// <summary>
/// The three FileSystemWatcher-backed tests below cannot be made deterministic by a clock — no
/// abstraction makes an OS file event predictable — so they are given a quiet pool instead, the
/// same treatment <c>FpsCapSettlerTests</c> got. Waiting on a real watcher while ~2,000 other
/// tests contend for pool threads is the condition that turns a generous budget into a flake.
/// <see cref="AppStorageDefenderTests.TheWatcherTestsKeepTheirQuietPool"/> fails the build if the
/// attribute pair is ever tidied away.
/// </summary>
[CollectionDefinition(AppStorageDefenderQuietPoolCollection.Name, DisableParallelization = true)]
public sealed class AppStorageDefenderQuietPoolCollection
{
    public const string Name = "AppStorageDefender quiet pool";
}

[Collection(AppStorageDefenderQuietPoolCollection.Name)]
public sealed class AppStorageDefenderTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public AppStorageDefenderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "rororo-appstorage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "appStorage.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private void WriteAppStorage(string username, string displayName = "Display", string userId = "1")
    {
        var node = new JsonObject
        {
            ["Username"] = username,
            ["DisplayName"] = displayName,
            ["UserId"] = userId,
            ["SomeOtherField"] = "keepme",
        };
        File.WriteAllText(_path, node.ToJsonString());
    }

    private string? ReadUsername()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs);
                var raw = sr.ReadToEnd();
                return JsonNode.Parse(raw)?["Username"]?.ToString();
            }
            catch (IOException) { Thread.Sleep(20); }
            catch (JsonException) { Thread.Sleep(20); }
        }
        return null;
    }

    private AppStorageDefender NewDefender(
        string username = "LaunchedAccount",
        TimeSpan? maxCap = null,
        TimeSpan? postAttachGrace = null,
        TimeProvider? time = null)
        => new(
            username,
            displayName: username,
            userId: 42,
            log: NullLogger.Instance,
            maxCap: maxCap ?? TimeSpan.FromSeconds(2),
            postAttachGrace: postAttachGrace ?? TimeSpan.FromMilliseconds(200),
            appStoragePath: _path,
            time: time);

    /// <summary>
    /// Real-time ceiling for "this should have completed by now" waits. It paces nothing — the
    /// fake clock drives every deadline — so load can only make it more generous, never fail it.
    /// </summary>
    private static readonly TimeSpan CompletionBound = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gives an already-advanced fake timer's continuation a chance to run before a NEGATIVE
    /// assertion reads it. Safe in a way the old real delays were not: with the clock faked,
    /// no amount of real time can complete the defender, so a stalled runner cannot turn
    /// "still defending" into a failure. It can only make this settle take longer.
    /// </summary>
    private static async Task SettleAsync()
    {
        for (var i = 0; i < 50; i++) await Task.Yield();
    }

    /// <summary>
    /// The fence on the quiet pool: this class must stay in a parallel-disabled collection.
    /// </summary>
    [Fact]
    public void TheWatcherTestsKeepTheirQuietPool()
    {
        var collection = (CollectionAttribute?)Attribute.GetCustomAttribute(
            typeof(AppStorageDefenderTests), typeof(CollectionAttribute));
        Assert.NotNull(collection);

        var definition = (CollectionDefinitionAttribute?)Attribute.GetCustomAttribute(
            typeof(AppStorageDefenderQuietPoolCollection), typeof(CollectionDefinitionAttribute));
        Assert.NotNull(definition);
        Assert.True(definition.DisableParallelization,
            "The AppStorageDefender collection no longer disables parallelization — the "
            + "FileSystemWatcher tests are back on a contended pool.");
    }

    [Fact]
    public async Task InitialStamp_WritesLaunchedIdentityIntoFile()
    {
        WriteAppStorage("PreviousIdentity");

        await using var defender = NewDefender("LaunchedAccount");

        Assert.Equal("LaunchedAccount", ReadUsername());
        // Non-identity fields are preserved.
        var node = JsonNode.Parse(File.ReadAllText(_path));
        Assert.Equal("keepme", node?["SomeOtherField"]?.ToString());
    }

    [Fact]
    public async Task Drift_RestampsBackToLaunchedIdentity()
    {
        WriteAppStorage("LaunchedAccount");

        await using var defender = NewDefender("LaunchedAccount", maxCap: TimeSpan.FromSeconds(8));

        // POKE UNTIL IT ANSWERS, rather than sleeping a guess at how long it needs (F-116).
        //
        // This was `await Task.Delay(600)` followed by one drift write and a single five-second
        // wait, and it failed twice in roughly eight full-suite runs while passing every time in
        // isolation. The 600ms was doing two jobs at once: clearing the defender's 250ms
        // self-write-suppression window, and "giving the FSW a beat to be fully armed". The first is
        // a real interval; the second is a guess about thread scheduling, and on a loaded machine
        // 600ms of wall clock can pass with the watcher not yet armed. The single drift write then
        // lands in a window nobody is listening to, and no amount of waiting afterwards recovers it
        // — the event it was waiting for already happened and was missed.
        //
        // Re-poking is what makes that harmless: a drift suppressed or unobserved is simply written
        // again, and the first one the defender actually sees produces the re-stamp. The budget
        // below bounds the FAILURE path only; on a healthy machine the first or second poke answers
        // immediately. Not a longer timeout — a different question.
        var restamped = false;
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        while (!restamped && DateTimeOffset.UtcNow < deadline)
        {
            WriteAppStorage("SiblingAccount");
            restamped = await WaitForUsernameAsync("LaunchedAccount", TimeSpan.FromMilliseconds(750));
        }
        Assert.True(restamped, $"Expected re-stamp back to LaunchedAccount but file held: {ReadUsername()}");
        Assert.True(defender.RestampCount >= 1, "Expected at least one re-stamp to be recorded.");
    }

    [Fact]
    public async Task WithoutNotifyConsumed_StaysActiveUntilMaxCap()
    {
        // F-116, fixed at the root 2026-09-09: this used to race a real 800ms cap with a real
        // Stopwatch, and a stalled continuation resuming past the cap reported correct behaviour
        // as a failure. Both timing assertions were guarded with "only assert if we actually
        // landed inside the window", which is honest but means the claim went UNMEASURED on
        // exactly the loaded runs where it mattered. With the clock injected the windows are
        // exact and always measured.
        WriteAppStorage("LaunchedAccount");

        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var cap = TimeSpan.FromSeconds(8);
        await using var defender = NewDefender(
            "LaunchedAccount", maxCap: cap, postAttachGrace: TimeSpan.FromSeconds(1), time: clock);

        Assert.False(defender.Completion.IsCompleted,
            "Defender completed before the max cap with no consume signal.");

        // Deep inside the cap window. Real time cannot reach this assertion now — only Advance
        // can complete the defender — so the probe is no longer conditional on having landed in
        // time. It is always measured.
        clock.Advance(TimeSpan.FromSeconds(3));
        await SettleAsync();
        Assert.False(defender.Completion.IsCompleted,
            "Defender wound down well before the max cap with no consume signal.");

        // One tick short of the cap: still defending.
        clock.Advance(TimeSpan.FromSeconds(5) - TimeSpan.FromMilliseconds(1));
        await SettleAsync();
        Assert.False(defender.Completion.IsCompleted, "Defender completed before the cap elapsed.");

        // And across it.
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await defender.Completion.WaitAsync(CompletionBound);
        Assert.True(defender.Completion.IsCompleted, "Defender never completed at the max cap.");
    }

    [Fact]
    public async Task NotifyConsumed_CompletesAfterGrace_NotBefore_NotAtCap()
    {
        // The test that failed PR #204's arm64 job. It asserted Completion was not done in the
        // statement immediately after NotifyConsumed(), against a real 400ms grace — so a thread
        // descheduled for longer than that saw correct code as a broken grace.
        WriteAppStorage("LaunchedAccount");

        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var cap = TimeSpan.FromSeconds(10);
        var grace = TimeSpan.FromSeconds(2);
        await using var defender = NewDefender(
            "LaunchedAccount", maxCap: cap, postAttachGrace: grace, time: clock);

        defender.NotifyConsumed();

        // Must NOT complete immediately — the grace keeps it defending so the live client reads
        // the identity for captcha branding.
        await SettleAsync();
        Assert.False(defender.Completion.IsCompleted,
            "Defender completed instantly on NotifyConsumed — grace not honored.");

        // One tick short of the grace.
        clock.Advance(grace - TimeSpan.FromMilliseconds(1));
        await SettleAsync();
        Assert.False(defender.Completion.IsCompleted, "Defender completed before the grace elapsed.");

        // Across the grace, and nowhere near the 10s cap — the distinction this test exists for.
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await defender.Completion.WaitAsync(CompletionBound);
        Assert.True(defender.Completion.IsCompleted, "Defender never completed after the grace.");
        Assert.True(clock.GetUtcNow() - DateTimeOffset.UnixEpoch < cap,
            "Completed at the cap instead of after the short grace.");
    }

    [Fact]
    public async Task AfterCompletion_DriftIsNotRestamped()
    {
        WriteAppStorage("LaunchedAccount");

        var defender = NewDefender("LaunchedAccount", maxCap: TimeSpan.FromMilliseconds(400),
            postAttachGrace: TimeSpan.FromMilliseconds(100));

        // Let the cap fire and fully dispose.
        //
        // 15s to wait out a 400ms cap looks absurd and is deliberate. This bound is pure liveness —
        // it exists so a wedged defender fails the test instead of hanging it, and nothing below
        // asserts anything about elapsed time, unlike the two tests above this one. So the only
        // thing a tight bound can do here is fail on a slow machine, which is what it did:
        // TimeoutException on the x64 CI runner while the arm64 runner passed the same test in the
        // same run. Widened 2026-08-13 rather than re-run until green.
        await defender.Completion.WaitAsync(TimeSpan.FromSeconds(15));
        await defender.DisposeAsync();

        // A drift after disposal must NOT be corrected — the defender is gone.
        WriteAppStorage("SiblingAccount");
        await Task.Delay(500); // generous: if any latent FSW handler fired, it'd have run by now.

        Assert.Equal("SiblingAccount", ReadUsername());
    }

    private async Task<bool> WaitForUsernameAsync(string expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (string.Equals(ReadUsername(), expected, StringComparison.Ordinal)) return true;
            await Task.Delay(50);
        }
        return string.Equals(ReadUsername(), expected, StringComparison.Ordinal);
    }
}
