#!/usr/bin/env bash
# Regenerates the mannequin and all body data files.
#   bash tools/blender/build_all.sh            model + export + data files
#   bash tools/blender/build_all.sh --renders  additionally renders review images to screens/<dir>
#   bash tools/blender/build_all.sh --export-only   skip build_model.py (use after manual edits in body.blend)
set -euo pipefail
cd "$(dirname "$0")/../.."
BLENDER="${BLENDER:-$HOME/tools/blender-4.5.16-linux-x64/blender}"
RENDERS_DIR="${RENDERS_DIR:-screens/etap-02}"

if [[ " $* " != *" --export-only "* ]]; then
  "$BLENDER" -b --python tools/blender/build_model.py -- --out tools/blender/body.blend
fi
"$BLENDER" -b tools/blender/body.blend --python tools/blender/export_assets.py
dotnet run --project tools/BodyDataTool -v:q -- build

if [[ " $* " == *" --renders "* ]]; then
  "$BLENDER" -b tools/blender/body.blend --python tools/blender/render_previews.py -- --out "$RENDERS_DIR"
  python3 -I tools/scripts/zones_overview.py "$RENDERS_DIR"
fi
