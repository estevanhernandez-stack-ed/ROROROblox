namespace ROROROblox.App.Discord.Internal;

/// <summary>
/// Test seam over Lachee's concrete <c>DiscordRpcClient</c>, which is IPC-bound and unfakeable.
/// The presence service consumes this interface so its tests can drive connect, drop, reconnect,
/// and Join without touching the local Discord pipe.
/// </summary>
internal interface IDiscordRpcClient : IDisposable
{
    bool IsInitialized { get; }
    void Initialize();
    void Deinitialize();
    void SetPresence(DiscordPresencePayload payload);
    void ClearPresence();

    /// <summary>Discord forwarded a Join click. Payload is the join secret.</summary>
    event EventHandler<string>? JoinRequested;

    /// <summary>The IPC pipe dropped, or the initial connect failed.</summary>
    event EventHandler? ConnectionFailed;

    /// <summary>A successful (re)connect.</summary>
    event EventHandler? Ready;

    /// <summary>Discord rejected something — bad payload, missing asset key, rate limit.</summary>
    event EventHandler<string>? Errored;
}

/// <summary>DTO so the seam stays free of Lachee types.</summary>
internal sealed record DiscordPresencePayload(
    string? State,
    string? Details,
    DateTimeOffset? StartedAtUtc,
    string? LargeImageKey,
    string? LargeImageText,
    DiscordPresenceParty? Party);

internal sealed record DiscordPresenceParty(string PartyId, string JoinSecret, int Size, int MaxSize);
