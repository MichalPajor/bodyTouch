#!/usr/bin/env python3
"""Builds an HTML overview of the model renders and the zone legend (for stage reviews).

Usage: python3 -I tools/scripts/zones_overview.py screens/etap-02
Needs zone_colors.json and the renders from tools/blender/render_previews.py in that folder,
tools/blender/zones.json and tools/blender/body_report.json. Writes przeglad.html next to the renders.
"""
import html
import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]


def legend(zones, colors, counts, level):
    rows = []
    for z in zones:
        if z["level"] != level:
            continue
        hidden = "" if z.get("drawable", True) else " <em>(nie losowana)</em>"
        count = counts.get(z["id"], "")
        rows.append(
            f'<li><span class="sw" style="background:{colors[z["id"]]}"></span>'
            f'<b>{html.escape(z["namePl"])}</b>{hidden}<small>{html.escape(z["id"])}'
            f'{f" · {count} tr." if count else ""}</small></li>')
    return "\n".join(rows)


def main():
    out = Path(sys.argv[1])
    zones = json.loads((REPO / "tools/blender/zones.json").read_text(encoding="utf-8"))["zones"]
    colors = json.loads((out / "zone_colors.json").read_text(encoding="utf-8"))
    report = json.loads((REPO / "tools/blender/body_report.json").read_text(encoding="utf-8"))
    medium_counts = report.get("triangles_per_zone", {})
    easy_counts = {}
    for z in zones:
        if z["level"] == "Medium" and z["id"] in medium_counts:
            easy_counts[z["parentId"]] = easy_counts.get(z["parentId"], 0) + medium_counts[z["id"]]

    def row(prefix):
        return "".join(f'<img src="{prefix}_{v}.png" alt="{prefix} {v}">' for v in ("front", "side", "back"))

    page = f"""<!doctype html><html lang="pl"><head><meta charset="utf-8"><title>Model i strefy – etap 2</title>
<style>
body{{margin:0;padding:24px;background:#0D0A1A;color:#F4F2FF;font:14px/1.4 system-ui,sans-serif;width:1720px}}
h1{{font-size:22px;margin:0 0 4px}} h2{{font-size:17px;margin:28px 0 10px;color:#FF9A3C}}
p{{color:#B3ACD6;margin:0 0 12px}}
.row{{display:flex;gap:8px}} .row img{{width:300px;background:#171329;border:1px solid #2E2650}}
.close img{{width:220px}}
.grid{{display:grid;grid-template-columns:920px 1fr;gap:24px;align-items:start}}
ul{{list-style:none;margin:0;padding:0;columns:2;column-gap:16px}}
li{{display:flex;align-items:center;gap:8px;padding:3px 0;break-inside:avoid}}
li small{{color:#B3ACD6;margin-left:auto;padding-left:8px;white-space:nowrap}}
.sw{{width:18px;height:18px;flex:none;border:1px solid #2E2650}} em{{color:#B3ACD6;font-style:normal}}
</style></head><body>
<h1>Body Touch Game – model 3D i strefy (etap 2)</h1>
<p>Wzrost {report["height_cm"]} cm · {report["triangles"]} trójkątów ciała + {report["eyes_triangles"]} oczy ·
odstęp ręka–tułów/uda min. {report["min_gap_arm_to_torso_or_thigh_cm"]} cm · źródło: MakeHuman (CC0)</p>
<h2>Model</h2><div class="row">{row("model")}</div>
<div class="row close" style="margin-top:8px">{"".join(f'<img src="model_{v}.png" alt="{v}">' for v in ("face", "hand", "hand_back", "foot"))}</div>
<h2>Strefy – poziom Easy ({sum(1 for z in zones if z["level"] == "Easy")})</h2>
<div class="grid"><div class="row">{row("zones_easy")}</div><ul>{legend(zones, colors, easy_counts, "Easy")}</ul></div>
<h2>Strefy – poziom Medium ({sum(1 for z in zones if z["level"] == "Medium")})</h2>
<div class="grid"><div class="row">{row("zones_medium")}</div><ul>{legend(zones, colors, medium_counts, "Medium")}</ul></div>
</body></html>"""
    (out / "przeglad.html").write_text(page, encoding="utf-8")
    print(out / "przeglad.html")


if __name__ == "__main__":
    main()
