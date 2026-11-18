#!/usr/bin/env bash
# Builds a standalone Release .apk (all ABIs, works on a phone without a PC) and copies it to dist/.
# Usage: tools/scripts/build_apk.sh <name>     e.g. tools/scripts/build_apk.sh etap-03
set -euo pipefail
cd "$(dirname "$0")/../.."
NAME="${1:?Podaj nazwę, np. etap-03}"
BUILD_ROOT="${BTG_BUILD_ROOT:-$HOME/build/BodyTouchGame}"

dotnet publish src/BodyTouchGame.App -f net10.0-android -c Release -nologo -v:q
mkdir -p dist
cp "$BUILD_ROOT/bin/BodyTouchGame.App/Release/net10.0-android/publish/place.yourmoment.bodytouchgame-Signed.apk" \
   "dist/BodyTouchGame-$NAME.apk"
ls -la "dist/BodyTouchGame-$NAME.apk"
