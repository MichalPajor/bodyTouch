#!/usr/bin/env python3
"""Creates the static TTF fonts used by the app from the official Google Fonts variable fonts (SIL OFL 1.1).

MAUI on Android needs one font file per weight. This script downloads the variable fonts, cuts the weights we use
with fontTools' instancer and writes them to src/BodyTouchGame.App/Resources/Fonts (licences to .../Fonts/licenses).

Plus Jakarta Sans has no Cyrillic: the Cyrillic glyphs (U+0400–U+04FF, Ukrainian, stage 10) are taken from Manrope
(OFL, a similar geometric sans) at the same weight, scaled to the same units per em and merged into each Jakarta file,
so the app keeps one font family for every language.

Usage (fontTools is not installed system-wide here; Blender's Python with a local package folder works):
  SSL_CERT_FILE=/etc/ssl/certs/ca-certificates.crt PYTHONPATH=~/tools/pylibs \
    ~/tools/blender-4.5.16-linux-x64/4.5/python/bin/python3.11 tools/scripts/make_fonts.py
"""
import os
import sys
import tempfile
import urllib.request

from fontTools import subset
from fontTools.merge import Merger
from fontTools.ttLib import TTFont
from fontTools.ttLib.scaleUpem import scale_upem
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
# Family missing glyphs -> (donor variable font, licence, unicode ranges)
CYRILLIC_DONOR = ("manrope/Manrope%5Bwght%5D.ttf", "manrope/OFL.txt", [(0x0400, 0x04FF)])


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
            donor = None
            if family == "PlusJakartaSans":
                donor = os.path.join(tmp, "Manrope-VF.ttf")
                download(f"{BASE}/{CYRILLIC_DONOR[0]}", donor)
                download(f"{BASE}/{CYRILLIC_DONOR[1]}", os.path.join(OUT, "licenses", "Manrope-OFL.txt"))
            for style, weight in styles.items():
                font = TTFont(variable)
                instance = instantiateVariableFont(font, {"wght": weight}, updateFontNames=True)
                target = os.path.join(OUT, f"{family}-{style}.ttf")
                instance.save(target)
                if donor:
                    add_glyphs(target, donor, weight, tmp)
                print(f"{os.path.relpath(target, REPO)}  ({os.path.getsize(target) // 1024} KiB)")


def add_glyphs(target, donor_variable, weight, tmp):
    """Merges the donor's Cyrillic glyphs (same weight) into the target font file."""
    donor = instantiateVariableFont(TTFont(donor_variable), {"wght": weight})
    options = subset.Options()
    options.layout_features = ["kern", "liga", "calt"]
    options.notdef_outline = True
    unicodes = [u for start, end in CYRILLIC_DONOR[2] for u in range(start, end + 1)]
    subsetter = subset.Subsetter(options)
    subsetter.populate(unicodes=unicodes)
    subsetter.subset(donor)
    base = TTFont(target)
    if donor["head"].unitsPerEm != base["head"].unitsPerEm:
        scale_upem(donor, base["head"].unitsPerEm)
    donor_path = os.path.join(tmp, f"donor-{weight}.ttf")
    donor.save(donor_path)
    # Merger needs the same glyph order rules; drop layout tables of the donor to keep the merge simple.
    for table in ("GSUB", "GPOS", "GDEF"):
        d = TTFont(donor_path)
        if table in d:
            del d[table]
            d.save(donor_path)
    merged = Merger().merge([target, donor_path])
    merged.save(target)


if __name__ == "__main__":
    sys.exit(main())
