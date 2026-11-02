using ROROROblox.App.Localization;

namespace ROROROblox.App.ViewModels;

/// <summary>
/// The five things a saved account's row can say about whether it is fit to launch (v1.36 spec §1.1).
/// <see cref="AccountSummary.Health"/> computes it, first match wins, in this order of severity:
/// Expired, NeedsVerification, Limited, FollowMarked, Ok.
/// <para>
/// <see cref="NeedsVerification"/> is compiled in and set ONLY from a measured signal: a 403 on the
/// auth-ticket POST that carries <c>rblx-challenge-type</c>. If that signal is never found, the
/// member stays (an enum member with no setter costs nothing and keeps the ladder stable).
/// </para>
/// </summary>
internal enum AccountHealth
{
    Ok,
    FollowMarked,
    Limited,
    NeedsVerification,
    Expired,
}

/// <summary>
/// The word, glyph, action and colour key each non-Ok <see cref="AccountHealth"/> carries, so the
/// four are tellable apart with colour removed (flatline). One table, read by the row's badge and
/// by the distinctness test.
/// </summary>
internal static class AccountHealthInfo
{
    /// <summary>The state's word, from the <c>Shell_Health_*</c> keys. Empty for Ok.</summary>
    public static string Word(AccountHealth health) => health switch
    {
        AccountHealth.Expired => Loc.Get("Shell_Health_Expired"),
        AccountHealth.NeedsVerification => Loc.Get("Shell_Health_NeedsVerification"),
        AccountHealth.Limited => Loc.Get("Shell_Health_Limited"),
        AccountHealth.FollowMarked => Loc.Get("Shell_Health_FollowMarked"),
        _ => string.Empty,
    };

    /// <summary>Plain Unicode symbols, not colour emoji. Distinct per state. Empty for Ok.</summary>
    public static string Glyph(AccountHealth health) => health switch
    {
        AccountHealth.Expired => "⚠",            // warning sign
        AccountHealth.NeedsVerification => "⚿",  // squared key
        AccountHealth.Limited => "⏳",            // hourglass
        AccountHealth.FollowMarked => "⇢",       // dashed arrow right
        _ => string.Empty,
    };

    /// <summary>What a click on the badge does, as a short imperative. Empty for Ok.</summary>
    public static string Action(AccountHealth health) => health switch
    {
        AccountHealth.Expired => Loc.Get("Shell_Health_Action_Reauthenticate"),
        AccountHealth.NeedsVerification => Loc.Get("Shell_Health_Action_OpenRoblox"),
        AccountHealth.Limited => Loc.Get("Shell_Health_Action_Reauthenticate"),
        AccountHealth.FollowMarked => Loc.Get("Shell_Health_Action_ToggleMark"),
        _ => string.Empty,
    };

    /// <summary>The badge's accessible name: "{word}, {action}".</summary>
    public static string BadgeName(AccountHealth health) =>
        health == AccountHealth.Ok ? string.Empty : Loc.Format("Shell_Health_BadgeName", Word(health), Action(health));

    /// <summary>The key the row's template switches colour and command on.</summary>
    public static string Kind(AccountHealth health) => health switch
    {
        AccountHealth.Expired => "expired",
        AccountHealth.NeedsVerification => "verify",
        AccountHealth.Limited => "limited",
        AccountHealth.FollowMarked => "follow",
        _ => "ok",
    };
}
