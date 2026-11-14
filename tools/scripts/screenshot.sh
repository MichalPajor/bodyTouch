#!/usr/bin/env bash
# Saves a PNG screenshot of the running emulator (or the only connected device).
# Usage: tools/scripts/screenshot.sh screens/etap-NN/nazwa.png
set -euo pipefail
OUT="${1:?Podaj ścieżkę pliku PNG}"
ADB="${ANDROID_HOME:-$HOME/Android/Sdk}/platform-tools/adb"
mkdir -p "$(dirname "$OUT")"
"$ADB" exec-out screencap -p >"$OUT"
echo "Zapisano $OUT"
