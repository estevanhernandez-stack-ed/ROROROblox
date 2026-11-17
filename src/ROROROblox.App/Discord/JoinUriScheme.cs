using Microsoft.Win32;
using ROROROblox.Core;

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

    private const string DefaultClassesPath = @"Software\Classes";

    public static void Register(string exePath) => Register(exePath, DefaultClassesPath);

    /// <summary>
    /// Seam for tests: the classes root is injectable so the suite can exercise the real write
    /// against a scratch key. Skipped under a data-folder override
    /// (<see cref="AppDataRoot.MachineWideRegistrationsAllowed"/>): a run on a test folder must
    /// not repoint the owner's real <c>roblox-rororo:</c> handler at the test build.
    /// </summary>
    internal static void Register(string exePath, string classesPath)
    {
        if (!AppDataRoot.MachineWideRegistrationsAllowed) return;
        using var key = Registry.CurrentUser.CreateSubKey($@"{classesPath}\{SchemeName}");
        key.SetValue("", $"URL:{SchemeName}");
        key.SetValue("URL Protocol", "");
        using var command = key.CreateSubKey(@"shell\open\command");
        command.SetValue("", BuildCommandValue(exePath));
    }

    /// <summary>
    /// Fixes up the registry command written by Lachee's <c>DiscordRpcClient.RegisterUriScheme()</c>
    /// for its own <c>discord-{applicationId}</c> scheme (distinct from <see cref="SchemeName"/>
    /// above, which is ours). Lachee writes that command WITHOUT the <c>"%1"</c> placeholder, so
    /// Windows launches us with no argument at all when Discord dispatches the scheme — same class
    /// of bug this file already guards against for our own scheme, just on Lachee's side of the
    /// fence. Best-effort by design: called from a swallowed try/catch, since a failure here still
    /// leaves the rest of presence working.
    /// </summary>
    public static void FixupDiscordSchemeCommand(string applicationId, string exePath) =>
        FixupDiscordSchemeCommand(applicationId, exePath, DefaultClassesPath);

    /// <summary>Test seam, and the same override guard as <see cref="Register(string, string)"/>.</summary>
    internal static void FixupDiscordSchemeCommand(string applicationId, string exePath, string classesPath)
    {
        if (!AppDataRoot.MachineWideRegistrationsAllowed) return;
        var commandKeyPath = $@"{classesPath}\discord-{applicationId}\shell\open\command";
        using var key = Registry.CurrentUser.CreateSubKey(commandKeyPath);
        key.SetValue("", BuildCommandValue(exePath));
    }

    /// <summary>
    /// Pure — no registry I/O — so it's unit-testable without touching the developer's registry.
    /// </summary>
    internal static string BuildCommandValue(string exePath) => $"\"{exePath}\" \"%1\"";
}
