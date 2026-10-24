SIDEARM 0.9.0 · "Raven" (Ворон)
"Switching to your sidearm is faster than opening a browser."

START
  Double-click Sidearm.exe. It lives in the tray (crosshair icon). For a desktop / Start menu icon you can pin
  to the taskbar: Settings -> General -> Shortcuts.
  Keep this whole folder together: the exe needs the DLLs and the ui folder.
  Windows may show a SmartScreen warning the first time (the exe isn't signed):
  More info -> Run anyway. A few lesser-known virus scanners guess "trojan" for unsigned new programs
  like this one; Microsoft Defender and the big names don't.
  Every command is listed on the start page (scroll down to the "Field manual",
  type in its filter box to find one).

UPDATING
  From 0.8 on, Sidearm checks GitHub once a day and says when a new version is out. Click Update in that
  message (or Settings -> General -> Updates): it downloads the new version, checks it against GitHub's
  checksum and restarts into it. Your data folder (sign-ins, notes, tabs, settings) is never touched.
  The replaced files are kept as *.old until the new version has run for half a minute; if it fails to
  start twice, the old version comes back by itself.

  By hand, as before:
  1. Right-click the tray icon -> Quit Sidearm.
  2. Either unzip the new version over this folder (replace everything; your data folder stays),
     or unzip it anywhere new and start it: it finds your earlier copy nearby and offers to bring
     over your sign-ins, open tabs and settings. Say yes, and you're still signed in everywhere.
  By hand: copy the old "data" folder next to the new Sidearm.exe before its first start.
  Never send your data folder to anyone: it holds your sign-ins.

NEW IN 0.9.0
  Raven                 messages and links to friends' Sidearms (Settings -> Raven; off until you turn it on).
                        Add a friend with a code (works once, for 24 hours), then read four words to each other
                        out loud: the same words mean it's really them. Whoever writes gets a tab with their
                        avatar beside the window, the wing opens every chat, and a small card offers a quick
                        reply (ready answers in Settings). In the bar, @name text sends it with the page you're
                        on (Shift + Enter: text only). Right-click a page or a link -> Send to a friend.
                        Encrypted on your PC; Sidearm's post office on Cloudflare only passes messages on and
                        can't read them. Five avatars to pick from (the clouds one is by soikach)
  Pop out a page        Alt + Shift + P, the shield panel or right-click a page: the page moves into its own
                        small window over the game, without reloading (a map keeps its place). Right-click its
                        title -> Back to Sidearm. Alt + G turns pop-outs see-through together with Sidearm.
                        Settings -> General -> Pop-outs hide with Sidearm (off: they stay up)
  Clipboard history     Alt + V or cb in the bar: the last 10 things you copied in Sidearm, text and pictures.
                        Enter copies one again. Kept in memory only, never saved
  Glance buttons        round buttons beside a Glance: close, open here, new tab, your browser, copy link,
                        send to a friend
  Fixed                 a page could close its own tab after a long holster or a restart (HoYoLAB's
                        interactive map did): only a tab a page opened itself, in that session, may be closed
                        by the page now. Failed page loads say why in the log

NEW IN 0.8.2
  Fixed                 the Update button didn't work in 0.8.0 and 0.8.1 ("failed to install" every time).
                        From this version on it does; 0.8.0 and 0.8.1 have to be replaced by hand once

NEW IN 0.8.1
  Command bar           on a search results page the bar shows the words you searched for (not a long
                        encoded address): change two words and press Enter, and Google Images stays Images.
                        The list of recent searches no longer pops open by itself: type, or press Down
  New tab button        the + now sits right after the last tab
  Shortcuts             Settings -> General -> Shortcuts (or the tray menu) puts Sidearm on the desktop and in
                        Start. To pin it to the taskbar, right-click the desktop icon -> Pin to taskbar.
                        Clicking it while Sidearm runs draws it. Sidearm offers this once on its own
  Link preview          hovering a link shows where it goes at the bottom, in your color; it no longer
                        gets stuck on screen
  Fixed                 a crash in the browser engine when a page opened a new tab while 5 tabs were open
                        (tabs are now closed safely, after the engine is done with them). If Sidearm ever
                        ends without quitting, the next start copies Windows' crash report into the log

NEW IN 0.8.0
  Updates               one click from the "is out" message, or Settings -> General -> Updates
  Notes                 a notes box on the start page that saves itself. In either bar, n <text>
                        (or note / н / нотатка) adds a line stamped with the time, without opening anything
  Pinned sites          tiles on the start page: + adds one, right-click a page -> Pin to start page,
                        drag to reorder, x to remove (up to 12). HoYoLAB and LIGA:ZAKON are there as examples
  Wrong layout          typed нщгегиу? The bar offers "youtube" (and ghbdsn -> привіт)
  Quick bar             stretches out from your mouse, and leans a little toward it
  YouTube ads           muted, skipped to the end and Skip pressed (Settings -> Pages; uBlock Lite does
                        this on its own)
  Snapping              Settings -> General -> Snap to screen edges; hold Shift while you let go to skip it once
  Colors                Settings -> Look rebuilt: round presets with names, your color and a gradient color
                        as big tiles, a HEX field and a live preview of the strip
  Fixed                 the strip (and quick bar) could come up as an empty box after a draw, mostly with
                        hardware acceleration off; the quick bar could close itself right after appearing;
                        per-site zoom reset to 100% after a long holster (zoom is now per site, so
                        wiki.example.com and www.example.com share it); Alt over a word now works right after
                        drawing, and a site where look-up is switched off says so instead of doing nothing;
                        "NBU rate for 19.10" instead of a date that looked wrong; Settings' section links
                        no longer pile up over the page in a narrow window

NEW IN 0.7.0
  Quick bar             Alt + Space anywhere, even mid-game: a small bar pops up at your mouse. Sums, units,
                        money and timers answer right in it; Enter opens the rest in Sidearm; Esc goes back
                        to the game. Settings -> Quick bar changes the key or where it appears.
  Money                 100 usd, 50 € in pln, 1000 грн в usd, $20: National Bank of Ukraine rates
  Timers                timer 5m, timer 1:30 boss, 10 min timer: a chime and a notification when it's up,
                        even while holstered. The strip shows the countdown; click it (or type timer off) to stop.
  Pop out video         Alt + P, the shield panel or right-click a video: it plays in its own small
                        always-on-top window that stays up while you play (YouTube and Twitch use their
                        own players). Right-click its title bar for opacity.
  Translate in place    Settings -> Translation -> In place (experimental): the words change on the real
                        page, so you stay signed in. The page proxy stays the default.
  uBlock Origin Lite    Settings -> Pages -> Blocker (experimental): blocks inside the engine instead of
                        Sidearm's own lists. Downloaded from its official GitHub when you pick it.
  Fixed                 the tab strip flickered on busy pages (most visible when pinned); a broken browser
                        install now falls back to your default browser; Google and other sites answer in
                        Ukrainian when Sidearm is in Ukrainian; the word pop-up no longer waits 6 s for a slow
                        dictionary; moving the window is handled by Windows itself
  Smaller download      filter lists download on first start instead of shipping in the zip

NEW IN 0.6.1
  Ukrainian             Settings -> General -> Language (the shared copy starts in Ukrainian)
  Gemini app            with "Desktop app" picked in Settings -> Ask AI, Gemini opens in its own app;
                        your question is on the clipboard, press Ctrl + V
  Shield panel          no longer covers the page beside it
  Your color            now reaches the start page, settings, the word pop-up, gestures and the
                        window edge straight away; grain is properly visible

NEW IN 0.6
  Your look             Settings -> Look: a color (presets or your own), a two-color gradient
                        across the top strip, tint strength and Zen-style grain
  Ask AI                Settings -> Ask AI: Claude, ChatGPT, Gemini, Perplexity or Copilot.
                        Prefix "a" asks your pick; c / gpt / px always go to Claude / ChatGPT / Perplexity
  Any browser           Settings -> Links: Ctrl + Enter and friends can use Zen, Chrome, Edge, Firefox,
                        Brave, Opera, Vivaldi or your default browser (private windows included)
  Translate             Alt + T or the shield panel; pages in languages you don't read offer a
                        Translate button (or get translated straight away: Settings -> Translate)
  Faster new tabs       a blank page engine waits ready while Sidearm is drawn (freed on holster)
  Warm start            when started with Windows, the engine starts quietly in the background
  Lighter background    tabs you leave give memory back straight away
  Page load readout     the shield panel's footer shows how long a page took and Sidearm's share

NEW IN 0.5
  Mouse gestures         hold the right button and flick (see GESTURES)
  Ghost mode             Alt + G: see-through and click-through, so a map stays up while you play
  Muted by default       new pages start muted; Alt + M or the speaker button unmutes a tab
  Clean links            copied links lose utm_*, fbclid and other trackers
  Quick copy             Ctrl + Shift + C copies the selection as plain text
  Amnesia mode           Settings -> Feel: every holster wipes history, cookies, recent searches
  Home key               the Home key goes to the start page (when you're not typing)
  Bottom tabs            the + new tab button sits with them
  Shield panel           opens and switches instantly
  Reverse image search   Bing instead of Yandex

NEW IN 0.4
  Light theme, tabs on the bottom, Glance, the Ctrl Ctrl image viewer, Send to Zen,
  the start page's Field manual, faster settings.

KEYS
  Alt + `               draw / holster (works over borderless games)
  Alt + ` twice         search what's on your clipboard
  Alt + `, then 1       same thing (hold Alt)
  Esc                   holster (first closes a Glance, image viewer or pop-up)
  Alt + G               ghost mode on / off (Alt + ` also makes it solid again)
  Ctrl+L, F6, Alt+D, /  jump to the command bar
  Enter / Shift+Enter   go here / go in a new tab
  Ctrl+Enter            send to your browser (Zen by default)   Ctrl+Shift+Enter   private window
  Ctrl+T, Ctrl+W        new tab, close tab (up to 5 tabs)
  Ctrl+Tab, Ctrl+1..9   switch tabs   ·   Ctrl+Shift+T  reopen closed tab
  Alt+Left / Alt+Right  back / forward      F5  reload      Home or Alt+Home  start page
  Alt + M               mute / unmute this tab
  Alt + T               translate this page (again: back to the original)
  Alt + P               pop out this page's video into its own always-on-top window
  Alt + Shift + P       pop out the whole page (no reload); again in the pop-out: back to Sidearm
  Alt + V               clipboard history (also: cb in the bar)
  Alt + Space           the quick bar (works even while holstered; change it in Settings -> Quick bar)
  Ctrl + Shift + C      copy the selection as plain text (nothing selected: the page's clean link)
  Ctrl + scroll on the top strip   window opacity
  Hold Alt over a word             meaning + translation pop-up (or double-click the word)
  Right-click                      Look up / Ask AI / Glance link / Open in your browser (or private) / Copy link (clean)
  Alt + R                          reader view (just the article; A-/A+, dark/sepia/light, Esc closes)
  Alt + F                          clear fog: overlays, "subscribe" walls, blur, scroll locks
  Shield button next to the bar    this site: ads on/off, reader, fog (+ auto), force dark,
                                   word look-up on/off, zoom
  Gear button (top strip)          Settings page
  Drag the top strip to move; drag the thin edge to resize; it snaps to screen edges (hold Shift as you let go
  to drop it exactly there).

GLANCE
  Alt + click a link (or middle-click; Settings -> Links can turn middle-click back
  into "background tab"). The link opens in a floating panel over the page.
  Alt + Enter            keep it: the Glance becomes this tab
  Alt + Shift + Enter    open it in a new tab
  Ctrl + Enter           send it to your browser
  Esc, or click the dimmed page   close it
  The top strip shows GLANCE with Open / Tab / browser / x buttons while one is open, and round buttons
  beside it do the same (plus copy link and send to a friend).

RAVEN (Settings -> Raven)
  Add a friend -> Invite: send the code to your friend (Discord, Telegram, anything). It works once, for
  24 hours. Add a friend -> I have a code: paste theirs. Both of you then see four words: read them to
  each other out loud. The same words mean nobody swapped the code on the way.
  Tabs beside the window show who wrote; the wing tab opens every chat. Against the left edge of the
  screen they move to the right. A card with a quick reply pops up when a message comes while you're in
  Sidearm. @name text in the bar sends it; Enter adds the page you're on, Shift + Enter doesn't.
  Your keys, friends and the last 200 messages with each live in data\raven. Like the rest of data\,
  never send it to anyone. Settings -> Raven -> Delete removes them and your inbox on the post office.

GESTURES (hold the right mouse button, flick about 40 px, let go)
  <-  back            ->  forward          ^  top of page        v  close Glance, or holster
  ^ then v  reload    v then ->  your browser   v then <-  its private window
  A plain right-click still opens the menu. Change or switch off any of them in Settings -> Gestures.

GHOST MODE
  Alt + G while Sidearm is up: the window turns see-through (Settings -> Feel -> Ghost opacity) and
  your clicks go straight through to the game. Alt + G or Alt + ` brings it back. Alt + G is only
  taken while Sidearm is on screen, so games keep it the rest of the time.

IMAGES
  Hover an image and tap Ctrl twice: it opens in the viewer at full size (the linked
  original, the biggest srcset entry, or the Wikimedia original when there is one).
  Scroll = zoom at the pointer · drag = pan · double-click = fit / 1:1 · R = rotate
  C = copy · S = save to Downloads (as PNG) · P = pop out into its own always-on-top window
  Lens / TinEye / Bing buttons = reverse image search · Esc closes

COMMAND BAR
  plain text            Google
  w / wu                Wikipedia EN / UK         i   Google Images
  y                     YouTube                   t   Google Translate (to UK, or to EN if you type Cyrillic)
  tt                    DeepL (same direction rule)               c   Ask Claude
  m                     Maps     r  Reddit     d  definition     ddg  DuckDuckGo
  120*3.5, 2^10, 15% of 80, sqrt(2)      answers in the bar; Enter copies it
  n buy potions         adds a line to the notes on the start page (note / н / нотатка work too)
  5 ft in cm, 30 mph in km/h, 100 f in c  unit conversion
  100 usd, 50 € in pln, 1000 грн в usd    money (NBU rates; Settings -> Search picks what "100 usd" turns into)
  timer 5m, timer 1:30 boss, timer off    timers
  Add or edit prefixes in Settings -> Search.

ZOOM
  Ctrl + scroll or Ctrl +/- on a page: remembered per site. The brass % pill next to the
  bar shows it; click it to go back to 100%. When the window is narrow, sites you haven't
  zoomed open a little zoomed out (Settings -> Pages).

WHILE HOLSTERED
  10 s after hiding, every page is suspended (scripts stop, memory drops).
  After 15 min hidden, tabs are unloaded entirely; they reload when you draw.
  Background tabs fall asleep 30 s after you leave them.
  The RAM readout on the strip is Sidearm's total, every engine process included.

FILES
  data\settings.json    everything in the Settings page (edits here apply by themselves)
  data\zoom.json        per-site zoom
  data\sites.json       per-site choices from the shield panel
  data\filters\         downloaded filter lists
  data\rates.json       exchange rates (refreshed every few hours)
  data\extensions\ublock-lite\  uBlock Origin Lite, if you picked it
  data\extensions\      your imported copy of Modern for Wikipedia
  data\raven\          Raven: your keys (identity.json), friends and messages. Never share it
  data\sidearm.log      what happened; send me this when something breaks
  data\webview\         browser profile (cookies, cache). Separate from your main browser.
  ui\                   everything you see. ui\sidearm.css is the whole theme (dark + light).
  ui\fonts\             NV Bitter, NV Jost, Sourcerer (eBook Fonts by Nico Verbruggen, OFL)
  ui\vendor\            Mozilla Readability (Apache 2.0)
  ui\avatars\           Raven's five avatars (four by the Sidearm project, the clouds by soikach)
  source\               the C# source and build script

ADS AND TRACKERS
  Blocked inside Sidearm with EasyList, EasyPrivacy and EasyList Cookie (EasyList authors, GPLv3 /
  CC BY-SA 3.0). They download on first start and weekly after that into data\filters; the first
  page you open may still show ads. The shield shows the count per page. Costs roughly 30 MB of RAM;
  turn it off in Settings if you'd rather.
  Or pick uBlock Origin Lite (Raymond Hill, GPLv3) in Settings -> Pages -> Blocker: experimental, it
  blocks inside the engine (no count in the shield; its own settings page has the per-site choices).

KNOWN LIMITS IN THIS BUILD
  Ghost mode makes the window layered (that's how Windows does see-through + click-through). If pages
  go blank in ghost mode, tell me: there's another way to draw it.
  Gestures that start inside some embedded frames (a YouTube player) may not register; start outside.
  Browsers are found through the registry and the usual install folders; if Ctrl+Enter opens your
  default browser instead of the one you picked, paste its .exe path in Settings -> Links.
  Translation goes through Google Translate's page proxy: pages behind a login may not translate.
  "In place" translation keeps logins but translates bold or linked words inside a sentence on their own.
  The quick bar takes its key all the time. If Alt + Space is already used (PowerToys Run uses it),
  pick another key in Settings -> Quick bar.
  Pop out uses the page's own video; sites that hide their video in a frame may not pop out (YouTube
  and Twitch always do).
  Windows may show a timer's notification quietly while a full-screen game runs (Focus assist); the
  chime still plays.
  Gemini can't take a question in its link, so Sidearm types it in for you (it's also on the clipboard).
  Fog clearing only reveals what the page already sent; hard paywalls stay shut.
  The look-up uses free public services (dictionaryapi.dev, Wikipedia, Google Translate);
  the hovered word is sent to them, nothing else.
  Opacity below 100% is experimental; if pages go blank, scroll back to 100%.
  Raven needs the internet and Sidearm's post office (sidearm-post.hard11.workers.dev). Messages wait
  there for up to 7 days while a friend is offline. There's no "seen" and no group chat yet.
  Needs the Microsoft Edge WebView2 Runtime (already on Windows 11 and updated
  Windows 10). If it's missing, Sidearm offers the download page.
