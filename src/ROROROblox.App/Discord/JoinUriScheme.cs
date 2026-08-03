using Microsoft.Win32;

namespace ROROROblox.App.Discord;

/// <summary>
/// Registers the <c>roblox-rororo:</c> scheme under HKCU (no elevation needed).
/// <para>
/// Two things that look optional and are not: the command value must end in <c>"%1"</c> or
/// Windows launches us with no argument at all and every inbound join silently does nothing;
/// and Discord refuses to accept a presence carrying join secrets unless the scheme is
/// registered first, so this runs before the first SetPresence.
/// </para>
/// </summary>
public static class JoinUriScheme
{
    public const string SchemeName = "roblox-rororo";

    public static void Register(string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{SchemeName}");
        key.SetValue("", $"URL:{SchemeName}");
        key.SetValue("URL Protocol", "");
        using var command = key.CreateSubKey(@"shell\open\command");
        command.SetValue("", $"\"{exePath}\" \"%1\"");
    }
}
