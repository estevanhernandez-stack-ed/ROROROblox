using System.IO;
using Microsoft.Extensions.Logging;
using ROROROblox.App.Tray;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests;

/// <summary>
/// v1.33 item 5 — what the three modes actually do, how a broken asset degrades, and the committed
/// chime measured against its own recipe.
/// <para>
/// WHY THERE IS A SEAM AT ALL. The real player ends in <c>System.Media.SoundPlayer.Play()</c> and
/// <c>SystemSounds.Asterisk.Play()</c>, neither of which a test may call — a suite that makes noise
/// is a suite nobody runs with headphones on, and on a build agent with no audio device the call is
/// a no-op that proves nothing either way. So the two terminal actions are injected and the
/// DECISION is what gets tested, which is where every one of this item's failure modes lives.
/// </para>
/// <para>
/// WHAT CANNOT BE TESTED HERE, said plainly so a green run is not mistaken for a verdict: whether
/// the chime sounds pleasant, whether it reads as a notification rather than an error, and whether
/// it is audible over a Roblox client. The clauses below pin frequency, duration, loudness and
/// envelope — the measurable consequences of the recipe. A human listens at C2.
/// </para>
/// </summary>
public class AlertSoundPlayerTests
{
    /// <summary>
    /// A player whose two terminal actions are counters. <paramref name="chime"/> supplies what
    /// the chime path gets when it asks for the asset — a stream, <c>null</c> for "not there", or a
    /// throw.
    /// </summary>
    private static (AlertSoundPlayer Player, Func<int> ChimeAsks, Func<int> SystemAsks, CapturingLogger<AlertSoundPlayer> Log)
        Build(Func<AlertSound> mode, Func<Stream?>? chime = null, Action? system = null)
    {
        var chimeAsks = 0;
        var systemAsks = 0;
        var log = new CapturingLogger<AlertSoundPlayer>();

        var player = new AlertSoundPlayer(
            mode,
            () => { chimeAsks++; return chime is null ? new MemoryStream(SilentWave()) : chime(); },
            () => { systemAsks++; system?.Invoke(); },
            log);

        return (player, () => chimeAsks, () => systemAsks, log);
    }

    /// <summary>
    /// A valid but inaudible 1-frame WAVE, so the chime path can be driven to its end without the
    /// committed asset and without a sound card.
    /// </summary>
    private static byte[] SilentWave()
    {
        using var buffer = new MemoryStream();
        using var w = new BinaryWriter(buffer);
        w.Write("RIFF".ToCharArray());
        w.Write(36 + 2);
        w.Write("WAVE".ToCharArray());
        w.Write("fmt ".ToCharArray());
        w.Write(16);
        w.Write((short)1);      // PCM
        w.Write((short)1);      // mono
        w.Write(44100);
        w.Write(44100 * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data".ToCharArray());
        w.Write(2);
        w.Write((short)0);
        w.Flush();
        return buffer.ToArray();
    }

    [Fact]
    public void Silent_PlaysNothingAtAll()
    {
        var (player, chimeAsks, systemAsks, log) = Build(() => AlertSound.Silent);

        player.Play();

        Assert.Equal(0, chimeAsks());
        Assert.Equal(0, systemAsks());
        // And it is not a degraded path: nothing is logged, because nothing went wrong.
        Assert.Empty(log.Snapshot());
    }

    [Fact]
    public void Chime_AsksForTheBundledWaveAndNotForTheSystemSound()
    {
        var (player, chimeAsks, systemAsks, _) = Build(() => AlertSound.Chime);

        player.Play();

        Assert.Equal(1, chimeAsks());
        Assert.Equal(0, systemAsks());
    }

    [Fact]
    public void WindowsDefault_PlaysTheSystemSoundAndNotTheWave()
    {
        var (player, chimeAsks, systemAsks, _) = Build(() => AlertSound.WindowsDefault);

        player.Play();

        Assert.Equal(1, systemAsks());
        Assert.Equal(0, chimeAsks());
    }

    [Fact]
    public void AModeThatIsNotDeclared_PlaysTheChime()
    {
        // Belt and braces over AlertSoundSetting.FromSetting, which already refuses to produce one.
        // The switch here has no business having a silent default: if a fourth mode is added and
        // this site is not updated, the alert should still make a noise.
        var (player, chimeAsks, systemAsks, _) = Build(() => (AlertSound)42);

        player.Play();

        Assert.Equal(1, chimeAsks());
        Assert.Equal(0, systemAsks());
    }

    [Fact]
    public void TheModeIsReadOnEveryPlay_SoAChangedSettingNeedsNoRestart()
    {
        // The checklist criterion, as the only thing that can hold it: a player that captured the
        // mode once in its constructor would pass every clause above and still require a restart.
        var mode = AlertSound.Silent;
        var (player, chimeAsks, _, _) = Build(() => mode);

        player.Play();
        Assert.Equal(0, chimeAsks());

        mode = AlertSound.Chime;
        player.Play();
        Assert.Equal(1, chimeAsks());
    }

    [Fact]
    public void AMissingChimeAsset_DegradesToSilenceForThatAlertAndSaysSoAtDebug()
    {
        // A notification that crashes the app because a sound file is not there is worse than a
        // quiet notification. Debug and not Warning: the user is not going to act on it, and the
        // only reader is whoever is holding a log asking why it went quiet.
        var (player, _, systemAsks, log) = Build(() => AlertSound.Chime, chime: () => null);

        player.Play();

        Assert.Equal(0, systemAsks());
        // Its own line, naming the resource, because "the asset is not in this build" is a
        // packaging mistake and the resource name is the only part of it worth logging.
        Assert.Contains(log.Snapshot(), l =>
            l.StartsWith("[Debug]", StringComparison.Ordinal)
            && l.Contains(AlertSoundPlayer.ChimeResourceName, StringComparison.Ordinal));
        Assert.DoesNotContain(log.Snapshot(), l =>
            l.StartsWith("[Warning]", StringComparison.Ordinal)
            || l.StartsWith("[Error]", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnreadableChimeAsset_DoesNotThrowIntoTheAlertPath()
    {
        // Play() is called from TrayService.ShowToast, inside the dispatcher's sequential fan-out
        // loop. A throw there does not just lose the sound: the 2026-09-11 defect in that same loop
        // was that an exception ended it, so every destination ordered after Local lost its send
        // too. This is the one clause that is about the blast radius rather than the sound.
        var (player, _, systemAsks, log) = Build(
            () => AlertSound.Chime,
            chime: () => throw new IOException("the resource stream is torn"));

        player.Play();

        Assert.Equal(0, systemAsks());
        // The CATCH line, which is a different line from the missing-asset one above: a throw and
        // an absence are different failures and a test that accepted either would pass with only
        // one of the two handled. (The exception itself is handed to the logger; CapturingLogger
        // formats the template only, so the branch is what is asserted here.)
        Assert.Contains(log.Snapshot(), l => l == "[Debug] Couldn't play the alert sound; this alert is silent.");
    }

    [Fact]
    public void AThrowingSystemSound_DoesNotThrowIntoTheAlertPath()
    {
        // SystemSounds reads the user's sound scheme out of the registry. Same reasoning as above,
        // and it is a separate clause because a try/catch wrapped around only the chime branch
        // would pass the one before this and fail here.
        var (player, _, _, log) = Build(
            () => AlertSound.WindowsDefault,
            system: () => throw new InvalidOperationException("no audio endpoint"));

        player.Play();

        Assert.Contains(log.Snapshot(), l => l == "[Debug] Couldn't play the alert sound; this alert is silent.");
    }

    [Fact]
    public void AThrowingModeProvider_DoesNotThrowIntoTheAlertPath()
    {
        // The provider reads a volatile field in the composition root today, so it cannot throw —
        // which is exactly why this is worth pinning now rather than after someone makes it read a
        // file.
        var (player, _, systemAsks, log) = Build(() => throw new InvalidOperationException("no settings"));

        player.Play();

        Assert.Equal(0, systemAsks());
        Assert.Contains(log.Snapshot(), l => l.StartsWith("[Debug]", StringComparison.Ordinal));
    }

    [Fact]
    public void TheDefaultConstructor_ReachesTheBundledAssetAndTheSystemSound()
    {
        // The seam above is only honest if the production wiring goes through the same two actions.
        // Silent is the one mode that can be driven on the REAL ctor without making a noise, so
        // this clause proves the ctor exists and runs, and the asset clause below proves the
        // resource the real loader resolves is really there.
        var player = new AlertSoundPlayer(() => AlertSound.Silent);

        player.Play();

        Assert.NotNull(AlertSoundPlayer.OpenBundledChime());
    }

    // ---- The committed asset, measured against its recipe -------------------------------------

    private const int SampleRate = 44100;
    private const double FirstTone = 659.25;    // E5
    private const double SecondTone = 987.77;   // B5

    private static short[] ReadBundledChime(out int sampleRate, out int channels, out int bits, out int bytes)
    {
        using var stream = AlertSoundPlayer.OpenBundledChime();
        Assert.NotNull(stream);

        using var copy = new MemoryStream();
        stream!.CopyTo(copy);
        var raw = copy.ToArray();
        bytes = raw.Length;

        Assert.True(raw.Length > 44, "the chime resource is too short to be a WAVE file at all");
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(raw, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(raw, 8, 4));

        // Walk the chunks rather than assuming a 44-byte canonical header: Python's wave module
        // writes exactly that today, but a file regenerated by any other tool may carry a LIST or
        // fact chunk and this clause should still be measuring the samples.
        var offset = 12;
        sampleRate = 0; channels = 0; bits = 0;
        short[]? samples = null;

        while (offset + 8 <= raw.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(raw, offset, 4);
            var size = BitConverter.ToInt32(raw, offset + 4);
            var body = offset + 8;

            if (id == "fmt ")
            {
                Assert.Equal(1, BitConverter.ToInt16(raw, body));    // uncompressed PCM
                channels = BitConverter.ToInt16(raw, body + 2);
                sampleRate = BitConverter.ToInt32(raw, body + 4);
                bits = BitConverter.ToInt16(raw, body + 14);
            }
            else if (id == "data")
            {
                samples = new short[size / 2];
                for (var i = 0; i < samples.Length; i++)
                {
                    samples[i] = BitConverter.ToInt16(raw, body + (i * 2));
                }
            }

            offset = body + size + (size % 2);
        }

        Assert.NotNull(samples);
        return samples!;
    }

    /// <summary>
    /// Goertzel energy at one frequency over one window — enough to answer "which of two tones is
    /// sounding here" without an FFT or a dependency.
    /// </summary>
    private static double EnergyAt(short[] samples, int from, int to, double frequency)
    {
        var w = 2.0 * Math.Cos(2.0 * Math.PI * frequency / SampleRate);
        double s1 = 0, s2 = 0;
        for (var i = from; i < to && i < samples.Length; i++)
        {
            var s = (samples[i] / 32768.0) + (w * s1) - s2;
            s2 = s1;
            s1 = s;
        }

        return (s1 * s1) + (s2 * s2) - (w * s1 * s2);
    }

    [Fact]
    public void TheChime_IsSixteenBitMonoAtFortyFourOneAndSmall()
    {
        var samples = ReadBundledChime(out var rate, out var channels, out var bits, out var bytes);

        Assert.Equal(SampleRate, rate);
        Assert.Equal(1, channels);
        Assert.Equal(16, bits);
        Assert.True(bytes < 64 * 1024, $"the chime is {bytes} bytes; it ships inside the binary");
        Assert.True(samples.Length > 1000, "the chime has almost no samples in it");
    }

    [Fact]
    public void TheChime_IsShorterThanFourHundredMilliseconds()
    {
        var samples = ReadBundledChime(out _, out _, out _, out _);
        var ms = samples.Length * 1000.0 / SampleRate;

        Assert.InRange(ms, 150.0, 400.0);
    }

    [Fact]
    public void TheChime_IsQuiet()
    {
        // A notification sound at full scale is an alarm. The ceiling is what makes "quiet" a
        // property of the file rather than a hope about the recipe; the floor is there because a
        // sound nobody hears fails the item just as completely.
        var samples = ReadBundledChime(out _, out _, out _, out _);
        var peak = samples.Max(s => Math.Abs((int)s));

        Assert.InRange(peak / 32767.0, 0.08, 0.28);
    }

    [Fact]
    public void TheChime_HasNoSharpAttackAndNoClickAtEitherEnd()
    {
        // A PCM buffer that starts or ends away from zero clicks through the speaker, and the click
        // is the part that reads as an error. The first five milliseconds are held well under the
        // peak, which is the measurable half of "no sharp attack".
        var samples = ReadBundledChime(out _, out _, out _, out _);
        var peak = samples.Max(s => Math.Abs((int)s));

        Assert.Equal(0, samples[0]);
        Assert.Equal(0, samples[^1]);

        var firstFiveMs = samples.Take(SampleRate * 5 / 1000).Max(s => Math.Abs((int)s));
        Assert.True(firstFiveMs < peak * 0.15,
            $"the first 5 ms reach {firstFiveMs} against a peak of {peak} — that is an attack, not a swell");

        var lastTenMs = samples.Skip(samples.Length - (SampleRate * 10 / 1000)).Max(s => Math.Abs((int)s));
        Assert.True(lastTenMs == 0, $"the last 10 ms are not silent (peak {lastTenMs}); the tail is what stops the click");
    }

    [Fact]
    public void TheChime_IsTwoTonesAndTheSecondIsTheHigherOne()
    {
        // "Two soft tones" is the recipe, and rising is what keeps it from reading as an error —
        // falling intervals are the universal "something stopped" gesture. Measured rather than
        // asserted about the generator: the first window is before the second tone starts and the
        // second window is after the first tone ends, so each should be dominated by its own
        // frequency. A single-tone file, or the same two tones swapped, fails here.
        var samples = ReadBundledChime(out _, out _, out _, out _);

        var earlyLow = EnergyAt(samples, SampleRate * 20 / 1000, SampleRate * 90 / 1000, FirstTone);
        var earlyHigh = EnergyAt(samples, SampleRate * 20 / 1000, SampleRate * 90 / 1000, SecondTone);
        Assert.True(earlyLow > earlyHigh * 3,
            $"the first window is not dominated by {FirstTone} Hz (low {earlyLow:G3} vs high {earlyHigh:G3})");

        var lateLow = EnergyAt(samples, SampleRate * 200 / 1000, SampleRate * 300 / 1000, FirstTone);
        var lateHigh = EnergyAt(samples, SampleRate * 200 / 1000, SampleRate * 300 / 1000, SecondTone);
        Assert.True(lateHigh > lateLow * 3,
            $"the second window is not dominated by {SecondTone} Hz (low {lateLow:G3} vs high {lateHigh:G3})");
    }

    [Fact]
    public void TheGeneratorScriptIsCommittedBesideTheAsset()
    {
        // The asset is reproducible or it is a mystery binary. The script is the recipe, and it has
        // to agree with the two frequencies this file measures — a script that drifts from the
        // committed wav would leave the next regeneration failing the clauses above with no clue
        // why.
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.NotNull(root);

        var script = Path.Combine(root!, "scripts", "generate-alert-chime.py");
        Assert.True(File.Exists(script), "scripts/generate-alert-chime.py is gone; the chime is now a mystery binary");

        var text = File.ReadAllText(script);
        Assert.Contains(FirstTone.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
        Assert.Contains(SecondTone.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
    }
}
