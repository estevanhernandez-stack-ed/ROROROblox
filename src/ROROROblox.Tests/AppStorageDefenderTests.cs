using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
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
        TimeSpan? postAttachGrace = null)
        => new(
            username,
            displayName: username,
            userId: 42,
            log: NullLogger.Instance,
            maxCap: maxCap ?? TimeSpan.FromSeconds(2),
            postAttachGrace: postAttachGrace ?? TimeSpan.FromMilliseconds(200),
            appStoragePath: _path);

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

        // Wait past the defender's 250ms self-write-suppression window (the initial stamp
        // primed it) AND give the FSW a beat to be fully armed, then simulate a sibling RPB
        // writing a different identity (drift). The defender should re-stamp back.
        await Task.Delay(600);
        WriteAppStorage("SiblingAccount");

        // Poll for the re-stamp — FSW delivery is async + the re-stamp runs on a worker.
        var restamped = await WaitForUsernameAsync("LaunchedAccount", TimeSpan.FromSeconds(5));
        Assert.True(restamped, $"Expected re-stamp back to LaunchedAccount but file held: {ReadUsername()}");
        Assert.True(defender.RestampCount >= 1, "Expected at least one re-stamp to be recorded.");
    }

    [Fact]
    public async Task WithoutNotifyConsumed_StaysActiveUntilMaxCap()
    {
        WriteAppStorage("LaunchedAccount");

        var cap = TimeSpan.FromMilliseconds(800);
        await using var defender = NewDefender("LaunchedAccount", maxCap: cap, postAttachGrace: TimeSpan.FromMilliseconds(100));

        var sw = Stopwatch.StartNew();
        // Without NotifyConsumed, completion must NOT happen before the cap.
        Assert.False(defender.Completion.IsCompleted, "Defender completed before the max cap with no consume signal.");
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        Assert.False(defender.Completion.IsCompleted, "Defender wound down well before the max cap with no consume signal.");

        // Eventually it completes at the cap.
        await defender.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        sw.Stop();
        Assert.True(defender.Completion.IsCompleted, "Defender never completed at the max cap.");
        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(700),
            $"Completion fired too early ({sw.ElapsedMilliseconds}ms) — should have held until ~{cap.TotalMilliseconds}ms cap.");
    }

    [Fact]
    public async Task NotifyConsumed_CompletesAfterGrace_NotBefore_NotAtCap()
    {
        WriteAppStorage("LaunchedAccount");

        var cap = TimeSpan.FromSeconds(10);          // far away
        var grace = TimeSpan.FromMilliseconds(400);
        await using var defender = NewDefender("LaunchedAccount", maxCap: cap, postAttachGrace: grace);

        // Simulate attach at ~150ms in.
        await Task.Delay(150);
        var sw = Stopwatch.StartNew();
        defender.NotifyConsumed();

        // Must NOT complete immediately — the grace keeps it defending so the live client
        // reads the identity for captcha branding.
        Assert.False(defender.Completion.IsCompleted, "Defender completed instantly on NotifyConsumed — grace not honored.");

        await defender.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        sw.Stop();

        Assert.True(defender.Completion.IsCompleted, "Defender never completed after the grace.");
        // Completed roughly at grace, well short of the 10s cap.
        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(300),
            $"Completed before the grace elapsed ({sw.ElapsedMilliseconds}ms).");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"Completed near the cap ({sw.ElapsedMilliseconds}ms) instead of after the short grace.");
    }

    [Fact]
    public async Task AfterCompletion_DriftIsNotRestamped()
    {
        WriteAppStorage("LaunchedAccount");

        var defender = NewDefender("LaunchedAccount", maxCap: TimeSpan.FromMilliseconds(400),
            postAttachGrace: TimeSpan.FromMilliseconds(100));

        // Let the cap fire and fully dispose.
        await defender.Completion.WaitAsync(TimeSpan.FromSeconds(2));
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
