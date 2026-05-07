namespace ROROROblox.Core.Discord;

public sealed record RichPresenceState(
    PresenceMode Mode,
    int ActiveAccountCount,
    string? CurrentActivity);

public enum PresenceMode
{
    Idle,
    AccountsActive,
    InPrivateServer,
}

public sealed class JoinRequestedEventArgs(string serverShareUrl) : EventArgs
{
    public string ServerShareUrl { get; } = serverShareUrl;
}
