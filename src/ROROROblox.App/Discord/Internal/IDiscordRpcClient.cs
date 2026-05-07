namespace ROROROblox.App.Discord.Internal;

/// <summary>
/// Test seam over <c>DiscordRPC.DiscordRpcClient</c> (Lachee). The library is fine but the
/// production type is concrete + IPC-bound + hard to fake. The service consumes this interface
/// so DiscordRichPresenceServiceTests can drive state transitions, JoinRequested, and
/// ConnectionFailed in-memory without touching the local Discord pipe.
/// </summary>
internal interface IDiscordRpcClient : IDisposable
{
    bool IsInitialized { get; }
    void Initialize();
    void Deinitialize();
    void SetPresence(DiscordPresencePayload payload);
    void ClearPresence();

    /// <summary>Raised when Discord forwards a Join button click. Payload = the join secret string.</summary>
    event EventHandler<string>? JoinRequested;

    /// <summary>Raised when the IPC pipe drops or the initial connect fails.</summary>
    event EventHandler? ConnectionFailed;

    /// <summary>Raised on a successful (re)connect.</summary>
    event EventHandler? Ready;
}

/// <summary>
/// Internal DTO so the wrapper interface stays free of Lachee types. Maps to
/// <c>DiscordRPC.RichPresence</c> in <see cref="LacheeDiscordRpcClientAdapter"/>.
/// </summary>
internal sealed record DiscordPresencePayload(
    string? State,
    string? Details,
    string? LargeImageKey,
    string? LargeImageText,
    string? SmallImageKey,
    string? SmallImageText,
    DiscordPresenceParty? Party);

internal sealed record DiscordPresenceParty(
    string PartyId,
    string JoinSecret,
    int MaxSize);
