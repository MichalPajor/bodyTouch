#!/usr/bin/env python3
"""Generates the short UI sounds (start / end of touching) as 16-bit mono WAV files.

Usage: python3 -I tools/scripts/make_sounds.py   -> src/BodyTouchGame.App/Resources/Raw/sounds/*.wav
Sounds are synthesised (no third-party audio), so there are no licence questions.
"""
import math
import os
import struct
import wave

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "src", "BodyTouchGame.App", "Resources", "Raw", "sounds")
RATE = 44100


def tone(freq, seconds, volume=0.5):
    n = int(RATE * seconds)
    attack, release = int(RATE * 0.008), int(RATE * 0.06)
    for i in range(n):
        env = min(1.0, i / attack) * min(1.0, (n - i) / release)
        # sine + a little third harmonic for a brighter, "game" beep
        s = math.sin(2 * math.pi * freq * i / RATE) + 0.25 * math.sin(2 * math.pi * 3 * freq * i / RATE)
        yield volume * env * s / 1.25


def silence(seconds):
    return (0.0 for _ in range(int(RATE * seconds)))


def write(name, *parts):
    path = os.path.join(OUT, name)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        for part in parts:
            w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, s)) * 32767)) for s in part))
    print(os.path.relpath(path), os.path.getsize(path), "B")


os.makedirs(OUT, exist_ok=True)
write("touch_start.wav", tone(880, 0.12), silence(0.05), tone(1320, 0.18))      # rising "go!"
write("touch_end.wav", tone(1320, 0.12), silence(0.05), tone(660, 0.35))        # falling "stop"
