namespace ROROROblox.Core.Discord;

/// <summary>
/// What a desktop alert sounds like (v1.33 item 5). There is a choice to make at all because item 4
/// replaced the Windows shell balloon with one this app draws: <c>ShowBalloonTip</c> always played
/// the system notification sound and had no supported way to ask for a quiet one, while
/// <c>ShowCustomBalloon</c> draws no OS chrome and therefore plays nothing. The sound stopped being
/// Windows' decision and became ours, which means it also became possible to have none.
/// <para>
/// <b><see cref="Chime"/> IS THE ZERO MEMBER, DELIBERATELY.</b> <c>default(AlertSound)</c> is what
/// an unassigned field, a <c>default</c> expression and a zero-initialised struct member all
/// produce. Declaring <see cref="Silent"/> first would put the one dangerous value of the three a
/// single forgotten initialiser away — and silence is how an alert system fails invisibly: nothing
/// throws, nothing logs, the user simply stops being told. The order here is the cheapest possible
/// guard against that and costs nothing, since nothing persists the ordinal (see
/// <see cref="AlertSoundSetting"/>).
/// </para>
/// </summary>
public enum AlertSound
{
    /// <summary>The bundled two-tone chime. The shipped default.</summary>
    Chime = 0,

    /// <summary>Nothing at all. A real choice, and the only one a user has to opt into.</summary>
    Silent = 1,

    /// <summary>Windows' own <c>SystemSounds.Asterisk</c>, whatever the user's sound scheme makes it.</summary>
    WindowsDefault = 2,
}

/// <summary>
/// The one place a persisted <c>AlertSound</c> string becomes a mode, and the one place a mode
/// becomes a string. Every reader goes through here, for the same reason
/// <see cref="AlertCadence.FromSettings"/> is the only resolver of the cadence keys: two guards for
/// one rule is how they drift.
/// <para>
/// <b>Stored as the member NAME.</b> <c>AlertCadenceOverridesJson</c> already made member names
/// load-bearing by keying its map on them, and refused a numeric key for a reason that applies
/// twice as hard here — accepting <c>"1"</c> would make the enum's declaration order persisted
/// state, AND <c>"1"</c> is <see cref="AlertSound.Silent"/>, the exact value a corrupt read must
/// never produce. A name is also what a human hand-editing settings.json can read, which is a
/// supported way in.
/// </para>
/// <para>
/// <b>Every unreadable value degrades to <see cref="AlertSound.Chime"/>, never to silence.</b> Note
/// that this is the OPPOSITE direction from the cadence, which reads a negative minute count as the
/// shipped five minutes. There the loud direction was the harm, so the fallback was the quiet one;
/// here the harm is inverted — an alert that makes no sound is a log entry — so the fallback is
/// too. A settings file nobody can parse must not be able to silence the alert system, in the same
/// way it must not be able to stop it alerting.
/// </para>
/// </summary>
public static class AlertSoundSetting
{
    /// <summary>
    /// What an install with no stored choice sounds like, and what every corrupt read lands on.
    /// </summary>
    public const AlertSound Default = AlertSound.Chime;

    /// <summary>
    /// The string that goes in settings.json. An undeclared mode — legal C# via a cast from an int
    /// — writes <see cref="Default"/>'s name rather than its number, so the file says what the app
    /// is actually going to do instead of carrying a value the next read would reject.
    /// </summary>
    public static string ToSetting(AlertSound sound) =>
        Enum.IsDefined(sound) ? sound.ToString() : Default.ToString();

    /// <summary>
    /// Resolve a stored value. Never throws, and never hands back a mode that is not declared.
    /// <para>
    /// <c>Enum.TryParse</c> alone is not enough and each refusal below is a specific hole it opens:
    /// it accepts a numeric string (<c>"1"</c> → <see cref="AlertSound.Silent"/>), and it accepts
    /// any comma-separated combination of names even on a non-flags enum (<c>"Silent,WindowsDefault"</c>
    /// → <c>(AlertSound)3</c>, a value no switch has a case for). This setting writes exactly one
    /// name, so both are corruption and both read as <see cref="Default"/>.
    /// </para>
    /// </summary>
    public static AlertSound FromSetting(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return Default;

        var name = stored.Trim();

        // A name starts with a letter, and carries no comma. Same two clauses, same reasons, as
        // AlertCadence.TryParseKindName.
        //
        // MEASURED, because the obvious reading of these two lines overstates them (mutation check,
        // 2026-10-06): with both removed, only four of the ten hostile inputs this file tests get
        // through — "1", "2", "Chime,Silent" and "Silent,Silent". The rest ("-1", "0x1", "+1", "42",
        // "Silent, WindowsDefault", "Silent|Chime") are stopped by Enum.IsDefined below, because
        // Enum.TryParse's numeric and flags-combination paths mostly land on values no member has.
        // So the guards are not redundant — the four that do get through include BOTH readings of a
        // corrupt file as Silent, which is the one outcome this setting may not produce — but they
        // earn their place on a narrower set than their names suggest, and Enum.IsDefined is doing
        // more of the work than it looks like it is.
        if (!char.IsLetter(name[0])) return Default;
        if (name.Contains(',', StringComparison.Ordinal)) return Default;

        return Enum.TryParse(name, ignoreCase: true, out AlertSound parsed) && Enum.IsDefined(parsed)
            ? parsed
            : Default;
    }
}
