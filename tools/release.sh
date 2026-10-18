#!/bin/bash
# Makes the files for a GitHub release from build/Sidearm (run ./build.sh and tests/run.sh first):
#   build/release/Sidearm-<ver>.zip                  the build plus source/ (src, assets, build.sh)
#   build/release/Sidearm-<ver>-share-uk.zip         the build plus defaults.json {"language":"uk"}: what the updater installs
#   build/release/Sidearm-<ver>-share-uk.zip.sha256  its checksum, for the updater (attach it too)
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
mkdir -p "$TMP/own/Sidearm/source" && cp -r src assets build.sh "$TMP/own/Sidearm/source/"
printf '{"language":"uk"}\n' > "$TMP/share/Sidearm/defaults.json"
(cd "$TMP/own" && zip -qr -X "$OLDPWD/$OUT/Sidearm-$VER.zip" Sidearm)
(cd "$TMP/share" && zip -qr -X "$OLDPWD/$OUT/Sidearm-$VER-share-uk.zip" Sidearm)
(cd "$OUT" && sha256sum "Sidearm-$VER-share-uk.zip" > "Sidearm-$VER-share-uk.zip.sha256")
rm -rf "$TMP"
if unzip -l "$OUT/Sidearm-$VER.zip" "Sidearm/data/*" >/dev/null 2>&1 || unzip -l "$OUT/Sidearm-$VER-share-uk.zip" "Sidearm/data/*" >/dev/null 2>&1; then
  echo "data/ ended up in a zip: stop"; exit 1
fi
echo "release files for v$VER in $OUT:"
ls -l "$OUT" | tail -n +2
