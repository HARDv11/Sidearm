#!/bin/bash
# Runs Sidearm's tests on Linux. Needs node, Mono (mcs + mono), and for the UI test playwright-core + a Chromium.
#   tests/run.sh          everything except the UI screenshots
#   tests/run.sh ui       also the UI test (English and Ukrainian), screenshots land in tests/ui/shots/
set -e
cd "$(dirname "$0")/.."
ROOT=$PWD
echo "== command bar (query.js)"
node tests/query.test.js | tail -1
node tests/money-timer.test.js | tail -1

[ -f build/Sidearm/Sidearm.exe ] || ./build.sh >/dev/null
W=$(mktemp -d)
cp build/Sidearm/Sidearm.exe build/Sidearm/*.dll "$W"/
echo "== clean links"
mcs -nologo src/CleanLinks.cs tests/shell/CleanLinksTest.cs -out:"$W/CleanLinksTest.exe" 2>&1 | grep -v warning || true
mono "$W/CleanLinksTest.exe" | tail -1
for t in TranslateUrlTest LangTest ShellTest MigrateTest UpdaterTest RavenTest; do
  mcs -nologo -r:"$W/Sidearm.exe" -r:System.Core.dll -r:System.Drawing.dll -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll -r:System.Web.Extensions.dll \
    tests/shell/$t.cs -out:"$W/$t.exe" 2>&1 | grep -v warning || true
done
cd "$W"
echo "== translate URLs";  mono TranslateUrlTest.exe | tail -1
echo "== Ukrainian strings"; SIDEARM_UI="$ROOT/ui" mono LangTest.exe
echo "== shell logic";     SIDEARM_UI="$ROOT/ui" mono ShellTest.exe | tail -1
echo "== bringing over an earlier copy"
O=mig/Downloads/Sidearm-0.6.1-share-uk/Sidearm/data
mkdir -p $O/webview/EBWebView/Default/Cache $O/webview/EBWebView/Default/Network mig/Downloads/Sidearm-0.7.0-share-uk/Sidearm/data
echo '{}' > $O/settings.json; echo c > $O/webview/EBWebView/Default/Network/Cookies; echo j > $O/webview/EBWebView/Default/Cache/x; echo l > $O/sidearm.log
mono MigrateTest.exe
echo "== updates (check, unpack, swap, roll back)"
mono UpdaterTest.exe
echo "== Raven's files and settings"
mono RavenTest.exe | tail -1
cd "$ROOT"
# Raven end to end needs the post office: a local one from server/ (wrangler dev), if it can start
echo "== Raven end to end (local post office)"
PO=""
if [ -d server/node_modules ]; then
  (cd server && npx wrangler dev --local --port 8787 >"$W/wrangler.log" 2>&1 & echo $! > "$W/wrangler.pid")
  for i in $(seq 1 40); do curl -s -m 1 http://127.0.0.1:8787/ >/dev/null 2>&1 && PO=1 && break; sleep 0.5; done
fi
if [ -n "$PO" ]; then node tests/raven.test.mjs http://127.0.0.1:8787 | tail -1; node server/test/smoke.mjs http://127.0.0.1:8787 | tail -1
else echo "skipped: no local post office (cd server && npm install)"; fi
if [ "$1" = "ui" ]; then
  echo "== UI (headless Chromium)"
  (cd ui && python3 -m http.server 8765 --bind 127.0.0.1 >/dev/null 2>&1 & echo $! > "$W/http.pid")
  sleep 1
  (cd tests/ui && [ -d node_modules ] || npm install --silent)
  node tests/ui/ui-test.js en | grep '"errors"'
  node tests/ui/ui-test.js uk | grep '"errors"'
  node tests/ui/raven-ui-test.js en | grep '"errors"'
  node tests/ui/raven-ui-test.js uk | grep '"errors"'
  node tests/ui/raven-ui-test.js en light | grep '"errors"'
  kill "$(cat "$W/http.pid")" 2>/dev/null || true
fi
[ -f "$W/wrangler.pid" ] && kill "$(cat "$W/wrangler.pid")" 2>/dev/null
rm -rf "$W"
