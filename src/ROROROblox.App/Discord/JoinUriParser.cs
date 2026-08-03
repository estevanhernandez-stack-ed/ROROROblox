using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.App.Discord;

/// <summary>
/// Pulls a join target out of process arguments. Discord launches us with
/// <c>roblox-rororo://join/&lt;url-encoded secret&gt;</c> when a clan member clicks Join.
/// </summary>
public static class JoinUriParser
{
    private const string Prefix = "roblox-rororo://join/";

    public static bool TryParse(string[] args, out LaunchTarget target)
    {
        target = new LaunchTarget.Home();
        if (args is null || args.Length == 0) return false;

        foreach (var arg in args)
        {
            if (arg is null || !arg.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var secret = Uri.UnescapeDataString(arg[Prefix.Length..].TrimEnd('/'));
            return JoinSecretCodec.TryDecode(secret, out target);
        }
        return false;
    }
}
