using System.Text.RegularExpressions;
using ROROROblox.Core.StreamerMode;

namespace ROROROblox.App.Tray;

/// <summary>
/// Single source of truth for the window-title text RORORO stamps on each foreign Roblox player
/// window, plus the inverse parse the startup scanner uses to re-attach after an app restart.
/// <para>
/// <see cref="RobloxWindowDecorator"/> WRITES the title; <see cref="RunningRobloxScanner"/> READS
/// it back to re-attach (there is no pid persistence — title-parsing is the whole re-attach
/// mechanism). The two MUST agree on the exact name or restart re-attach silently breaks. That is
/// precisely what regressed when the decorator switched from the raw <c>DisplayName</c> to the
/// streamer-mode / LocalName-aware name (v1.10) while the scanner still matched on
/// <c>DisplayName</c>: nickname users lost re-attach on every restart, and streamer-mode users
/// lost it whenever the mode was active across a restart. Both sides now resolve the name through
/// <see cref="ResolveName"/> so they cannot drift again.
/// </para>
/// </summary>
internal static class RobloxWindowTitle
{
    // "Roblox - {name}". Anchored to ^…$ so a stray "Roblox - X" mid-title doesn't false-match;
    // trailing \s* absorbs any whitespace Roblox pads on. Group 1 captures the name.
    public static readonly Regex Pattern = new(@"^Roblox\s*-\s*(.+?)\s*$", RegexOptions.Compiled);

    /// <summary>
    /// The name shown for this account in its window title: the streamer-mode fake name when the
    /// provider is active, else the real render name. Callers pass the REAL render name
    /// (<c>LocalName ?? DisplayName</c>) as <paramref name="renderName"/>; the provider substitutes
    /// the fake only while active. A <see langword="null"/> provider yields
    /// <paramref name="renderName"/> verbatim.
    /// </summary>
    public static string ResolveName(IStreamerIdentityProvider? identity, Guid accountId, string renderName, string avatarUrl)
        => identity is not null ? identity.ForAccount(accountId, renderName, avatarUrl).Name : renderName;

    /// <summary>Full window-title text (<c>"Roblox - {name}"</c>) for this account.</summary>
    public static string Format(IStreamerIdentityProvider? identity, Guid accountId, string renderName, string avatarUrl)
        => $"Roblox - {ResolveName(identity, accountId, renderName, avatarUrl)}";
}
