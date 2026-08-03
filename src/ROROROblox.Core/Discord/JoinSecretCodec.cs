using System.Globalization;

namespace ROROROblox.Core.Discord;

/// <summary>
/// Encodes a launch target into a Discord join secret and back.
/// <para>
/// Compact by necessity: Lachee's client silently refuses a <c>SetPresence</c> whose secret
/// exceeds 128 characters, which presents as "presence works but Join never appears." Hence
/// pipe-delimited fields rather than JSON.
/// </para>
/// Only targets that name ONE server are encodable. <see cref="LaunchTarget.Place"/> means "this
/// game, any server with room" — joining that is not joining the host.
/// </summary>
public static class JoinSecretCodec
{
    public const int MaxLength = 128;

    public static string? Encode(LaunchTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target switch
        {
            LaunchTarget.GameJob job => $"g|{job.PlaceId}|{job.JobId}",
            LaunchTarget.PrivateServer ps =>
                $"p|{ps.PlaceId}|{(ps.Kind == PrivateServerCodeKind.LinkCode ? "l" : "a")}|{ps.Code}",
            _ => null,
        };
    }

    public static bool TryDecode(string? secret, out LaunchTarget target)
    {
        target = new LaunchTarget.Home();
        if (string.IsNullOrWhiteSpace(secret)) return false;

        var parts = secret.Split('|');
        if (parts.Length < 3) return false;
        if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var placeId) || placeId <= 0)
        {
            return false;
        }

        switch (parts[0])
        {
            case "g" when !string.IsNullOrWhiteSpace(parts[2]):
                target = new LaunchTarget.GameJob(placeId, parts[2]);
                return true;
            case "p" when parts.Length == 4 && !string.IsNullOrWhiteSpace(parts[3]):
                target = new LaunchTarget.PrivateServer(
                    placeId, parts[3],
                    parts[2] == "l" ? PrivateServerCodeKind.LinkCode : PrivateServerCodeKind.AccessCode);
                return true;
            default:
                return false;
        }
    }
}
