#!/usr/bin/env bash
# Renders every docs/makiety/NN-*.html to a 390x844 PNG next to it (Chrome headless).
# Usage: tools/scripts/render_mockups.sh [dir]   (default: docs/makiety)
set -euo pipefail
DIR="$(cd "${1:-docs/makiety}" && pwd)"
CHROME="${CHROME:-google-chrome}"
PROFILE="$(mktemp -d)"
trap 'rm -rf "$PROFILE"' EXIT

for html in "$DIR"/[0-9][0-9]-*.html; do
  png="${html%.html}.png"
  "$CHROME" --headless=new --disable-gpu --no-sandbox --hide-scrollbars \
    --user-data-dir="$PROFILE" --allow-file-access-from-files \
    --force-device-scale-factor=1 --window-size=390,844 \
    --virtual-time-budget=4000 --screenshot="$png" "file://$html" >/dev/null 2>&1
  echo "$(basename "$png")"
done
