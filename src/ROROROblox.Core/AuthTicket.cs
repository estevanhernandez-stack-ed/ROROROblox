namespace ROROROblox.Core;

/// <summary>
/// One Roblox-issued authentication ticket. Short-lived (Roblox docs: ~30s); the launcher must
/// consume it immediately. <see cref="CapturedAt"/> is informational — useful when retrying or
/// diagnosing "ticket expired between exchange and launch."
/// </summary>
public sealed record AuthTicket(string Ticket, DateTimeOffset CapturedAt);
