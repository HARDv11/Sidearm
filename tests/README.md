# Tests

Sidearm's shell can only run on Windows (WebView2), so these tests cover everything around it on Linux. That means the command-bar logic, the C# logic loaded by reflection, and the HTML/JS pages in headless Chromium with a fake `chrome.webview`.

```bash
tests/run.sh       # query.js, clean links, translate URLs, Ukrainian strings, shell logic, migration
tests/run.sh ui    # + the UI test in English and Ukrainian (screenshots in tests/ui/shots/)
```

Needs `node`, Mono (`mcs`, `mono`) and the build tools from `build.sh`. The UI test also needs `playwright-core` (installed on first run) and a Chromium. It looks in `/opt/pw-browsers/chromium` unless you set `CHROMIUM=/path/to/chrome`. The UI test serves `ui/` on `http://127.0.0.1:8765` because localStorage needs a real origin.

| File | What it checks |
| --- | --- |
| `query.test.js` | Prefixes, calculator, units, URL detection, notes and the wrong-layout fix (63 cases) |
| `money-timer.test.js` | `100 usd`, `50 € in pln`, `timer 5 min boss`, `timer off`… (28 cases) |
| `shell/CleanLinksTest.cs` | Tracking parameters removed, redirects unwrapped |
| `shell/TranslateUrlTest.cs` | `TranslateUrl` / `OriginalOf` round trips |
| `shell/LangTest.cs` | Ukrainian lookups through `Lang.T` (prints pairs; `?` marks are only the console's encoding) |
| `shell/ShellTest.cs` | Settings validation, `VideoEmbedUrl`, `TranslateBatch` with a fake Google, NBU/ER-API parsing, the chime WAV, `Clock` |
| `shell/MigrateTest.cs` | A new copy finds an older `data` folder and copies it without caches or logs |
| `shell/UpdaterTest.cs` | Version compare, reading GitHub's release JSON (foreign links refused), SHA-256 check, zip paths that escape, swapping files in with `.old` copies, rollback after failed starts, confirm, quitting early not counting (0.9.5) |
| `shapes.test.js` | 0.9.5: shapes drawn by a shaky hand (gestures and the magic pen); straight flicks must not be shapes |
| `shell/DrawTest.cs` | 0.9.5: drawings on disk, Raven's picture files, your own gestures, the gesture migration, joystick and box-color settings |
| `ui/draw-ui-test.js` | 0.9.5: the drawing page (every tool, save, send) and the page script's screenshot picker, Alt + drag box, joystick and shape gestures |
| `ui/ui-test.js` | Strip (tabs patched in place: zero DOM mutations over 20 busy updates), palette (money, timer), shield panel, quick bar, settings sections, in-place translation in `inject.js`, field manual, `pop.html` |

Screenshots of the UI test are the real check for layout: look at them after any CSS change.
