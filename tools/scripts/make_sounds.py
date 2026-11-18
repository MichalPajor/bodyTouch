#!/usr/bin/env python3
"""Generates the short UI sounds as 16-bit mono WAV files: start / end of touching, countdown ticks, result sounds
(perfect hit, points, miss) and the end-of-game fanfare.

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
write("tick.wav", tone(1760, 0.045, 0.35))                                         # countdown, last 3 s
write("reveal_perfect.wav", tone(784, 0.09), tone(988, 0.09), tone(1175, 0.09), tone(1568, 0.32))  # arpeggio up
write("reveal_points.wav", tone(659, 0.1), silence(0.03), tone(988, 0.22))         # pleasant two-note
write("reveal_miss.wav", tone(392, 0.16), silence(0.03), tone(294, 0.38, 0.45))    # "womp womp"
write("game_over.wav", tone(523, 0.14), tone(659, 0.14), tone(784, 0.14), silence(0.04), tone(1047, 0.5))  # fanfare
