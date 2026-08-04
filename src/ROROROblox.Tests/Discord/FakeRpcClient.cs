using ROROROblox.App.Discord.Internal;

namespace ROROROblox.Tests.Discord;

/// <summary>
/// Stands in for Lachee's IPC-bound client so connect, drop, reconnect, and Join are driveable
/// from a unit test. Shared by every suite that exercises <c>DiscordPresenceService</c> — it was
/// duplicated per-file before, which is exactly how two copies drift into disagreeing about what
/// the seam does.
/// </summary>
internal sealed class FakeRpcClient : IDiscordRpcClient
{
    public List<DiscordPresencePayload> Presences { get; } = [];
    public int ClearCount { get; private set; }
    public bool IsInitialized { get; private set; }
    public void Initialize() => IsInitialized = true;
    public void Deinitialize() => IsInitialized = false;
    public void SetPresence(DiscordPresencePayload p) => Presences.Add(p);
    public void ClearPresence() => ClearCount++;
    public void Dispose() { }
    public event EventHandler<string>? JoinRequested;
    public event EventHandler? ConnectionFailed;
    public event EventHandler? Ready;
    public event EventHandler<string>? Errored;
    public void RaiseJoin(string secret) => JoinRequested?.Invoke(this, secret);
    public void RaiseConnectionFailed() => ConnectionFailed?.Invoke(this, EventArgs.Empty);
    public void RaiseReady() => Ready?.Invoke(this, EventArgs.Empty);
    public void RaiseErrored(string message) => Errored?.Invoke(this, message);
}
