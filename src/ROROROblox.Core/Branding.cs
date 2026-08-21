namespace ROROROblox.Core;

/// <summary>
/// The name the product is allowed to call itself in front of a user.
/// <para>
/// WHY A CONSTANT FOR ONE WORD. <c>ROROROblox</c> is the repo, the assembly, the namespace, the
/// <c>%LOCALAPPDATA%</c> folder, the single-instance guard, the drag format and the User-Agent —
/// all correct, none of them user-facing. The brand is <b>RoRoRo</b>. Because the identifier is
/// everywhere, the wrong one has been easy to reach for and has shipped four separate times: a
/// runtime-assembled window title (fixed in wave 4), the tray menu entry, the tray tooltip in all
/// three states, and the support-bundle header — plus the idle toast, which nobody had counted
/// until F-034 was re-measured on 2026-08-20.
/// </para>
/// <para>
/// Nothing here changes what an identifier is called. This is only for the strings a person reads,
/// and <c>BrandNameFenceTests</c> is what keeps the literal from creeping back into them.
/// </para>
/// </summary>
public static class Branding
{
    /// <summary>The user-facing product name. Never the repo name.</summary>
    public const string ProductName = "RoRoRo";
}
