#!/bin/bash
# Builds Sidearm.exe for .NET Framework 4.8 (x64) with Mono's C# compiler.
# Needs: mcs (Mono), llvm-rc (version info, icon and manifest), python3 with pefile (build time and checksum).
set -e
cd "$(dirname "$0")"
OUT=build/Sidearm
rm -rf "$OUT" && mkdir -p "$OUT" build/res
VERSION=$(sed -n 's/.*AssemblyInformationalVersion("\([^"]*\)").*/\1/p' src/Program.cs)
VERSION4=$(sed -n 's/.*AssemblyFileVersion("\([^"]*\)").*/\1/p' src/Program.cs)
VERSIONC=${VERSION4//./,}
# Windows resources: the icon, version details (publisher, copyright) and an application manifest
sed "s/@VERSION4@/$VERSION4/g" assets/Sidearm.exe.manifest > build/res/Sidearm.exe.manifest.out
sed -e "s/@VERSION4@/$VERSION4/g" -e "s/@VERSIONC@/$VERSIONC/g" -e "s/@VERSION@/$VERSION/g" assets/sidearm.rc.in > build/res/sidearm.rc
cp assets/sidearm.ico build/res/
(cd build/res && llvm-rc -no-preprocess -c 1252 -fo sidearm.res sidearm.rc)
REF=/usr/lib/mono/4.5
mcs -nologo -sdk:4.5 -platform:x64 -target:winexe -optimize+ -warn:4 \
  -out:"$OUT/Sidearm.exe" -win32res:build/res/sidearm.res \
  -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Web.Extensions.dll -r:System.Core.dll -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll \
  -r:lib/Microsoft.Web.WebView2.Core.dll -r:lib/Microsoft.Web.WebView2.WinForms.dll \
  src/*.cs
python3 assets/pe_stamp.py "$OUT/Sidearm.exe"
cp lib/Microsoft.Web.WebView2.Core.dll lib/Microsoft.Web.WebView2.WinForms.dll lib/WebView2Loader.dll "$OUT/"
cp assets/Sidearm.exe.config assets/sidearm.ico assets/README.txt "$OUT/"
cp -r ui "$OUT/ui"
# Filter lists download on first launch (data\filters). They aren't shipped: a list of bad domains inside a
# download makes virus scanners nervous, and a fresh copy is better anyway.
echo "built $OUT ($VERSION)"
