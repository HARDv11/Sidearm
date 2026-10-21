#!/bin/bash
# Makes the files for a GitHub release from build/Sidearm (run ./build.sh and tests/run.sh first):
#   build/release/Sidearm-<ver>-en.zip      English: the build plus defaults.json {"language":"en"}
#   build/release/Sidearm-<ver>-ua.zip      Ukrainian: the build plus defaults.json {"language":"uk"}
#   build/release/*.sha256                  a checksum for each, for the updater (attach them too)
# The updater takes either zip (it skips defaults.json; your language is in data\settings.json).
# No spaces in the names: GitHub turns them into dots.
# The source code needs no zip of ours: GitHub attaches "Source code (zip / tar.gz)" to every release by itself.
# Both zips have a Sidearm/ folder at the root and never contain data/.
set -e
cd "$(dirname "$0")/.."
[ -f build/Sidearm/Sidearm.exe ] || { echo "build first: ./build.sh"; exit 1; }
VER=$(sed -n 's/.*AssemblyInformationalVersion("\([^"]*\)").*/\1/p' src/Program.cs)
OUT=build/release
TMP=$(mktemp -d)
rm -rf "$OUT" && mkdir -p "$OUT" "$TMP/own/Sidearm" "$TMP/share/Sidearm"
cp -r build/Sidearm/. "$TMP/own/Sidearm/"
cp -r build/Sidearm/. "$TMP/share/Sidearm/"
rm -rf "$TMP/own/Sidearm/data" "$TMP/share/Sidearm/data"
printf '{"language":"en"}\n' > "$TMP/own/Sidearm/defaults.json"
printf '{"language":"uk"}\n' > "$TMP/share/Sidearm/defaults.json"
(cd "$TMP/own" && zip -qr -X "$OLDPWD/$OUT/Sidearm-$VER-en.zip" Sidearm)
(cd "$TMP/share" && zip -qr -X "$OLDPWD/$OUT/Sidearm-$VER-ua.zip" Sidearm)
(cd "$OUT" && for z in "Sidearm-$VER-en.zip" "Sidearm-$VER-ua.zip"; do sha256sum "$z" > "$z.sha256"; done)
rm -rf "$TMP"
if unzip -l "$OUT/Sidearm-$VER-en.zip" "Sidearm/data/*" >/dev/null 2>&1 || unzip -l "$OUT/Sidearm-$VER-ua.zip" "Sidearm/data/*" >/dev/null 2>&1; then
  echo "data/ ended up in a zip: stop"; exit 1
fi
echo "release files for v$VER in $OUT:"
ls -l "$OUT" | tail -n +2
