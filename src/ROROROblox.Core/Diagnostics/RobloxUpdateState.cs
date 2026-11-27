namespace ROROROblox.Core.Diagnostics;

public enum RobloxUpdateKind { Unknown, Current, Pending }

public enum RobloxUpdateDirection { None, Newer, Older }

/// <summary>
/// What the client-version check concluded. <c>Unknown</c> is "could not tell" (Roblox not installed,
/// nothing readable, the endpoint blank) and never reads as a difference. <c>Latest</c> is the endpoint's
/// answer when it was readable, so a caller can re-compare against local reads without a request.
/// </summary>
public sealed record RobloxUpdateState(
    RobloxUpdateKind Kind,
    RobloxUpdateDirection Direction = RobloxUpdateDirection.None,
    Version? Latest = null)
{
    public static RobloxUpdateState Unknown { get; } = new(RobloxUpdateKind.Unknown);
}
