#!/usr/bin/env bash
# Replaces the app database on the emulator with a seeded one (tools/DevSeed). Debug build only (run-as).
# Usage: tools/scripts/push_seed_db.sh <file.db3>
set -euo pipefail
DB="${1:?Podaj plik bazy (.db3)}"
ADB="${ANDROID_HOME:-$HOME/Android/Sdk}/platform-tools/adb"
PACKAGE="com.example.bodytouchgame"

"$ADB" shell am force-stop "$PACKAGE"
"$ADB" push "$DB" /data/local/tmp/seed.db3 >/dev/null
"$ADB" shell run-as "$PACKAGE" sh -c "'rm -f files/bodytouch.db3 files/bodytouch.db3-wal files/bodytouch.db3-shm; cp /data/local/tmp/seed.db3 files/bodytouch.db3'"
"$ADB" shell rm /data/local/tmp/seed.db3
"$ADB" shell monkey -p "$PACKAGE" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1
echo "Baza testowa wgrana, aplikacja uruchomiona."
