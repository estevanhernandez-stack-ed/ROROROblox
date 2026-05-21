using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace ROROROblox.App.Diagnostics;

/// <summary>
/// Stamps the dispatched account's identity into Roblox's
/// <c>appStorage.json</c> immediately, then defends that value against
/// late writes from sibling RobloxPlayerBeta processes until the launched
/// client actually consumes it (or a generous max cap expires).
/// Roblox brands its captcha gate from the <c>Username</c>/<c>DisplayName</c>
/// fields in that file, but each spawned RPB writes its own identity back
/// to it ~3-5s after attach. Without this defender, a captcha rendered
/// during launch N can read identity from launch N+1's RPB write.
///
/// <para>v1.6.0 (item 9) — install-resilient lifetime. Previously the defender
/// ran for a FIXED 12s window. A Roblox install/update box popping mid-launch
/// delays the real RPB from reading <c>appStorage.json</c> until well past 12s,
/// so the defense expired before the identity was consumed → a sibling identity
/// won → wrong account + captcha. Now the defender holds up to a generous
/// <paramref name="maxCap"/> (default ~120s, the upper bound, not the normal
/// lifetime) and winds down a short <c>postAttachGrace</c> after
/// <see cref="NotifyConsumed"/> is called (i.e. once the client process attaches
/// and can read the identity). Normal path: attach in ~1-2s → defends ~12s total
/// (attach + grace), measured from attach rather than from launch.</para>
///
/// Disposing cancels the watch loop early.
/// </summary>
internal sealed class AppStorageDefender : IAsyncDisposable
{
    private static readonly string DefaultAppStoragePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "LocalStorage", "appStorage.json");

    private static readonly long SelfWriteSuppressionTicks =
        Stopwatch.Frequency * 250 / 1000;

    // Only one defender actively watches at a time — the most recent launch.
    // When a new defender is constructed, it takes over and cancels the
    // previous one's watch loop. This prevents two defenders for different
    // accounts from ping-ponging re-stamps against each other (each RPB's
    // captcha read happens within ~5s of attach, well before the next
    // launch in the LaunchAll gap, so the previous defender's job is done
    // by the time the next launch dispatches).
    //
    // KNOWN LIMITATION (out of scope for item 9): multilaunch DURING an install.
    // If a second account launches while the first's RPB is still blocked behind
    // a Roblox install, the takeover here cancels the first defender before its
    // identity is consumed. The full fix is the future Bloxstrap install-deferral
    // cycle (don't dispatch the next launch until the install completes), not a
    // lifetime tweak in this class.
    private static AppStorageDefender? _active;

    private readonly string _appStoragePath;
    private readonly string _username;
    private readonly string _displayName;
    private readonly long _userId;
    private readonly ILogger _log;
    private readonly TimeSpan _maxCap;
    private readonly TimeSpan _postAttachGrace;
    private readonly CancellationTokenSource _cts;
    // Signalled by NotifyConsumed — collapses the remaining wait from the
    // (long) max cap down to the short post-attach grace.
    private readonly TaskCompletionSource _consumed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly FileSystemWatcher? _fsw;
    private readonly SemaphoreSlim _restampLock = new(1, 1);
    private readonly Task _completion;
    private long _lastSelfWriteTicks;
    private int _restampCount;
    private int _consumedSignalled;
    private bool _disposed;

    public AppStorageDefender(string username, string displayName, long userId,
        ILogger log, TimeSpan maxCap, TimeSpan postAttachGrace,
        string? appStoragePath = null, CancellationToken externalCt = default)
    {
        _username = username;
        _displayName = displayName;
        _userId = userId;
        _log = log;
        _maxCap = maxCap;
        _postAttachGrace = postAttachGrace;
        _appStoragePath = appStoragePath ?? DefaultAppStoragePath;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);

        // Take over as the sole active defender; signal the previous one to
        // stop watching. Its DisposeAsync (driven by MainViewModel's
        // Completion ContinueWith) will tear down its FSW.
        var previous = Interlocked.Exchange(ref _active, this);
        try { previous?._cts.Cancel(); } catch { }

        TryStamp(initial: true);

        var dir = Path.GetDirectoryName(_appStoragePath);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            try
            {
                _fsw = new FileSystemWatcher(dir, Path.GetFileName(_appStoragePath))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
                                 | NotifyFilters.FileName | NotifyFilters.CreationTime,
                    InternalBufferSize = 65536,
                    EnableRaisingEvents = true,
                };
                _fsw.Changed += OnFileEvent;
                _fsw.Created += OnFileEvent;
                _fsw.Renamed += OnFileRenamed;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex,
                    "AppStorageDefender: could not start FileSystemWatcher; degrading to one-shot stamp");
            }
        }

        _completion = RunWindowAsync();
    }

    public Task Completion => _completion;
    public int RestampCount => _restampCount;

    /// <summary>
    /// Signals that the launched client has attached and can now read the stamped
    /// identity (for captcha branding). The defender keeps re-stamping for a short
    /// post-attach grace, then winds down. Before this is called the defender
    /// defends up to the max cap, so an install delay that postpones the RPB's
    /// first read past the cap is the only thing that can expire the defense early.
    /// Idempotent — only the first call collapses the remaining wait.
    /// </summary>
    public void NotifyConsumed()
    {
        if (Interlocked.Exchange(ref _consumedSignalled, 1) != 0) return;
        _ = ScheduleGraceWinddownAsync();
    }

    private async Task ScheduleGraceWinddownAsync()
    {
        try { await Task.Delay(_postAttachGrace, _cts.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _consumed.TrySetResult();
    }

    private async Task RunWindowAsync()
    {
        // Hold until EITHER the max cap elapses (install-delay upper bound) OR the
        // post-attach grace fires after NotifyConsumed (normal wind-down).
        var cap = Task.Delay(_maxCap, _cts.Token);
        var winner = await Task.WhenAny(cap, _consumed.Task).ConfigureAwait(false);
        // Observe the cap task's cancellation if it lost the race, to avoid an
        // unobserved-exception warning when DisposeAsync cancels.
        if (!ReferenceEquals(winner, cap))
        {
            _ = cap.ContinueWith(static t => { _ = t.Exception; },
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }
        else
        {
            try { await cap.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e) => CheckAndRestamp();
    private void OnFileRenamed(object sender, RenamedEventArgs e) => CheckAndRestamp();

    private void CheckAndRestamp()
    {
        // If a newer defender has taken over, this defender is past its useful
        // window — don't re-stamp into the new defender's identity.
        if (!ReferenceEquals(Volatile.Read(ref _active), this)) return;

        var since = Stopwatch.GetTimestamp() - Volatile.Read(ref _lastSelfWriteTicks);
        if (since < SelfWriteSuppressionTicks) return;

        _ = Task.Run(async () =>
        {
            await _restampLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_cts.IsCancellationRequested) return;
                if (!File.Exists(_appStoragePath)) return;

                var current = await ReadCurrentUsernameAsync().ConfigureAwait(false);
                if (current is null) return;
                if (string.Equals(current, _username, StringComparison.Ordinal)) return;

                TryStamp(initial: false, driftFrom: current);
            }
            finally { _restampLock.Release(); }
        });
    }

    private async Task<string?> ReadCurrentUsernameAsync()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var fs = new FileStream(_appStoragePath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs);
                var raw = await sr.ReadToEndAsync().ConfigureAwait(false);
                var node = JsonNode.Parse(raw);
                return node?["Username"]?.ToString();
            }
            catch (IOException) { await Task.Delay(50).ConfigureAwait(false); }
            catch (JsonException) { await Task.Delay(50).ConfigureAwait(false); }
        }
        return null;
    }

    private void TryStamp(bool initial, string? driftFrom = null)
    {
        try
        {
            if (!File.Exists(_appStoragePath))
            {
                if (initial) _log.LogDebug("appStorage.json not present; skipping identity stamp");
                return;
            }

            var raw = File.ReadAllText(_appStoragePath);
            var node = JsonNode.Parse(raw);
            if (node is null) return;

            var prev = node["Username"]?.ToString() ?? "<null>";
            node["Username"] = _username;
            node["DisplayName"] = _displayName;
            node["UserId"] = _userId.ToString();

            var tmp = _appStoragePath + ".tmp";
            File.WriteAllText(tmp, node.ToJsonString());
            Volatile.Write(ref _lastSelfWriteTicks, Stopwatch.GetTimestamp());
            File.Move(tmp, _appStoragePath, overwrite: true);

            if (initial)
            {
                _log.LogInformation(
                    "appStorage.json identity stamped: {Name} (was {Prev}, userId {UserId})",
                    _username, prev, _userId);
            }
            else
            {
                Interlocked.Increment(ref _restampCount);
                _log.LogInformation(
                    "appStorage.json defender re-stamped: {Name} (drift from {Drift}, count={Count})",
                    _username, driftFrom ?? prev, _restampCount);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AppStorageDefender: stamp failed for {Username}", _username);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Clear the active slot only if we're still it (a newer defender may
        // have already taken over).
        Interlocked.CompareExchange(ref _active, null, this);

        try { _cts.Cancel(); } catch { }
        // Unblock RunWindowAsync if it's still waiting on the consume signal.
        _consumed.TrySetResult();
        if (_fsw is not null)
        {
            _fsw.EnableRaisingEvents = false;
            _fsw.Changed -= OnFileEvent;
            _fsw.Created -= OnFileEvent;
            _fsw.Renamed -= OnFileRenamed;
            _fsw.Dispose();
        }
        try { await _completion.ConfigureAwait(false); } catch { }

        if (_restampCount > 0)
        {
            _log.LogInformation(
                "AppStorageDefender for {Username} disposed after {Restamps} re-stamp(s)",
                _username, _restampCount);
        }

        _cts.Dispose();
        _restampLock.Dispose();
    }
}
