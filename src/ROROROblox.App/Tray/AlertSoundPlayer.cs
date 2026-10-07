using System.IO;
using System.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.Core.Discord;

namespace ROROROblox.App.Tray;

/// <summary>
/// The noise one desktop alert makes. Asked for once per drawn balloon, from
/// <see cref="TrayService.ShowToast"/>.
/// <para>
/// <b>WHY NOT FROM THE DISPATCHER.</b> <c>AlertDispatcher</c> fans one event out to the destinations
/// a kind is routed to — the desktop, the clan channel, a private webhook, a phone — sequentially,
/// in a loop. A sound played there plays once per DESTINATION, so a user with three ticked would
/// hear three chimes for one memory crossing. One event is one sound, and the only leg of the
/// fan-out that makes a noise is the one that puts something on the screen.
/// <c>OnePathToTheScreenFenceTests</c> holds that down.
/// </para>
/// </summary>
internal interface IAlertSoundPlayer
{
    /// <summary>
    /// Make the noise the user chose, if any. Never throws — see
    /// <see cref="AlertSoundPlayer.Play"/> for why that is a contract rather than a courtesy.
    /// </summary>
    void Play();
}

/// <summary>
/// Plays the chime, Windows' asterisk, or nothing.
/// <para>
/// <b><see cref="SoundPlayer"/> AND NOT <c>MediaPlayer</c>.</b> <c>MediaPlayer</c> is a
/// <c>DispatcherObject</c> and affinitises to its creating thread; this plays from the tray path,
/// which is reached from the dispatcher's fan-out on a thread-pool timer thread and marshalled into
/// the UI dispatcher by <see cref="TrayService.ShowToast"/> — a second dispatcher-affine object in
/// that chain is a second way to throw cross-thread. <see cref="SoundPlayer"/> has no such
/// affinity, is in the framework (no new NuGet entry in a binary that ships to the Store with an
/// auth-cookie threat model), and its <c>Play</c> reads the stream on the calling thread and then
/// hands the bytes to the OS, so the stream can be disposed on the way out.
/// </para>
/// <para>
/// <b>THE MODE IS READ ON EVERY PLAY.</b> <paramref name="mode"/> is a function, not a value,
/// because the checklist asks for "changing the setting takes effect on the next alert with no
/// restart" and a captured value would satisfy every other clause while failing that one. What it
/// reads today is <c>App.AlertSoundSetting</c>, the volatile the 30 s settings tick refreshes —
/// the same shape <c>App.AlertCadenceSetting</c> uses and for the same reason: this runs on a
/// thread that must not wait on a settings file.
/// </para>
/// <para>
/// <b>THE TWO TERMINAL ACTIONS ARE INJECTABLE</b> through the internal constructor, which is what
/// makes any of this testable. A suite that really called <see cref="SoundPlayer.Play"/> would make
/// noise on a developer's machine and would be a silent no-op on a build agent with no audio
/// device — proving nothing in either place. So the DECISION is tested and the two calls are not,
/// which is where all of this item's failure modes live anyway.
/// </para>
/// </summary>
internal sealed class AlertSoundPlayer : IAlertSoundPlayer
{
    /// <summary>
    /// The <c>LogicalName</c> from the csproj, which also explains why this is an
    /// <c>EmbeddedResource</c> rather than a WPF <c>Resource</c> like the ICOs beside it.
    /// </summary>
    internal const string ChimeResourceName = "ROROROblox.App.Tray.alert-chime.wav";

    private readonly Func<AlertSound> _mode;
    private readonly Func<Stream?> _openChime;
    private readonly Action _playSystemSound;
    private readonly ILogger _log;

    public AlertSoundPlayer(Func<AlertSound> mode, ILogger<AlertSoundPlayer>? log = null)
        : this(mode, OpenBundledChime, PlayAsterisk, log)
    {
    }

    internal AlertSoundPlayer(
        Func<AlertSound> mode,
        Func<Stream?> openChime,
        Action playSystemSound,
        ILogger? log = null)
    {
        _mode = mode;
        _openChime = openChime;
        _playSystemSound = playSystemSound;
        _log = log ?? NullLogger.Instance;
    }

    /// <summary>
    /// <b>NEVER THROWS, AND THAT IS THE POINT OF THE METHOD.</b> It is called from inside
    /// <see cref="TrayService.ShowToast"/>'s dispatcher marshal, which sits inside
    /// <c>AlertDispatcher</c>'s sequential fan-out loop — and that loop's measured behaviour
    /// (2026-09-11) is that an exception ends it, so every destination ordered after the desktop
    /// loses its send too. A missing sound file would therefore cost a user their phone alert. An
    /// alert that crashes because an asset is absent is worse than a quiet alert, so every failure
    /// here degrades to silence for that one notification.
    /// <para>
    /// Debug, not Warning: nobody is going to act on it, and the only reader is whoever is holding
    /// a log asking why it went quiet. A Warning per alert would also be a Warning per alert.
    /// </para>
    /// <para>
    /// The catch is around the whole switch rather than per branch, because the mode provider can
    /// throw too — it reads a volatile field today and cannot, which is exactly why pinning it now
    /// is cheaper than pinning it after someone makes it read a file.
    /// </para>
    /// </summary>
    public void Play()
    {
        try
        {
            switch (_mode())
            {
                case AlertSound.Silent:
                    return;

                case AlertSound.WindowsDefault:
                    _playSystemSound();
                    return;

                // Chime, and anything undeclared. AlertSoundSetting.FromSetting already refuses to
                // produce an undeclared mode, so this is the second of two guards — and it leans the
                // same way, because a fourth mode added without updating this site should still make
                // a noise rather than silently turn alerts into log entries.
                default:
                    PlayChime();
                    return;
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Couldn't play the alert sound; this alert is silent.");
        }
    }

    private void PlayChime()
    {
        using var stream = _openChime();
        if (stream is null)
        {
            _log.LogDebug(
                "The bundled chime ({Resource}) isn't in this build; this alert is silent.",
                ChimeResourceName);
            return;
        }

        using var player = new SoundPlayer(stream);
        player.Play();
    }

    /// <summary>
    /// The production loader, internal so the asset clauses in <c>AlertSoundPlayerTests</c> measure
    /// the bytes the app really gets rather than a copy read off disk.
    /// </summary>
    internal static Stream? OpenBundledChime() =>
        typeof(AlertSoundPlayer).Assembly.GetManifestResourceStream(ChimeResourceName);

    private static void PlayAsterisk() => SystemSounds.Asterisk.Play();
}
