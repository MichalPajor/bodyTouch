#!/usr/bin/env bash
# Builds the Debug app, deploys it to the running emulator (Fast Deployment) and launches it.
# A Debug .apk installed with plain `adb install` crashes on start ("No assemblies found"):
# with Fast Deployment the assemblies are pushed separately by the Install target.
set -euo pipefail
cd "$(dirname "$0")/../.."
ADB="${ANDROID_HOME:-$HOME/Android/Sdk}/platform-tools/adb"
PACKAGE="com.example.bodytouchgame"

dotnet build src/BodyTouchGame.App -f net10.0-android -t:Install -nologo -v:q
"$ADB" shell am force-stop "$PACKAGE"
"$ADB" shell monkey -p "$PACKAGE" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1
echo "Uruchomiono $PACKAGE na emulatorze."
