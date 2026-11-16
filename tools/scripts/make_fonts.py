#!/usr/bin/env python3
"""Creates the static TTF fonts used by the app from the official Google Fonts variable fonts (SIL OFL 1.1).

MAUI on Android needs one font file per weight. This script downloads the variable fonts, cuts the weights we use
with fontTools' instancer and writes them to src/BodyTouchGame.App/Resources/Fonts (licences to .../Fonts/licenses).

Usage (fontTools is not installed system-wide here; Blender's Python with a local package folder works):
  SSL_CERT_FILE=/etc/ssl/certs/ca-certificates.crt PYTHONPATH=~/tools/pylibs \
    ~/tools/blender-4.5.16-linux-x64/4.5/python/bin/python3.11 tools/scripts/make_fonts.py
"""
import os
import sys
import tempfile
import urllib.request

from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(REPO, "src", "BodyTouchGame.App", "Resources", "Fonts")
BASE = "https://raw.githubusercontent.com/google/fonts/main/ofl"

FAMILIES = {
    # family file name, source path, {style: weight}
    "Unbounded": ("unbounded/Unbounded%5Bwght%5D.ttf", "unbounded/OFL.txt", {"Bold": 700, "ExtraBold": 800}),
    "PlusJakartaSans": ("plusjakartasans/PlusJakartaSans%5Bwght%5D.ttf", "plusjakartasans/OFL.txt",
                        {"Regular": 400, "SemiBold": 600, "Bold": 700, "ExtraBold": 800}),
}


def download(url, path):
    with urllib.request.urlopen(url, timeout=60) as response, open(path, "wb") as f:
        f.write(response.read())


def main():
    os.makedirs(os.path.join(OUT, "licenses"), exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        for family, (font_path, licence_path, styles) in FAMILIES.items():
            variable = os.path.join(tmp, family + "-VF.ttf")
            download(f"{BASE}/{font_path}", variable)
            download(f"{BASE}/{licence_path}", os.path.join(OUT, "licenses", f"{family}-OFL.txt"))
            for style, weight in styles.items():
                font = TTFont(variable)
                instance = instantiateVariableFont(font, {"wght": weight}, updateFontNames=True)
                target = os.path.join(OUT, f"{family}-{style}.ttf")
                instance.save(target)
                print(f"{os.path.relpath(target, REPO)}  ({os.path.getsize(target) // 1024} KiB)")


if __name__ == "__main__":
    sys.exit(main())
