#!/usr/bin/env python3
"""Generate the bundled alert chime (RoRoRo v1.33 item 5).

The asset is committed, so this script is the recipe rather than a build step. It exists for the
same reason generate-tray-icons.ps1 does: an asset nobody can regenerate is a mystery binary, and
the next person who wants the chime a semitone lower should be editing numbers, not a waveform.

    python scripts/generate-alert-chime.py

Writes src/ROROROblox.App/Tray/Resources/alert-chime.wav. Deterministic: stdlib only, no numpy, no
randomness, so a regeneration on any machine produces a byte-identical file.

WHAT THE NUMBERS ARE FOR, since they are the whole deliverable:

  Two tones, rising.      E5 -> B5, a perfect fifth up. Rising is what keeps it from reading as an
                          error: a falling interval is the universal "something stopped" gesture,
                          which is exactly the wrong thing to say when the message is "look at
                          this". The fifth is the most consonant interval that is not a unison, so
                          it does not sound like a verdict either way.

  No sharp attack.        Each tone's envelope is a raised cosine over its first 45 ms, so the
                          waveform swells rather than starting. A step onto a non-zero sample is a
                          click, and a click is the part of a notification sound that reads as an
                          error even when the pitch does not.

  Quiet.                  Peak normalised to -13 dBFS. A notification at full scale is an alarm.

  Short.                  340 ms of tone plus a 10 ms silent tail. The tail is not padding: it
                          guarantees the buffer ends at zero, so there is no click on the way out
                          either.

  Overlapped.             The second tone starts 110 ms in, while the first is still ringing, so
                          the pair reads as one gesture instead of two beeps. A gap between them
                          would also mean a second attack, which is a second chance to click.

  Second harmonic at 12%. A pure sine is clean but thin through a laptop speaker; a touch of the
                          octave gives it a body that survives a small driver without adding any
                          brightness. Higher harmonics are deliberately absent -- brightness is
                          what makes a sound feel urgent.

THE TWO FREQUENCIES BELOW ARE ASSERTED, not just used: AlertSoundPlayerTests measures the committed
wav with a Goertzel filter at each of them and requires each to dominate its own window. Change one
here and the test tells you which half of the pair drifted.
"""

import math
import os
import struct
import wave

SAMPLE_RATE = 44100
BITS = 16
CHANNELS = 1

# E5 then B5 -- a rising perfect fifth.
FIRST_TONE_HZ = 659.25
SECOND_TONE_HZ = 987.77

# (start_ms, duration_ms, relative_amplitude)
FIRST_TONE = (0, 180, 1.00)
SECOND_TONE = (110, 230, 0.85)

ATTACK_MS = 45
TAIL_MS = 10
PEAK = 0.2239  # ~ -13 dBFS
SECOND_HARMONIC = 0.12

OUT = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "src", "ROROROblox.App", "Tray", "Resources", "alert-chime.wav",
)


def envelope(position_ms, duration_ms):
    """A bell: raised-cosine swell into a raised-cosine fall, both ending at exactly zero.

    No sustain segment. A flat top would need a third joint, and a joint is where a discontinuity
    hides -- the whole point of this shape is that the first derivative never jumps.
    """
    if position_ms < 0 or position_ms > duration_ms:
        return 0.0
    if position_ms < ATTACK_MS:
        return 0.5 * (1.0 - math.cos(math.pi * position_ms / ATTACK_MS))
    fall = (position_ms - ATTACK_MS) / (duration_ms - ATTACK_MS)
    return 0.5 * (1.0 + math.cos(math.pi * fall))


def tone(frames, frequency, start_ms, duration_ms, amplitude):
    for i in range(len(frames)):
        t_ms = i * 1000.0 / SAMPLE_RATE
        env = envelope(t_ms - start_ms, duration_ms)
        if env == 0.0:
            continue
        phase = 2.0 * math.pi * frequency * (t_ms - start_ms) / 1000.0
        value = math.sin(phase) + (SECOND_HARMONIC * math.sin(2.0 * phase))
        frames[i] += amplitude * env * value


def main():
    total_ms = max(start + length for start, length, _ in (FIRST_TONE, SECOND_TONE)) + TAIL_MS
    frames = [0.0] * int(round(SAMPLE_RATE * total_ms / 1000.0))

    tone(frames, FIRST_TONE_HZ, *FIRST_TONE)
    tone(frames, SECOND_TONE_HZ, *SECOND_TONE)

    # Normalise to the stated peak rather than trusting the amplitudes to sum to it: the tones
    # overlap, so the actual maximum depends on where their envelopes cross and on the phase
    # relationship at that instant. Asserting the peak instead of predicting it is what lets the
    # test state a loudness ceiling the file really holds.
    loudest = max(abs(v) for v in frames) or 1.0
    scale = PEAK / loudest

    pcm = bytearray()
    for v in frames:
        sample = int(round(max(-1.0, min(1.0, v * scale)) * 32767))
        pcm += struct.pack("<h", sample)

    with wave.open(OUT, "wb") as w:
        w.setnchannels(CHANNELS)
        w.setsampwidth(BITS // 8)
        w.setframerate(SAMPLE_RATE)
        w.writeframes(bytes(pcm))

    print(
        "wrote {} ({} bytes, {} frames, {:.0f} ms, peak {:.4f} FS)".format(
            OUT, os.path.getsize(OUT), len(frames), total_ms, PEAK
        )
    )


if __name__ == "__main__":
    main()
