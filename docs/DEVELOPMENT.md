# Sidearm — developer guide

Sidearm is a pocket browser for Windows that you draw over a borderless game with Alt + `, use for ten seconds, and holster. This guide covers the architecture, the message protocol, settings, the gotchas, testing and the changelog: read it before touching the code.

## What it is

Sidearm is a small, always-on-top browser window for quick lookups mid-game. It doesn't replace your main browser: it's what you reach for when opening that browser would mean alt-tabbing out of a match. Tagline, used on the start page and tray: *Switching to your sidearm is faster than opening a browser.*

It is a C# WinForms shell around Microsoft's WebView2 (the Chromium engine built into Windows), with every visible surface written in plain HTML, CSS and JavaScript. **Current version: 0.8.1 · Resupply.**

**Non-negotiables**

- **Light first.** Sleep when holstered, free what isn't in use, and count every MB. A little extra memory is fine for real speed, never bloat.
- **Out of the game's way.** Draw and holster in one key, no focus stealing, nothing global that a game might need except the draw key (and Alt + G only while Sidearm is on screen).
- **Keyboard-first, mouse-friendly.** Every feature has a key, and the start page's Field manual lists all of them. A new feature isn't done until it's in the Field manual.
- **Restylable.** Colors live in CSS variables; a non-programmer can repaint `ui\sidearm.css`.
- **Honest.** Pages are untrusted; Sidearm never ships closed-source code (it loads the user's own copy of Modern for Wikipedia); no password manager (logins already persist in the profile).
- **Ukrainian first.** The default translation target is Ukrainian; no Russian services (Yandex was removed for this reason).

## Quick start

There is no .NET SDK or NuGet in the build, so it uses Mono's C# compiler against .NET Framework 4.8, which ships with Windows 10 and 11. Building works on Linux; running needs Windows.

1. **Build.** `./build.sh` in the project root runs `mcs -sdk:4.5 -platform:x64 -target:winexe` over `src/*.cs` and copies the DLLs, `ui/` and `assets/README.txt` into `build/Sidearm/`. A clean build prints only `built build/Sidearm`. It needs `mcs` (Mono), `llvm-rc` and Python 3 with `pefile`.
2. **Test.** `tests/run.sh` (add `ui` for headless screenshots; see Testing below).
3. **Package.** `tools/release.sh` puts the release files in `build/release/` (see Releases and updates below):
   - `Sidearm-<ver>-en.zip`: English, the build plus a `defaults.json` holding `{"language":"en"}`;
   - `Sidearm-<ver>-ua.zip`: Ukrainian, the build plus `{"language":"uk"}`;
   - a `.sha256` checksum for each.
   The updater takes either zip: it skips `defaults.json`, and each copy's language lives in `data\settings.json`. No spaces in the names (GitHub turns them into dots).
   The source code needs no zip of ours: GitHub attaches "Source code (zip / tar.gz)" to every release by itself.
4. **Never include `data\`** in a zip or a commit: it holds cookies and sign-ins. The script refuses if it finds one.

**Read the log after a release.** `data\sidearm.log` logs one line per event, and every version logs its name on the `starting` line, so you can tell which build produced which errors.

**WebView2 SDK.** `lib/` holds `Microsoft.Web.WebView2.Core.dll`, `.WinForms.dll` and `WebView2Loader.dll` (SDK 1.0.3856.49). Tested against WebView2 runtime 155.0.

## Architecture

*[Architecture diagram: shell (MainForm) hosting the strip view, tab views, Glance; pages talk to the shell through web messages]*

The shell is the only thing that touches Windows, the disk or the network on Sidearm's behalf. The views draw and report, and pages are treated as untrusted: they can only ask for a short whitelist of actions (see Message protocol).

**Life of a draw.** The draw key arrives as `WM_HOTKEY`. The shell remembers the game's window (for ghost mode), shows itself, resumes the suspended views and focuses the bar. It also starts a spare tab engine 1.5 s later. Esc or a click back into the game holsters: views are hidden and suspended 10 s later, the spare is thrown away, and tabs unload after 15 minutes. Pressing the hotkey twice within 450 ms searches the clipboard.

**Life of a page.** `NavigationStarting` resets the tab's counters and timer. `WebResourceRequested` runs every request through `FilterEngine.ShouldBlock`. The page script sends its classes and ids for cosmetic hiding and reports its language. `NavigationCompleted` logs the load time and Sidearm's share of it.

**Two more windows since 0.7.** The quick bar (`QuickBar`, `ui/quick.html`) and the video pop-out (`VideoWindow`) are top-level windows with their own web views in the same engine environment, so they share cookies, logins and recent searches. The quick bar is created on first use, suspended 30 s after it hides and dropped with the tabs after a long holster; the pop-out lives until it's closed, through holsters.

## Repository layout

The shell is one `MainForm` split into partial classes by concern. The UI is static files served from `https://sidearm.example/` (a WebView2 virtual host mapped to `ui\`).

| Path | What lives there |
| --- | --- |
| `src/Program.cs` | Entry point, version string and assembly details, single instance (mutex + `Local\Sidearm.Draw` event), data folder (`AppDir\data`, else `%LOCALAPPDATA%\Sidearm`), WebView2 runtime check, `--tray` start |
| `src/MainForm.cs` | Window, init, draw / holster (`DrawAsync`), sleep and unload, hotkeys, `HandleShortcut` (modifiers from the key event, no keyboard polling), UI-strip messages (`OnUiMessage`), layout, native window move, tray, quit |
| `src/MainForm.Tabs.cs` | `Tab` class, open / activate / close / recycle (LRU at `maxTabs`), session save and restore, view creation and `ConfigureContent`, page messages (`OnContentMessage`), context menu, per-site zoom, `PushTabs` |
| `src/MainForm.Pages.cs` | Ad blocking hooks and timing, reader, fog, per-site options, `PageInfo`, the injected page script and its config, settings page backend, `ApplySetting` |
| `src/MainForm.Theme.cs` | Light / dark / system, bottom tabs view, `GoHome`, `CurrentPageUrl` |
| `src/MainForm.Glance.cs` | Glance panel, image requests (copy, save, pop out, reverse search, WebP fallback) |
| `src/MainForm.Feel.cs` | Ghost mode, sound, gestures, clean-link copy, quick copy, amnesia |
| `src/MainForm.Handoff.cs` | Send to a browser (Zen, Chrome, Edge…, private flags, fallback to the default browser), Ask AI providers, Gemini fill-in |
| `src/MainForm.Extras.cs` | Translate (page proxy), accent on the window frame, load timing, spare tab, warm start, background memory level |
| `src/MainForm.Tools.cs` | 0.7: quick bar glue and its hotkey, timers, exchange-rate push, video pop-out, in-place translation batches, uBlock Origin Lite switching |
| `src/QuickBar.cs`, `src/VideoWindow.cs` | The quick bar window (borderless, topmost, hides on focus loss and hands focus back) and the video pop-out window (tool window, opacity in its system menu) |
| `src/MainForm.Paint.cs` | 0.8: makes sure the strip paints after a draw (`NudgeAfterShow`, the `painted` handshake, `KickView`) |
| `src/MainForm.Home.cs` | 0.8: the start page's notes (`data\notes.txt`, `AppendNote` for `n …`) and pinned sites (`data\pins.json`, `PinPage`) |
| `src/Shortcuts.cs` | 0.8.1: `Sidearm.lnk` on the desktop and in Start (IShellLink), and moving them along when a new copy starts in another folder. Sidearm is a tool window with no taskbar button, so these are how it gets pinned |
| `src/CrashCheck.cs` | 0.8.1: a `running.txt` note in `data\` while Sidearm runs; if it's still there at the next start, Windows' crash record (Application Error / .NET Runtime) is copied into the log |
| `src/Updater.cs`, `src/MainForm.Update.cs` | 0.8: update check against GitHub Releases, download + SHA-256 check, file swap with `.old` copies, rollback, and the toast / Settings glue |
| `src/Rates.cs`, `src/Chime.cs`, `src/Ublock.cs` | NBU exchange rates with a fallback and a disk cache; the in-memory timer chime; uBlock Origin Lite download, unpack and enable |
| `src/FilterEngine.cs`, `Adblock.cs` | ABP-syntax filter engine (host fast path, token index, cosmetic rules by class / id) and list download and weekly update |
| `src/Lookup.cs` | Word look-up: dictionaryapi.dev (time-boxed), Wiktionary, Wikipedia summary, Google translate with fallbacks, `TranslateBatch` for in-place translation, cache, 40/min limit |
| `src/CleanLinks.cs` | Strips tracking parameters and unwraps redirect links |
| `src/Extensions.cs` | Finds Modern for Wikipedia in the user's browsers and loads it |
| `src/Settings.cs`, `Sites.cs`, `Native.cs`, `ImageWindow.cs` | Settings model and migrations (also `Json` and `Log` helpers), per-site choices, P/Invoke, the pop-out image window |
| `src/Lang.cs`, `ui/i18n.js`, `ui/lang/uk.js` | Interface language: one Ukrainian dictionary (exact strings, `{1}` templates, and a `page` section for `inject.js` and the reader) used by the shell and Sidearm's pages; `defaults.json` next to the exe sets first-run values for the shared zip |
| `ui/chrome.html`, `app.js`, `query.js` | The strip: command bar, palette, tabs (patched in place), ticker, timer readout, shield panel. `query.js` resolves input (prefixes, calculator, units, money, timers, URLs). `chrome.html?view=tabs` is the bottom tab strip |
| `ui/quick.html`, `quick.js` | The quick bar page (same `query.js`, shares recent searches through `sidearm.recent.v1`) |
| `ui/pop.html` | Host page for YouTube and Twitch embeds in the video pop-out (they refuse to play without a referrer or `parent`) |
| `ui/inject.js` | Runs in every page: keys, look-up card, cosmetic hiding, fog, force dark, Glance links, gestures, image viewer, page language, video detection, in-place translation |
| `ui/look.js` | Accent, gradient tint and grain from the user's colors, shared by strip, start page and settings |
| `ui/newtab.html` | Start page: pinned sites, notes and the Field manual |
| `ui/settings.html`, `settings.js` | Settings page (talks to the shell with `settings-*` messages) |
| `ui/reader.js`, `ui/vendor/` | Reader overlay around Mozilla Readability 0.6.0 |
| `ui/sidearm.css`, `ui/fonts/` | The whole theme; NV Bitter, NV Jost, Sourcerer (OFL) |
| `assets/` | Icon, exe config, README.txt (copied next to the exe), the version-resource template `sidearm.rc.in`, the app manifest, `pe_stamp.py` |
| `seed/` | Old starter filter lists, kept for offline development only: not shipped since 0.7 (`Adblock` still uses `seed\` beside the exe if one exists) |
| `tools/` | `uk_add.py` (add Ukrainian strings), `release.sh` (the zips and checksum for a GitHub release) |

## Message protocol

Everything talks through WebView2 web messages: pages call `chrome.webview.postMessage(obj)`, the shell answers with `PostWebMessageAsJson`. Strip messages use the key `type`; page messages use the key `t`. The shell checks the sender every time: the strip only from `https://sidearm.example/chrome.html`, settings messages only from `settings.html`, and page requests only from the active tab or the Glance.

**Strip → shell** (`OnUiMessage` in `MainForm.cs`)

| type | Fields | Does |
| --- | --- | --- |
| `ready` |  | Strip loaded: shell sends `init`, `tabs` and running `timers` (the bottom strip gets its own copy) |
| `layout` | `chrome`, `full` (device px), `cut` | Strip height at rest and with the palette or panel open; the shell resizes the strip view and clips it to the panel |
| `navigate` | `url`, `newTab` | Go. `https://sidearm.example/ai?q=` is the Ask AI prefix and is intercepted |
| `tab` | `op`: activate, close, new, home, reopen, cycle, move; `id`, `dir`, `to` | Tab actions |
| `nav` | `op`: back, forward, reload, stop | Navigation on the active tab |
| `page` | `op`: reader, fog, translate, popout, ublock, ads, fogAuto, dark, lookup, trAuto; `on` | Shield panel actions and per-site switches |
| `mute` | `id`, optional `on` | Mute or toggle a tab |
| `zoom` | `op`: in, out, reset | Page zoom, remembered per site |
| `glance` | `op`: promote, newtab, zen, zenPrivate, close | Glance controls in the ticker |
| `zen` | `url`, `private` | Send to the chosen browser (the name is historical) |
| `copy` | `text`, `clean` | Clipboard; `clean` runs it through `CleanLinks` |
| `timer` | `op`: start (`seconds`, `label`) or cancel (`id`, none = all) | Timers from the bar and the strip's countdown readout |
| `cmd` | `name`: reopen, settings, unmute, ghost, translate, popout | Toast actions and buttons |
| `dragStart`, `opacity`, `pin`, `holster`, `clipSearch`, `focusContent`, `log` |  | Window chores (`dragStart` is sent once the pointer has moved 3 px with the button down) |

**Shell → strip**: `init` (theme, bangs, hotkeys, browser and AI names, look, tabs position), `tabs` (tab list with audio state, page info for the shield panel, Glance), `mem`, `focusBar`, `runQuery`, `drawn`, `holstered`, `toast` (with an optional action button), `pinned`, `opacity`, `ghost`, `amnesia`, `room` (window height, so the panel never runs off the bottom).

**Page → shell** (`OnContentMessage` in `MainForm.Tabs.cs`)

| t | From | Does |
| --- | --- | --- |
| `cosmetic` | any tab | Sends the classes and ids on the page; shell answers with `cosmetic-css` hiding rules |
| `reader-size`, `img` | any tab / active or Glance | Saves reader text size; image viewer actions |
| `tr-batch` | any tab, only while in-place translation is on for it | Up to 60 texts / 6,000 characters, at most 400 batches a page; answered with `tr-result` |
| `esc`, `focusBar`, `home` | active or Glance | Holster or close Glance, jump to the bar, start page |
| `glance`, `glance-close` | active or Glance | Open or close a Glance |
| `gesture` | active or Glance | `a` = one action name from `Settings.GestureActions`; throttled |
| `pagelang` | active | Page language for translation offers |
| `hasvideo` | active | A video started playing: offers Pop out in the panel and the context menu |
| `tr-state` | active | In-place translation on / off, and whether Google stopped answering |
| `lookup` | active or Glance | Word look-up; answered with `lookup-result` |
| `claude`, `search` | active | Ask AI or search a selected word; throttled to one per 1.5 s |
| `fog-done`, `reader-none` | active | Results to show as toasts |
| `settings-get`, `-set`, `-bangs`, `-action` | settings page only | Settings changes |
| `home-get`, `notes-set`, `pins-set` | start page only (`IsStartPage`) | 0.8: read the notes and pins, save the notes box, save the pin list (cleaned: http(s) only, at most 12) |
| `lookup-off` | active | 0.8: Alt held over a word on a site where look-up is switched off; the shell offers to turn it back on |
| `yt-ad` | any tab | 0.8: the YouTube ad skipper did its thing (logged once) |

**Shell → page**: `cfg` (look-up, `siteOff`, gestures), `alt` (`down`: Alt relayed from the command bar, 0.8), `notes` / `pins` (start page), `theme`, `dim` (behind a Glance), `lookup-result`, `lookup-sel`, `lookup-cancel`, `cosmetic-css`, `fog`, `dark`, and `settings` / `settings-msg` for the settings page.

**Since 0.7.** Shell → strip also sends `timers` (`now`, `list` of `id`, `label`, `ends` in ms) and `rates` (the NBU snapshot); `init` carries `rates`, `currency` and `quickHotkey`. Shell → page adds `tr-inplace` (`on`, `to`) and `tr-result` (`id`, `texts`, or null when Google refused). The quick bar page talks to `QuickBar.cs` with `type: "quick"` and an `op`: `ready`, `layout` (`h`, device px), `go` (`url`, `newTab`), `zen`, `copy`, `timer`, `esc`, `settings`; the shell answers with `init`, `show`, `rates` and `timers`. The video pop-out takes only `esc`, plus `open` (`url`) from `pop.html`.

**Since 0.8.1.** Strip → shell also sends `away` (the pointer reached the strip); shell → page relays it as `away`.

**Since 0.8.** Strip → shell also sends `painted` (answer to `drawn`, from an animation frame), `alt` (`down`) and `note` (`text`); `cmd` takes `update` and `lookupOn`. The quick bar sends `op: "painted"` after `show` (which now carries `ox`, where the pointer is) and `op: "note"`.

The page script's config is baked in at injection: `InjectScript()` replaces `/*SIDEARM_CFG*/{}` in `inject.js` with JSON. Changing a setting re-registers the script for future pages and posts `cfg` to open ones.

## Settings and data files

Everything the user can change lives in `data\settings.json`, and the settings page edits the same file. Property names on `Settings` are lowercase on purpose because they are what the user sees in the file.

- **Adding a setting:** give it a property and a default in the constructor, then validate it in `Settings.Load`. If it has a side effect, handle it in `ApplySetting`. Finally add a row to `settings.html`. Simple controls use `data-bool`, `data-range` (+ `data-scale`), `data-num`, `data-choice`, `data-select` and `data-text`, and `settings.js` wires those up generically.
- **Migrations:** `defaultsVersion` (currently 6). When an update must change an existing user's value (new prefixes, a renamed field like `zenPath` → `browserPath`), bump `CurrentDefaults` and branch on the old number in `Load`.
- **Outside edits:** a `FileSystemWatcher` reloads the file when it's edited by hand and applies the changes live. It ignores Sidearm's own saves for 2 seconds (`Settings.LastSelfSave`).

| File in data\ | Holds |
| --- | --- |
| `settings.json` | All settings, window position, `videoBounds` of the pop-out |
| `session.json` | Open tabs and the active one (empty in amnesia mode) |
| `sites.json` | Per-site choices: `allowAds`, `autoFog`, `forceDark`, `readerSize`, `noLookup`, `autoTranslate` |
| `zoom.json` | Per-site zoom (`hoyolab.com`); per-host entries from before 0.8 still win for their host |
| `notes.txt` | 0.8: the start page's notes box (amnesia mode doesn't touch it) |
| `pins.json` | 0.8: pinned sites, `[{ title, url, icon }]`; missing = the two examples |
| `update\` | 0.8: `pending.json` while a fresh update is unconfirmed, `failed.txt` after a failed install, the download while it runs |
| `rates.json` | The last exchange rates (`date`, `source`, hryvnias per unit) |
| `filters\` | Downloaded filter lists (EasyList, EasyPrivacy, EasyList Cookie), refreshed weekly; downloaded on first start since 0.7 |
| `extensions\` | The imported copy of Modern for Wikipedia, and `ublock-lite\` when uBlock Origin Lite was picked |
| `webview\` | The browser profile: cookies, cache, logins, extension state |
| `sidearm.log` | One line per event; rolled to `.old` past 1 MB |

The strip mirrors a few values into the UI origin's localStorage for pages that load before the shell can answer: `sidearm.theme`, `.look`, `.bangs`, `.hotkey`, `.ghostkey`, `.quickkey`, `.gestures`, `.browser` and `.ai` (start page and settings read them), plus `sidearm.recent.v1` (recent searches).

## Gotchas

Each of these cost a build to learn. Read them before changing the area they touch.

**WebView2 and WinForms**

- **A visible view refuses to suspend.** WinForms doesn't tell the engine about visibility while the form is hidden, so `TrySuspendAsync` failed every time in 0.1. `SetViewVisible` sets the controller's `IsVisible` directly, through reflection on the private `_coreWebView2Controller` field. Always hide a view through it before suspending.
- **A hidden control never initializes** unless its handle exists: read `v.Handle` before `EnsureCoreWebView2Async`.
- **Keys:** accelerator keys (Ctrl or Alt combos, F-keys, Esc) reach the shell through the WinForms `KeyDown` event and `HandleShortcut`. Plain keys don't, so single-key features (Home, `/`, Esc in pages) live in `inject.js`. Reader and fog are Alt + R and Alt + F, and mute is Alt + M, so they never fire while typing.
- **Context menu:** `ContextMenuRequested` only fires if the page didn't cancel `contextmenu`. Gestures use this to swallow the menu after a stroke; on Windows, `contextmenu` arrives after `mouseup`. The built-in item named `copyLinkLocation` is swapped for a clean-link copy.
- **The strip is a separate view laid over the page.** To show the palette or panel, it reports its full height (`layout`) and the shell grows the view; anything taller than the window must scroll inside it (`room`). Report heights in device pixels (CSS px × devicePixelRatio).
- **Zero-height views don't get animation frames.** The bottom tab strip starts hidden, so its layout uses `setTimeout`, not `requestAnimationFrame`.
- **Every network request passes through the UI thread** (`WebResourceRequested`) while ad blocking is on. Keep `CheckRequest` cheap; its cost per page is measured and shown in the shield panel.
- **Connections are kept separate per site**, so warming one up from the strip doesn't help the page. Preconnect was tried and dropped.
- **Ghost mode** turns on WS_EX_LAYERED plus WS_EX_TRANSPARENT through `CreateParams` + `UpdateStyles`. Set `Opacity` first (it makes the window layered), then add click-through. Untested risk: if pages render blank under a layered window, switch to a DWM-thumbnail overlay.
- A web view can't be see-through over another web view (they're separate windows). When the strip grows for the shield panel, `ClipStrip` cuts its window region down to the bar plus the panel, so the page shows beside it. The palette is full width, so it isn't clipped.
- Translation of the interface: English stays in the code. `ui/i18n.js` translates Sidearm's own pages as they render (text nodes and title/placeholder, skipping tab titles and page content), and the shell's toasts and menus go through `Lang.T`. A new string only needs an entry in `ui/lang/uk.js`; strings with a variable part go in `templates` with {1}, {2}.
- **Never poll the keyboard.** `GetAsyncKeyState` is what keyloggers use, and with the clipboard, hotkeys and the Run key it made VBA32 call 0.6.1 `Trojan.MSIL.InfoStealer.gen`. Shortcuts take modifiers from the key event (`KeyData`, plus `Control.ModifierKeys`); the window move posts `WM_NCLBUTTONDOWN` + `HTCAPTION` after `ReleaseCapture` and lets Windows run it (snap happens on `WM_EXITSIZEMOVE`).
- **Patch the strip, don't rebuild it.** `tabs` arrives several times a second on busy pages; re-creating tabs, favicons and the reload icon each time made the strip flicker. `renderTabs` keeps one node per tab id and skips identical lists.
- **Focus checks see the engine's processes.** Clicking into a web view can deactivate the form, so "did focus leave?" means the foreground process is neither Sidearm nor one of `env.GetProcessInfos()` (the quick bar uses the same test).
- **Extensions load from their folder and keep it.** WebView2 rejects folder names starting with `_` (except `_locales`), and an extension's files can't change while it's loaded: remove it, replace the folder, add it again (`Ublock.Update`).
- **Show the window before waking its views (0.8).** A view made visible inside a hidden window may keep an empty rectangle until something forces a layout, especially with `--disable-gpu`. `DrawAsync` and `SummonQuick` now show first, then call `SetViewVisible`, then `NotifyParentWindowPositionChanged`; the strip answers `drawn` with `painted` from `requestAnimationFrame`, and no answer in 350 ms means `KickView(…, hard)` (visibility flick plus a 1 px resize).
- **The page script runs before `<html>` exists.** `document.documentElement` is null at that point: listen on `window` (0.8's first Alt relay broke the whole script this way).
- **Alt only reaches the page with keyboard focus.** After a draw the focus is in the command bar, so the strip relays Alt (`alt` message) and the page does the look-up if the pointer is over it.
- **Renaming a running exe is allowed; overwriting it isn't.** The updater renames every replaced file to `.old`, moves the new one in, and only deletes the `.old` files after the new version has run 30 s. It closes the page engine first so nothing in `ui\` is held open.
- **Never dispose a view inside an engine event, or mid-suspend (0.8.1).** Disposing a WebView2 while the engine is still busy with it (a new-window request being answered, a `TrySuspendAsync` in flight) crashed the whole process in `EmbeddedBrowserWebView.dll` with an access violation, with nothing in our log. Close views only through `RetireView`: it hides the view and takes it off the window at once, and disposes it a moment later, after any suspend has finished. A page's new window always gets a fresh engine, never the spare.
- **The engine's own link bubble can get stuck** when the pointer leaves the page for the strip (a separate window), so `IsStatusBarEnabled` is off and `inject.js` shows its own bubble; the strip sends `away` when the pointer reaches it.
- **Embeds want a parent page.** YouTube refuses an embed opened as the top page (no referrer) and Twitch needs `parent=`, so the pop-out loads them inside `ui/pop.html`.

**State and data**

- **Never overwrite the session before it was loaded.** Tabs load lazily on first draw, so quitting from the tray before drawing used to save an empty tab list (fixed in 0.6: `SaveSession` returns while `initTask` is null).
- **Sidearm saves settings.json too.** Re-reading after an outside edit must copy every property (`CopyFrom`), or new keys get lost.
- **Pages are untrusted.** Only the settings page may send `settings-*`. Page requests that open things (`claude`, `search`, `zen` gestures) are throttled, and gesture actions come from a whitelist.
- **GDI+ can't read WebP or AVIF.** Copy, save and pop-out fall back to the original file (or a Glance). Fandom thumbnails are rewritten to `format=original`.
- **Google Translate's free endpoint returns 429** under bursts: keep the browser user agent and the fallbacks in `Lookup.cs`.

**Tooling**

- **Mono, not dotnet:** `mcs` compiles C# up to about version 7 and nothing can be fetched from NuGet, so stick to the framework and the three WebView2 DLLs.
- **Headless Chromium** for UI tests needs `--headless=new` and the binary at `/opt/pw-browsers/chromium`.
- **CSS edits are dangerous:** a replace once matched the light-theme block instead of the base and wiped most of `sidearm.css`. Check counts before replacing, and keep a backup copy.
- **Ukrainian strings:** add them with `python3 tools/uk_add.py new.json` (see the script's header) rather than editing `ui/lang/uk.js` by hand.
- **Windows resources come from llvm-rc.** `mcs -win32res` takes the icon, version details and manifest from `build/res/sidearm.res`; `assets/pe_stamp.py` (needs `pefile`) then sets the build time and PE checksum, which Mono leaves at zero.

## Testing

WebView2 doesn't run on Linux, so the shell itself is only exercised on Windows. Everything else is tested on Linux before shipping.

| What | How | Expect |
| --- | --- | --- |
| Command bar logic | `tests/query.test.js` (prefixes, calculator, units, URL detection) and `tests/money-timer.test.js` (28 money and timer cases, with and without rates) against `ui/query.js` | all pass |
| Clean links | A Mono test that compiles `src/CleanLinks.cs` with 18 cases (utm, fbclid, YouTube `si`, Google and Facebook redirects, Amazon `/ref=`) | all pass |
| Shell logic by reflection | A Mono test that loads `Sidearm.exe`: `TranslateUrl` / `OriginalOf`, `Lang.T` on new strings, settings validation, `VideoEmbedUrl`, `TranslateBatch` with a fake `Lookup.Fetch`, NBU and ER-API parsing with a fake `Rates.Fetch`, the chime WAV, `Clock` | all pass |
| Filter engine, look-up parsing | Mono harnesses with the real lists and canned API responses | correct block / allow; about 0.5 s to build the engine |
| The strip, quick bar, start page, settings, page script | Playwright-core with `/opt/pw-browsers/chromium --headless=new`, serving `ui/` from `python3 -m http.server` on 127.0.0.1 (localStorage needs a real origin) with a stub `window.chrome.webview` that records `postMessage` and replays shell messages; run once in English and once with `sidearm.lang = uk` | screenshots look right; zero DOM mutations in `#tabs` over 20 busy `tabs` messages; the recorded messages match the protocol |
| The exe itself | `pefile`: resources 3, 14, 16, 24 present, version strings filled in, `verify_checksum()` true | all true |

Run everything with `tests/run.sh` (add `ui` for the headless screenshots; see `tests/README.md`). The stub is three lines: `window.__sent=[]`, a `postMessage` that pushes into it, and `addEventListener` that keeps listeners so a test can feed `init` / `tabs` / `room` messages. For `inject.js`, replace `/*SIDEARM_CFG*/{}` with a test config before adding it as an init script.

After shipping, the log is the only window into Windows behavior. New features should log one line when they do something meaningful: ghost on and off, sends to a browser, page load timing, and so on.

## Releases and updates

Since 0.8, Sidearm updates itself from this repository's **GitHub Releases**. Nothing reaches users until a release is published: pushing to `main` alone changes nothing for them.

**How a copy finds an update.** Once a day (and from Settings → General → Updates → Check now) `Updater.Check` asks `https://api.github.com/repos/HARDv11/Sidearm/releases/latest`. GitHub answers with the newest *published* release; drafts and pre-releases are skipped. With no release at all, Settings says "No releases on GitHub yet" and nothing else happens. If the release's tag is newer than the running version, the next draw shows "Sidearm X is out" with an Update button, and Settings shows the release notes.

**What the updater needs from a release.** Get any of these wrong and the update is refused or never offered:

| Part | Must be | Why |
| --- | --- | --- |
| Tag | `v<ver>`, the same version as `AssemblyInformationalVersion` in `src/Program.cs` (for example `v0.8.1`) | Compared number by number with the running version |
| Release type | Published, **not** a draft or a pre-release | `releases/latest` ignores the others (a pre-release is a way to hold one back) |
| Assets | `Sidearm-<ver>-ua.zip` and `Sidearm-<ver>-en.zip`, with `Sidearm/Sidearm.exe` and `Sidearm/ui/` inside | The updater installs the first it finds (`-ua`, then `-en`, then the pre-0.8.2 names `-share-uk.zip` and plain `Sidearm-<ver>.zip`); the download link must be on `github.com/HARDv11/Sidearm/releases/download/` |
| Old updaters | 0.8.0 only knows `*-share-uk.zip` and plain `Sidearm-<ver>.zip` | That's why the 0.8.1 release ships its Ukrainian zip as `Sidearm-0.8.1-share-uk.zip`; from 0.8.2 on, every copy reads `-ua` / `-en` |
| Checksum | GitHub's own `digest` for the asset, or an attached `<zip>.sha256` | No checksum, no install. Attach both `.sha256` files anyway: they're the fallback |
| Notes | The release description | Shown in Settings → General → Updates (first 4,000 characters), so write them for users |

**Copies older than 0.8 have no updater.** Anyone on 0.7.0 or earlier installs 0.8.x by hand once (unzip over the old folder, or into a new one and let it bring the data over). From then on, every published release reaches them by itself within a day of their next draw.

**Checklist for a release**

1. `./build.sh`, then `tests/run.sh ui`. Everything passes; look at the screenshots in `tests/ui/shots/`.
2. Bump the version: the three assembly attributes and `Version` in `src/Program.cs`, the title of `assets/README.txt` and its "NEW IN" section, and the changelog below. Build again.
3. `tools/release.sh`. Upload the four files it made (two zips, two `.sha256`), not older copies: each `.sha256` belongs to its own zip.
4. Commit and push to `main`.
5. On GitHub: Releases → Draft a new release → tag `v<ver>` on `main` → title `Sidearm <ver> · <name>` → notes → attach the four files → **Publish release**. (A Claude session can push code but gets 403 when it tries to create a release, so this step is done by hand. A GitHub Actions workflow that builds and publishes on a tag push would remove it; not tried yet.)
6. Check that it arrived: in a running copy, Settings → General → Updates → Check now should say the new version is out. Then read `data\sidearm.log` after the update: `Update <ver>: … files swapped in`, then `Updated from <old> to <ver>` about 30 s after the restart.

**If an update goes wrong on someone's PC.** Install failures put every file back and leave `data\update\failed.txt`, which the old version shows as a tray message at its next start. A new version that fails to start twice in a row is rolled back by itself (its log says `didn't start properly twice`). Replaced files sit next to the new ones as `*.old` until the new version has run for 30 s.

## Changelog

Newest first. Every version name is in the log's `starting` line, so log entries can be matched to builds. Milestones 1–4 of the original plan map to 0.1, 0.3, 0.4 and 0.5.

### 0.8.1 · Resupply — 2026-10-21

*Fixes from a week with 0.8, and the first update delivered by the updater.*

**Fixed**

- A crash in the browser engine (`EmbeddedBrowserWebView.dll`, access violation) when a page opened a new tab while 5 tabs were open: the recycled tab's view was disposed in the middle of the new-window request. Views are now retired safely (`RetireView`), and new windows get a fresh engine.
- The engine's link bubble stayed stuck at the bottom of the page. Replaced with Sidearm's own, in the accent color, which goes away when the pointer reaches the strip or Sidearm holsters.
- On a search results page the bar showed the whole encoded address (`…search?q=%D1%87%D0%BE…`). It now shows the search words; editing them keeps the results' mode (`tbm=isch` stays), a new query goes to the usual engine. Other addresses are shown decoded.
- The recent-searches list popped open on its own and covered the page. It now opens on typing or ↓ only, and while you edit a page's query it lists no recents.

**Added**

- The + new-tab button sits right after the last tab (with one tab it stays in the bar row).
- Shortcuts (`Shortcuts.cs`): Settings → General, the tray menu, or a one-time offer on the first draw. Sidearm has no taskbar button (a tool window), so it couldn't be pinned before; a pinned shortcut now draws it.
- `CrashCheck`: an unclean exit is noticed at the next start, and Windows' crash record goes into the log. New windows and recycled tabs are logged too.
- Releases ship as `Sidearm-<ver>-en.zip` and `Sidearm-<ver>-ua.zip`, each with a `.sha256`; the updater reads both names (and the old ones). The 0.8.1 release itself keeps `-share-uk.zip` for the Ukrainian zip so 0.8.0's updater can find it.

### 0.8.0 · Resupply — 2026-10-18

*Fixes from testers' logs and videos, the start page grows up, and updates without zips.*

**Added**

- **Updates from GitHub** (`Updater.cs`, `MainForm.Update.cs`): once a day Sidearm asks `api.github.com/repos/HARDv11/Sidearm/releases/latest`. A newer release shows a toast with an Update button (and a row in Settings → General with the release notes). Update downloads the `*-share-uk.zip`, checks it against the SHA-256 GitHub publishes for the asset (`digest`, or a `<zip>.sha256` asset), unpacks it into `data\update`, closes the page engine, swaps the files in (each replaced file kept as `.old`), and restarts with `--after-update <pid>`. The new copy deletes the `.old` files after 30 s; two failed starts put them back. `data\` is never touched.
- **Notes** on the start page (one box, saved to `data\notes.txt`), and `n` / `note` / `н` / `нотатка` in both bars adds a time-stamped line.
- **Pinned sites** on the start page (`data\pins.json`): a + tile, right-click a page → Pin to start page, drag to reorder, × to remove, up to 12. HoYoLAB and LIGA:ZAKON as examples.
- **Wrong-layout fix** in `query.js` (`layoutFix`): `нщгегиу` offers `youtube`, `ghbdsn` offers `привіт`, judged by common letter pairs and vowels.
- **Quick bar animation**: it stretches out from the pointer (ease-out with a slight overshoot) and leans toward the mouse while hovered. Off when Windows' animations are off.
- **YouTube ad skipper** in `inject.js` (`youtubeAds`, only with Sidearm's own blocker): mutes the ad, jumps to its end, presses Skip.
- **Snap to screen edges** is now a setting (`snapEdges`), and Shift while letting go skips it once.

**Fixed**

- The strip and the quick bar could come up blank after a draw (hardware acceleration off, warm start): see the gotcha above.
- The quick bar could close itself right after appearing: Sidearm's own process now counts as "ours", deactivation is ignored for 500 ms after it shows, and the reason it closes is logged.
- Per-site zoom reset to 100% after a long holster: `DisposeView` and new views reset `ZoomHost`. Zoom is now kept per site.
- Settings → Look's color picker lost its CSS around 0.6.1. Rebuilt: round presets with names (Ukraine as a split circle), tiles for your color and the gradient color, a HEX field and a live strip preview.
- Alt over a word did nothing until you clicked into the page (now relayed from the bar), and nothing at all on a site where look-up was switched off (now offers to turn it back on).
- "NBU rate · 19.10.2026" on a Friday looked wrong: it now reads "NBU rate for 19.10".
- Settings' section links piled up over the page in a narrow window: they scroll sideways in one row now.

### 0.7.0 · Quickdraw — 2026-10-16

*Fixes from testers' logs, the VirusTotal clean-up, and the ideas list.*

**Added**

- **Quick bar** (`Alt+Space`, Settings → Quick bar): a small always-on-top bar at the mouse (or mid-screen / top), even over a game. It shares `query.js` with the strip; answers copy, everything else opens in Sidearm (`QuickBar.cs`, `ui/quick.html`, `ui/quick.js`).
- **Money** in both bars: `100 usd`, `50 € in pln`, `1000 грн в usd`, `$20`. National Bank of Ukraine rates (open.er-api.com as fallback), cached in `data\rates.json`, refreshed every 6 h (`Rates.cs`). `currency` picks what a one-sided amount converts into.
- **Timers**: `timer 5m`, `timer 1:30 boss`, `10 min timer`, `timer off`. A synthesized chime (`Chime.cs`) plus a tray notification, even while holstered; the strip shows the soonest one counting down (click stops it).
- **Video pop-out** (`Alt+P`, shield panel, right-click a video): a small always-on-top window that survives holstering (`VideoWindow.cs`). YouTube and Twitch use their own embedded players through `ui/pop.html`; any other site loads the page and its biggest video fills the window. Opacity from the title bar's menu.
- **In-place translation** (experimental, `translateMode: inplace`): the page script sends text nodes in batches (`tr-batch`), the shell translates them with one Google request per batch (`Lookup.TranslateBatch`), and the words change on the real page, so logins survive.
- **uBlock Origin Lite** (experimental, `blocker: ublock`): downloaded from the uBOL-home GitHub release on demand, loaded as an engine extension (`Ublock.cs`); Sidearm's own engine is dropped while it runs and comes back if it fails.
- Exe metadata: company, copyright, an application manifest (asInvoker, supported OS list), a real build time and a correct PE checksum (`assets/sidearm.rc.in`, `Sidearm.exe.manifest`, `pe_stamp.py`).
- **Updating keeps sign-ins** (`Migrate.cs`): a fresh copy with no `settings.json` looks for an earlier `data` folder beside it, one folder up, or in `%LOCALAPPDATA%\Sidearm`, and offers to copy it over (caches and the log stay behind; the old copy is only read). Chromium's cookies are encrypted per Windows user, so this works on the same PC and account.

**Fixed**

- The tab strip flickered on busy pages, most visibly when pinned over a game: every `tabs` message (several a second while ads are blocked) rebuilt every tab, favicon and button. Tabs are now patched in place, and identical lists are skipped.
- `Ctrl+Enter` to a broken Zen install threw "side-by-side configuration is incorrect". Sidearm now retries without the shell, then falls back to the default browser with a toast.
- Google answered in Russian on Russian-language Windows. With the interface in Ukrainian the engine now starts with `Language = uk`.
- The word pop-up waited up to 6 s for dictionaryapi.dev. Once the other sources answer it waits 0.4–1.5 s more at most.

**Changed**

- No keyboard polling: `GetAsyncKeyState` is gone. Shortcuts read modifiers from the key event (plus `Control.ModifierKeys`), and moving the window hands the press to Windows (`WM_NCLBUTTONDOWN` + `HTCAPTION`), which also snaps on release as before.
- Filter lists are no longer shipped in `seed\`; they download on first start. The share zip went from 2.9 MB to 1.3 MB.
- A pinned window takes back "topmost" once when the game takes focus.

### 0.6.1 · Loadout — 2026-10-16

*Fixes from the first day with 0.6, and Sidearm in Ukrainian.*

- **Added**
    - Ukrainian interface (Settings → General → Language). One dictionary, `ui/lang/uk.js`, serves the pages (`ui/i18n.js`), the page script and the shell (`src/Lang.cs`). The shared zip starts in Ukrainian through a `defaults.json` next to the exe.
    - Gemini opens in Google's desktop app when “Desktop app” is picked and the app is installed, with the question on the clipboard.

- **Fixed**
    - The shield panel covered the page beside it. The strip view is now clipped to its bar plus the panel (`SetWindowRgn`).
    - The accent color didn't reach open start pages, settings, the look-up card, the gesture trail, the image viewer or the window edge. It now does, live.
    - Grain was almost invisible on dark themes. It's now real specks instead of a blend.

- **Changed**: the speed readout moved into the shield panel's footer; “Claude app” is now “Desktop app”.

### 0.6.0 · Loadout — 2026-10-16

*Make it yours, and make it quicker.*

- **Added**
    - Look: nine color presets (one is a blue and yellow Ukraine gradient) or your own colors, a two-color gradient across the strip, tint strength and Zen-style grain. The window border takes the color too.
    - Ask AI: Claude, ChatGPT, Gemini, Perplexity or Copilot. The `a` prefix asks your pick, and `gpt` and `px` are new fixed prefixes. Gemini has no prompt link, so Sidearm types the question in for you.
    - Send pages to any browser (Zen, Chrome, Edge, Firefox, Brave, Opera, Vivaldi or the default), with each browser's own private-window flag.
    - Translate: Alt + T or the shield panel. Pages in other languages get a one-time offer per site, or are translated automatically, and there's a per-site “always” switch and a list of languages you read.
    - Speed: a spare tab engine kept ready while Sidearm is drawn, a warm start at boot, background tabs dropped to low memory, and a page-load readout showing Sidearm's own share.

- **Changed**
    - Settings gained Look, Translate, Ask AI and Speed sections. Labels name the chosen browser instead of “Zen”.
    - The shield panel fits short windows and scrolls; load time moved to its footer.
    - Settings migration 6: `zenPath` became `browserPath`, and the new prefixes were added.

- **Fixed**
    - Quitting before the first draw saved an empty tab list and lost the session.
    - WebP images (Fandom wikis) couldn't be copied, saved or popped out.
    - Sidearm's own pages could be “sent” to an outside browser.

- **Dropped**: preconnecting while you type (connections are kept per site, so the warm-up couldn't be reused).

### 0.5.0 · Feel — 2026-10-15

*Milestone 4: the small things.*

- **Added**
    - Mouse gestures: seven right-button strokes with a drawn trail and the action's name, each one remappable.
    - Ghost mode (Alt + G): see-through and click-through.
    - Muted by default, with Alt + M and speaker buttons in the bar and on tabs.
    - Clean links on copy, Ctrl + Shift + C quick copy, and amnesia mode.
    - The Home key goes to the start page.

- **Changed**
    - The Home button was removed (the logo and the Home key replace it).
    - With tabs at the bottom, the + button moves with them.
    - The shield panel switches flip instantly and it no longer rebuilds on every update.
    - Bing replaced Yandex for reverse image search.


### 0.4.0 · Glance — 2026-10-15

*Milestone 3: look without leaving.*

- **Added**
    - Glance (Alt + click or middle-click a link). Alt + Enter keeps it, Alt + Shift + Enter opens it in a new tab, Ctrl + Enter sends it to Zen.
    - Ctrl Ctrl image viewer: zoom, rotate, copy, save, a pop-out window, Lens and TinEye.
    - Open in Zen or a Zen private window, from keys and the right-click menu.
    - Light and System themes, and tabs along the bottom.
    - Home button and Alt + Home.
    - The Field manual: every command on the start page, with a filter.

- **Changed**: the settings page opens instantly; the Wikipedia extension scan runs in the background.

### 0.3.0 · Clean read — 2026-10-15

*Milestone 2: pages without the junk.*

- **Added**
    - Ad and tracker blocking with Sidearm's own filter engine (EasyList, EasyPrivacy, EasyList Cookie), cosmetic hiding included.
    - Reader view (Alt + R) and the fog remover (Alt + F, optionally automatic per site).
    - Force dark, and the Modern for Wikipedia import.
    - The shield page panel, the full settings page, and live reload of `settings.json`.

- **Fixed**
    - Changing settings in the app didn't stick: a validation bug sent “Ask Claude in a tab” back to the app.
    - Hand edits to `settings.json` were overwritten.


### 0.2.0 · Holster+ — 2026-10-15

*First round of feedback.*

- **Added**
    - Home on the logo, and per-site zoom with a % pill and automatic zoom-out on narrow windows.
    - Ask Claude (desktop app by default) and the `c` prefix.
    - Word look-up (hold Alt, or double-click) with a Ukrainian translation.
    - `tt` for DeepL, and NV Bitter, NV Jost and Sourcerer fonts.

- **Changed**: larger text everywhere; other tabs stay visible when you open a new one.
- **Fixed**: the bar overlapping, and the prefix label always reading “web”.

### 0.1.0 · Holster — 2026-10-15

*Milestone 1: a usable lookup tool.*

- **Added**
    - The C# shell and the Alt + ` draw and holster key.
    - The command bar with prefixes, calculator, units and clipboard search.
    - Up to 5 tabs with least-recently-used recycling.
    - Sleep 10 s after holstering, unload after 15 min, the RAM readout and the tray icon.

- **Fixed** before release: suspending failed every time, because of the visibility gotcha above.

## Roadmap

**Unconfirmed on Windows** (check the logs)

- [ ] Ghost mode renders pages while see-through. If they go blank, replace the layered window with a DWM-thumbnail overlay.
- [ ] Gestures suppress the right-click menu after a stroke on a real mouse.
- [ ] Gemini fill-in still finds the prompt box (it depends on Gemini's page layout).
- [ ] Browser detection on other PCs (App Paths, Start-menu registration, usual folders).
- [ ] Warm start and the spare tab: is the memory cost acceptable in practice?
- [ ] 0.7: the strip no longer flickers when pinned over a game.
- [ ] 0.7: dragging the strip with the Windows move loop, including a quick click that shouldn't start a move.
- [ ] 0.7: the quick bar hides and hands focus back to a full-screen game. Alt+Space is taken by PowerToys Run where it's installed; the key can be changed in Settings.
- [ ] 0.7: Google's batch translate keeps one line per text, and a long page doesn't hit the 429 limit.
- [ ] 0.7: re-scan the new zip on VirusTotal.
- [ ] 0.8: the strip and quick bar paint after a draw with hardware acceleration off (look for "kicked it" in the log).
- [ ] 0.8: the first real update (0.8.0 → 0.8.1): download, swap, restart, `.old` files gone after 30 s.
- [ ] 0.8: the quick bar's stretch animation looks smooth with a web view inside (it resizes the window ~10 times).
- [ ] 0.8: Alt relayed from the command bar reaches the page (mouse events keep coming while the bar has focus).
- [ ] 0.8: the YouTube ad skipper still finds YouTube's player classes.

**Ideas on the table**

- Code signing (for example SignPath's free open-source plan): the real fix for SmartScreen and scanner guesses.
- Translate whole sentences in place even when they contain links or bold words (needs an HTML-aware endpoint).
- A redesign of the icons and layouts.

**Decided against**

- A password manager (sessions persist; Bitwarden-style extensions need a toolbar Sidearm doesn't have).
- Russian services, and anything that touches files outside Sidearm's own folder.
