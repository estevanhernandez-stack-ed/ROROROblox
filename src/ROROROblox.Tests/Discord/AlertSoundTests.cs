using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

/// <summary>
/// v1.33 item 5 — the persisted sound choice and, more importantly, which way it falls over.
/// <para>
/// WHY THE FALLBACK DIRECTION IS THE WHOLE POINT. Item 4 replaced the shell balloon with one this
/// app draws, and the shell balloon was the only thing forcing the Windows notification sound. So
/// silence is now a state the app can reach by accident, and an alert that makes no sound is a log
/// entry. Item 2's cadence degrades DOWNWARD (a negative minute count reads as the shipped five)
/// because there the loud direction was the harm; here the harm is inverted and so is the
/// fallback. Every unreadable value in this file lands on <see cref="AlertSound.Chime"/>.
/// </para>
/// <para>
/// WHY A NAME AND NOT A NUMBER. <c>AlertCadenceOverridesJson</c> already made member names
/// load-bearing by keying its map on them, and refused a numeric key for exactly the reason that
/// applies here: accepting <c>"1"</c> would make the enum's declaration ORDER persisted state, and
/// <c>"1"</c> is <see cref="AlertSound.Silent"/> — a corrupt file reading as silence is the one
/// outcome this setting must not allow.
/// </para>
/// </summary>
public class AlertSoundTests
{
    [Fact]
    public void TheShippedDefault_IsTheChime()
    {
        // Not Silent and not WindowsDefault. The whole reason the setting exists is that item 4
        // left the drawn balloon silent, and shipping that as the default would be shipping the
        // defect with a switch beside it.
        Assert.Equal(AlertSound.Chime, AlertSoundSetting.Default);
    }

    [Fact]
    public void TheZeroMember_IsTheChimeRatherThanSilence()
    {
        // default(AlertSound) is what an unassigned field, a `default` expression and a
        // zero-initialised struct member all produce. Making Silent the zero member would put the
        // dangerous value one forgotten initialiser away, which is why Chime is declared first.
        Assert.Equal(AlertSound.Chime, default(AlertSound));
        Assert.Equal(0, (int)AlertSound.Chime);
    }

    [Theory]
    [InlineData(AlertSound.Chime)]
    [InlineData(AlertSound.Silent)]
    [InlineData(AlertSound.WindowsDefault)]
    public void EveryMode_SurvivesTheRoundTripThroughTheStoredString(AlertSound mode)
    {
        Assert.Equal(mode, AlertSoundSetting.FromSetting(AlertSoundSetting.ToSetting(mode)));
    }

    [Theory]
    [InlineData(AlertSound.Chime, "Chime")]
    [InlineData(AlertSound.Silent, "Silent")]
    [InlineData(AlertSound.WindowsDefault, "WindowsDefault")]
    public void ToSetting_WritesTheMemberName(AlertSound mode, string expected)
    {
        // The name is what lands in settings.json, so it is what a human hand-editing the file
        // reads and writes. It is asserted literally rather than against nameof() so a rename has
        // to come through here and be noticed as a persisted-format change.
        Assert.Equal(expected, AlertSoundSetting.ToSetting(mode));
    }

    [Fact]
    public void ToSetting_AModeThatIsNotDeclared_WritesTheChimesName()
    {
        // A cast from an out-of-range int is legal C# and this is the one place that would persist
        // it. Writing "42" would make the next read unparseable; writing Chime's name makes the
        // file say what the app is actually going to do.
        Assert.Equal("Chime", AlertSoundSetting.ToSetting((AlertSound)42));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromSetting_AnAbsentOrBlankValue_IsTheChime(string? stored)
    {
        // Absent is the overwhelmingly common case: every settings.json written before this cycle
        // has no such key, so this is the upgrade path for every existing install.
        Assert.Equal(AlertSound.Chime, AlertSoundSetting.FromSetting(stored));
    }

    [Theory]
    [InlineData("silent", AlertSound.Silent)]
    [InlineData("SILENT", AlertSound.Silent)]
    [InlineData("windowsdefault", AlertSound.WindowsDefault)]
    [InlineData("WINDOWSDEFAULT", AlertSound.WindowsDefault)]
    [InlineData("chime", AlertSound.Chime)]
    public void FromSetting_IgnoresCase(string stored, AlertSound expected)
    {
        // A hand-edited file is a supported way in (AppSettings' own doc comments say so), and a
        // human types "silent".
        Assert.Equal(expected, AlertSoundSetting.FromSetting(stored));
    }

    [Theory]
    [InlineData(" Silent ")]
    [InlineData("\tSilent\r\n")]
    public void FromSetting_TrimsSurroundingWhitespace(string stored)
    {
        Assert.Equal(AlertSound.Silent, AlertSoundSetting.FromSetting(stored));
    }

    [Theory]
    [InlineData("beep")]
    [InlineData("Ping")]
    [InlineData("!!")]
    [InlineData("Chimey")]
    [InlineData("null")]
    public void FromSetting_AnUnrecognisedValue_IsTheChimeRatherThanSilence(string stored)
    {
        Assert.Equal(AlertSound.Chime, AlertSoundSetting.FromSetting(stored));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("-1")]
    [InlineData("42")]
    public void FromSetting_ANumericValue_IsRefusedRatherThanReadAsAnOrdinal(string stored)
    {
        // Enum.TryParse accepts a numeric string and would read "1" as Silent. Two harms in one:
        // it makes the declaration order persisted state, and the specific value it silently
        // produces is the one a corrupt read must never produce. Refused outright, the same way
        // AlertCadence.TryParseKindName refuses a numeric key.
        Assert.Equal(AlertSound.Chime, AlertSoundSetting.FromSetting(stored));
    }

    [Theory]
    [InlineData("Chime,Silent")]
    [InlineData("Silent, WindowsDefault")]
    [InlineData("Silent,Silent")]
    public void FromSetting_ACombinationOfNames_IsRefused(string stored)
    {
        // Enum.TryParse treats a comma list as a flags combination even on a non-flags enum, so
        // "Silent,WindowsDefault" parses cleanly to (AlertSound)3 — a value no switch has a case
        // for. This setting writes exactly one name, so a comma is corruption.
        Assert.Equal(AlertSound.Chime, AlertSoundSetting.FromSetting(stored));
    }

    [Fact]
    public void FromSetting_NeverHandsBackAModeThatIsNotDeclared()
    {
        // The guarantee every caller leans on: the switch at the play site has three cases and no
        // sensible default other than "make a noise". Proven across the awkward inputs together,
        // because each clause above could be right on its own while some combination slipped past.
        string[] hostile =
        [
            null!, "", " ", "3", "1,2", "Silent|Chime", "0x1", "+1", "WindowsDefault ,Chime",
            "Chime\0", "ChIme", "silent;", "  WindowsDefault  ",
        ];

        Assert.All(hostile, s => Assert.True(
            Enum.IsDefined(AlertSoundSetting.FromSetting(s)),
            $"FromSetting({s ?? "null"}) produced an undeclared mode."));
    }
}
