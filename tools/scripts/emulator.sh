#!/usr/bin/env bash
# Starts the project's Android emulator (AVD "btg_pixel5") headless.
# The emulator needs libpulse and audio codecs, which are not installed system-wide
# on this machine; they are unpacked locally in ~/tools/emu-libs (see docs/uruchamianie.md).
#
# Usage: tools/scripts/emulator.sh [--window]   (default: no window, waits until boot completes)
set -euo pipefail

export ANDROID_HOME="${ANDROID_HOME:-$HOME/Android/Sdk}"
AVD="${BTG_AVD:-btg_pixel5}"
LIBS="$HOME/tools/emu-libs/root/usr/lib/x86_64-linux-gnu"
export LD_LIBRARY_PATH="$LIBS:$LIBS/pulseaudio${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
ADB="$ANDROID_HOME/platform-tools/adb"

if "$ADB" devices | grep -q '^emulator-'; then
  echo "Emulator already running."; exit 0
fi

ARGS=(-avd "$AVD" -no-snapshot-save -no-boot-anim -gpu swiftshader_indirect -no-audio)
if [[ "${1:-}" != "--window" ]]; then ARGS+=(-no-window); fi

nohup "$ANDROID_HOME/emulator/emulator" "${ARGS[@]}" >"${TMPDIR:-/tmp}/btg-emulator.log" 2>&1 &
echo "Emulator starting (log: ${TMPDIR:-/tmp}/btg-emulator.log)..."

"$ADB" wait-for-device
until [[ "$("$ADB" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" == "1" ]]; do sleep 2; done
"$ADB" shell settings put global window_animation_scale 0 >/dev/null
"$ADB" shell settings put global transition_animation_scale 0 >/dev/null
"$ADB" shell settings put global animator_duration_scale 0 >/dev/null
echo "Emulator ready."
