#!/usr/bin/env python3
"""Unpacks the bundled UI mockups into plain, readable HTML files.

The source file ("Body Touch Game – propozycje UI.html") is a nested bundle:
  outer page  -> <script type="__bundler/manifest">  {uuid: {mime, compressed, data(base64[+gzip])}}
                 <script type="__bundler/page_order"> [uuid, ...]   (screen order)
  each screen -> again a bundle; its <script type="__bundler/template"> is a JSON string holding
                 static HTML with inline styles. Resources (fonts, images) are referenced by uuid.

Output (in OUT_DIR):
  NN-slug.html            static screen, 390x844, no scripts, local fonts and images
  assets/fonts/*.woff2    Unbounded and Plus Jakarta Sans (latin + latin-ext subsets)
  assets/img/NN-*.png     mannequin preview images (illustrative only, see spec 4.1)

Usage: python3 -I tools/scripts/unpack_mockups.py <bundle.html> <out_dir>
"""
from __future__ import annotations

import base64
import gzip
import hashlib
import json
import re
import sys
import unicodedata
from pathlib import Path

BUNDLER_SCRIPT = re.compile(r'<script type="__bundler/(\w+)">(.*?)</script>', re.S)


def read_bundle(html: str) -> dict[str, object]:
    parts: dict[str, object] = {}
    for match in BUNDLER_SCRIPT.finditer(html):
        parts[match.group(1)] = json.loads(match.group(2).strip())
    return parts


def decode_resource(entry: dict) -> bytes:
    data = base64.b64decode(entry["data"])
    return gzip.decompress(data) if entry.get("compressed") else data


def slugify(text: str) -> str:
    text = text.replace("ł", "l").replace("Ł", "L")
    text = unicodedata.normalize("NFKD", text).encode("ascii", "ignore").decode()
    return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")


class FontStore:
    """Deduplicates font files across screens by content hash."""

    def __init__(self, folder: Path):
        self.folder = folder
        self.by_hash: dict[str, str] = {}

    def add(self, data: bytes, family: str, subset: str) -> str:
        digest = hashlib.sha256(data).hexdigest()
        if digest not in self.by_hash:
            name = f"{slugify(family)}-{slugify(subset) or 'font'}.woff2"
            if name in self.by_hash.values():
                name = name.replace(".woff2", f"-{digest[:6]}.woff2")
            (self.folder / name).write_bytes(data)
            self.by_hash[digest] = name
        return self.by_hash[digest]


def convert_screen(index: int, screen_html: str, out_dir: Path, fonts: FontStore) -> tuple[str, str]:
    bundle = read_bundle(screen_html)
    manifest: dict = bundle["manifest"]  # type: ignore[assignment]
    template: str = bundle["template"]  # type: ignore[assignment]
    title = re.search(r"<title>(.*?)</title>", template, re.S).group(1).strip()

    # Fonts: keep only @font-face rules whose source is bundled locally.
    def font_rule(match: re.Match) -> str:
        comment, rule = match.group(1) or "", match.group(2)
        src = re.search(r'src:\s*url\("?([^")]+)"?\)', rule).group(1)
        if src not in manifest:
            return ""  # remote subset (cyrillic, vietnamese, ...) - not needed for Polish
        family = re.search(r"font-family:\s*'([^']+)'", rule).group(1)
        subset_match = re.search(r"/\*\s*(.*?)\s*\*/", comment)
        subset = subset_match.group(1) if subset_match else ""
        name = fonts.add(decode_resource(manifest[src]), family, subset)
        return f"/* {subset} */\n" + rule.replace(match.group(3), f'url("assets/fonts/{name}")')

    template = re.sub(
        r"(/\*[^*]*\*/\s*)?(@font-face\s*\{[^}]*?src:\s*(url\([^)]*\))[^}]*\})",
        font_rule,
        template,
    )
    template = re.sub(r"/\*[^*]*\*/\s*(?=/\*|</style>)", "", template)  # comments left without a rule

    # Images.
    def image(match: re.Match) -> str:
        uuid = match.group(1)
        entry = manifest[uuid]
        ext = entry["mime"].split("/")[-1]
        name = f"{index:02d}-manekin.{ext}"
        (out_dir / "assets" / "img" / name).write_bytes(decode_resource(entry))
        return f'src="assets/img/{name}"'

    template = re.sub(r'src="([0-9a-f-]{36})"', lambda m: image(m) if m.group(1) in manifest and manifest[m.group(1)]["mime"].startswith("image/") else m.group(0), template)

    # Bundler runtime and component scripts are not needed for a static view.
    template = re.sub(r'<script src="[0-9a-f-]{36}"></script>\s*', "", template)
    template = re.sub(r'<script type="text/x-dc".*?</script>\s*', "", template, flags=re.S)

    # Move <helmet> styles into <head>, drop custom wrapper tags.
    helmet = re.search(r"<helmet>(.*?)</helmet>", template, re.S)
    styles = helmet.group(1).strip() if helmet else ""
    template = re.sub(r"<helmet>.*?</helmet>\s*", "", template, flags=re.S)
    template = template.replace("</head>", f'<meta name="viewport" content="width=390">\n{styles}\n</head>', 1)
    template = re.sub(r"</?x-dc>\s*", "", template)
    template = re.sub(r"<sc-if[^>]*>|</sc-if>", "", template)  # conditional blocks: shown (default true)
    template = template.replace("sc-camel-view-box=", "viewBox=")

    leftovers = re.findall(r"\{\{.*?\}\}|sc-[a-z-]+=|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", template)
    if leftovers:
        raise SystemExit(f"Screen {index} ({title}): unconverted fragments: {sorted(set(leftovers))[:5]}")

    file_name = f"{index:02d}-{slugify(title)}.html"
    (out_dir / file_name).write_text(template, encoding="utf-8")
    return file_name, title


def main() -> None:
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    source, out_dir = Path(sys.argv[1]), Path(sys.argv[2])
    (out_dir / "assets" / "fonts").mkdir(parents=True, exist_ok=True)
    (out_dir / "assets" / "img").mkdir(parents=True, exist_ok=True)

    outer = read_bundle(source.read_text(encoding="utf-8"))
    manifest: dict = outer["manifest"]  # type: ignore[assignment]
    order: list[str] = outer["page_order"]  # type: ignore[assignment]
    fonts = FontStore(out_dir / "assets" / "fonts")

    for index, uuid in enumerate(order, start=1):
        screen_html = decode_resource(manifest[uuid]).decode("utf-8")
        file_name, title = convert_screen(index, screen_html, out_dir, fonts)
        print(f"{file_name}\t{title}")
    print("fonts:", ", ".join(sorted(fonts.by_hash.values())))


if __name__ == "__main__":
    main()
