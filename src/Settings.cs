using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Sidearm
{
    public class Bang
    {
        public string name { get; set; }
        public string url { get; set; }
        public Bang() { }
        public Bang(string name, string url) { this.name = name; this.url = url; }
    }

    /// <summary>
    /// Everything in data\settings.json. Property names are lowercase on purpose:
    /// they are what you see and edit in the file.
    /// </summary>
    public class Settings
    {
        public string hotkey { get; set; }
        public string searchUrl { get; set; }
        public Dictionary<string, Bang> bangs { get; set; }
        public int maxTabs { get; set; }
        public bool autoHolster { get; set; }
        public bool pinned { get; set; }
        public double opacity { get; set; }
        public int suspendAfterHideSeconds { get; set; }
        public int unloadAfterHideMinutes { get; set; }
        public int backgroundTabSuspendSeconds { get; set; }
        public bool disableGpu { get; set; }
        public string browserArguments { get; set; }
        public bool devTools { get; set; }
        public double uiScale { get; set; }
        public bool autoZoom { get; set; }
        public int autoZoomBelow { get; set; }
        public bool lookup { get; set; }
        public bool lookupOnDoubleClick { get; set; }
        public string claudeOpenIn { get; set; }
        public string theme { get; set; }          // dark, light or system
        public string tabsPosition { get; set; }   // top or bottom
        public string zenPath { get; set; }        // old name of browserPath, read once and moved
        public string sendTo { get; set; }         // auto (Zen if installed, else default), default, zen, chrome, edge, firefox, brave, opera, vivaldi
        public string browserPath { get; set; }    // optional: that browser's .exe, if Sidearm can't find it
        public string aiProvider { get; set; }     // claude, chatgpt, gemini, perplexity, copilot
        public string translateTo { get; set; }    // language code pages are translated into
        public string autoTranslate { get; set; }  // off, offer, always
        public string neverTranslate { get; set; } // languages you read, comma separated
        public string accent { get; set; }         // "" = the theme's brass, else #rrggbb
        public string accent2 { get; set; }        // second gradient color, "" = none
        public int tint { get; set; }              // 0-100: how much the strip takes the color
        public int grain { get; set; }             // 0-100
        public string language { get; set; }       // en or uk: Sidearm's own menus and pages
        public bool spareTab { get; set; }         // keep one tab engine ready while drawn
        public bool warmStart { get; set; }        // start the engine quietly when started with Windows
        public bool middleClickGlance { get; set; }
        public bool gestures { get; set; }         // hold the right mouse button and flick
        public string gestureLeft { get; set; }
        public string gestureRight { get; set; }
        public string gestureUp { get; set; }
        public string gestureDown { get; set; }
        public string gestureUpDown { get; set; }
        public string gestureDownRight { get; set; }
        public string gestureDownLeft { get; set; }
        // 0.9.5: shapes drawn with the right button held
        public string gestureCircle { get; set; }      // ○ clockwise
        public string gestureCircleBack { get; set; }  // ○ counter-clockwise
        public string gestureSquare { get; set; }
        public string gestureTriangle { get; set; }
        public string gestureCheck { get; set; }       // ✓
        public string gestureZ { get; set; }
        public string gestureZigzag { get; set; }      // a wave: ⩘⩗ back and forth
        public string myGestures { get; set; }         // your own: JSON [{ "name", "action", "pts": [[x,y]…] }]
        public string ghostHotkey { get; set; }    // see-through, click-through window
        public double ghostOpacity { get; set; }
        public bool muteByDefault { get; set; }
        public bool cleanLinks { get; set; }       // strip utm_*, fbclid and friends from copied links
        public bool amnesia { get; set; }          // wipe history, cookies and recent searches on holster
        public bool adblock { get; set; }
        public string blocker { get; set; }        // sidearm (its own EasyList engine) or ublock (uBlock Origin Lite, experimental)
        public bool quickBar { get; set; }         // the floating bar with its own key
        public string quickHotkey { get; set; }
        public string quickPosition { get; set; }  // mouse, center or top
        public string currency { get; set; }       // what "100 usd" converts into
        public string translateMode { get; set; }  // proxy (Google's translate.goog copy of the page) or inplace
        public string videoBounds { get; set; }    // where the video pop-out was last: "x,y,w,h"
        public bool preferDark { get; set; }
        public bool updateCheck { get; set; }      // 0.8: ask GitHub once a day whether there's a newer Sidearm
        public string lastUpdateCheck { get; set; } // when that last happened (UTC, ISO 8601)
        public bool snapEdges { get; set; }        // 0.8: a dropped window jumps to a screen edge within 24 px
        public bool youtubeAds { get; set; }       // 0.8: skip YouTube's video ads (Sidearm's own blocker)
        public bool shortcutsOffered { get; set; } // 0.8.1: asked once whether to put Sidearm on the desktop and in Start
        public bool popsHide { get; set; }         // 0.9: popped-out pages and videos hide while Sidearm is holstered
        public string pageBounds { get; set; }     // 0.9: where a popped-out page goes: "x,y,w,h" (one place for all of them)
        public bool raven { get; set; }            // 0.9: Raven, messages to friends' Sidearms (off until you turn it on)
        public string ravenName { get; set; }      // what the feature is called: raven, courier, radio, pigeon, dispatch
        public string callsign { get; set; }       // the name your friends see unless they rename you
        public int avatar { get; set; }            // 0-4: one of the five pictures in uivatars
        public string ravenHome { get; set; }      // unread on the start page: pinned (pinned friends first, else everyone), all, off
        public bool ravenTabs { get; set; }        // the side tabs outside the window for whoever wrote
        public string quickReplies { get; set; }   // up to 5, separated by |; "" = the language's defaults
        public string ravenPos { get; set; }       // 0.9.5: where Raven's window was dragged to: "x,y"; "" = beside Sidearm
        public string shotFolder { get; set; }     // 0.9.5: where "Save" puts screenshots and drawings; "" = Pictures\Sidearm
        public string downloadFolder { get; set; } // 0.9.5: mass download; "" = Downloads\<site>
        public string selectColor { get; set; }    // 0.9.5: the Alt + drag box; "" = your accent, else #rrggbb
        public bool joystick { get; set; }         // 0.9.5: hold or click the middle button on a page for the scroll joystick
        public int joystickSpeed { get; set; }     // 1-10
        public string joystickKey { get; set; }    // "" = none, else a key that starts it where the pointer is (e.g. Alt+J)
        public string ravenServer { get; set; }    // the post office; "" = Sidearm's own (https://sidearm-post.hard11.workers.dev)
        public int defaultsVersion { get; set; }
        public int windowX { get; set; }
        public int windowY { get; set; }
        public int windowWidth { get; set; }
        public int windowHeight { get; set; }

        [ScriptIgnore] public string FilePath;
        [ScriptIgnore] public bool IsFirstRun;

        public Settings()
        {
            hotkey = "Alt+`";
            searchUrl = "https://www.google.com/search?q={q}";
            bangs = DefaultBangs();
            maxTabs = 5;
            autoHolster = true;
            pinned = false;
            opacity = 1.0;
            suspendAfterHideSeconds = 10;
            unloadAfterHideMinutes = 15;
            backgroundTabSuspendSeconds = 30;
            disableGpu = false;
            browserArguments = "";
            devTools = true;
            uiScale = 1.0;
            autoZoom = true;
            autoZoomBelow = 900;
            lookup = true;
            lookupOnDoubleClick = true;
            claudeOpenIn = "app"; // app = Claude desktop app, tab = a Sidearm tab, browser = your default browser
            theme = "dark";
            tabsPosition = "top";
            zenPath = "";
            middleClickGlance = true;
            sendTo = "auto"; browserPath = "";
            aiProvider = "claude";
            translateTo = "uk"; autoTranslate = "offer"; neverTranslate = "uk, en";
            accent = ""; accent2 = ""; tint = 0; grain = 0;
            spareTab = true; warmStart = true;
            language = "en";
            gestures = true;
            gestureLeft = "back"; gestureRight = "forward"; gestureUp = "top"; gestureDown = "close";
            gestureUpDown = "reload"; gestureDownRight = "home"; gestureDownLeft = "newTab";
            gestureCircle = "shot"; gestureCircleBack = "draw"; gestureSquare = "popPage"; gestureTriangle = "reader";
            gestureCheck = "send"; gestureZ = "ghost"; gestureZigzag = "mute"; myGestures = "";
            ghostHotkey = "Alt+G";
            ghostOpacity = 0.6;
            muteByDefault = true;
            cleanLinks = true;
            amnesia = false;
            adblock = true;
            blocker = "sidearm";
            quickBar = true; quickHotkey = "Alt+Space"; quickPosition = "mouse";
            currency = "UAH";
            translateMode = "proxy";
            videoBounds = "";
            popsHide = false; pageBounds = "";
            raven = false; ravenName = "raven"; callsign = ""; avatar = 0; ravenHome = "pinned"; ravenTabs = true; quickReplies = ""; ravenServer = ""; ravenPos = "";
            shotFolder = ""; downloadFolder = ""; selectColor = ""; joystick = true; joystickSpeed = 5; joystickKey = "";
            preferDark = true;
            updateCheck = true; lastUpdateCheck = ""; snapEdges = true; youtubeAds = true;
            defaultsVersion = 0; // a file that lacks this key predates 0.2 and gets the new prefixes added
            windowX = windowY = windowWidth = windowHeight = -1;
        }

        public static Dictionary<string, Bang> DefaultBangs()
        {
            // {q} = your query (URL-encoded). {tl} = translate target: "en" for Cyrillic text, otherwise "uk".
            // {sl} = translate source: "uk" for Cyrillic text, otherwise "en".
            var b = new Dictionary<string, Bang>();
            b["w"] = new Bang("Wikipedia (EN)", "https://en.wikipedia.org/w/index.php?search={q}");
            b["wu"] = new Bang("Wikipedia (UK)", "https://uk.wikipedia.org/w/index.php?search={q}");
            b["i"] = new Bang("Google Images", "https://www.google.com/search?tbm=isch&q={q}");
            b["y"] = new Bang("YouTube", "https://www.youtube.com/results?search_query={q}");
            b["t"] = new Bang("Google Translate", "https://translate.google.com/?sl=auto&tl={tl}&text={q}&op=translate");
            b["m"] = new Bang("Google Maps", "https://www.google.com/maps/search/{q}");
            b["r"] = new Bang("Reddit", "https://www.reddit.com/search/?q={q}");
            b["d"] = new Bang("Dictionary", "https://www.google.com/search?q=define+{q}");
            b["ddg"] = new Bang("DuckDuckGo", "https://duckduckgo.com/?q={q}");
            AddV2Bangs(b);
            AddV6Bangs(b);
            return b;
        }

        const int CurrentDefaults = 7;

        /// <summary>What a gesture can do. "off" turns that stroke off.</summary>
        public static readonly string[] GestureActions =
            { "back", "forward", "close", "top", "bottom", "reload", "zen", "zenPrivate", "closeTab", "newTab", "reopen", "home",
              "popPage", "shot", "draw", "clips", "send", "raven", "ghost", "joystick", "mute", "reader", "translate", "off" };

        /// <summary>Stroke (L, R, U, D, UD, DR, DL) or shape (@circle…) to action, without the ones switched off.</summary>
        public Dictionary<string, string> GestureMap()
        {
            var map = new Dictionary<string, string>();
            Action<string, string> add = (k, v) => { if (!string.IsNullOrEmpty(v) && v != "off") map[k] = v; };
            add("L", gestureLeft); add("R", gestureRight); add("U", gestureUp); add("D", gestureDown);
            add("UD", gestureUpDown); add("DR", gestureDownRight); add("DL", gestureDownLeft);
            add("@circle", gestureCircle); add("@circleBack", gestureCircleBack); add("@square", gestureSquare);
            add("@triangle", gestureTriangle); add("@check", gestureCheck); add("@z", gestureZ); add("@zigzag", gestureZigzag);
            return map;
        }

        /// <summary>Your own gestures, checked: at most 12, each a name, an action and 16-64 points.</summary>
        public static string CleanMyGestures(string json)
        {
            var list = new List<Dictionary<string, object>>();
            var arr = Json.ParseAny(json ?? "") as object[];
            if (arr != null)
                foreach (var o in arr)
                {
                    var d = o as Dictionary<string, object>;
                    if (d == null || list.Count >= 12) continue;
                    string name = CleanCallsign(Json.Str(d, "name"));
                    string action = Json.Str(d, "action");
                    if (name.Length == 0 || Array.IndexOf(GestureActions, action) < 0) continue;
                    var pts = d.ContainsKey("pts") ? d["pts"] as object[] : null;
                    if (pts == null || pts.Length < 16 || pts.Length > 64) continue;
                    var clean = new List<double[]>();
                    foreach (var p in pts)
                    {
                        var xy = p as object[];
                        if (xy == null || xy.Length != 2) { clean = null; break; }
                        double x, y;
                        try { x = Convert.ToDouble(xy[0], System.Globalization.CultureInfo.InvariantCulture); y = Convert.ToDouble(xy[1], System.Globalization.CultureInfo.InvariantCulture); }
                        catch (Exception) { clean = null; break; }
                        if (double.IsNaN(x) || double.IsNaN(y) || Math.Abs(x) > 1000 || Math.Abs(y) > 1000) { clean = null; break; }
                        clean.Add(new[] { Math.Round(x, 2), Math.Round(y, 2) });
                    }
                    if (clean == null) continue;
                    list.Add(new Dictionary<string, object> { { "name", name }, { "action", action }, { "pts", clean } });
                }
            return list.Count == 0 ? "" : Json.Serialize(list);
        }

        static string ValidGesture(string v, string fallback)
        {
            return v != null && Array.IndexOf(GestureActions, v) >= 0 ? v : fallback;
        }

        static void AddV2Bangs(Dictionary<string, Bang> b)
        {
            if (!b.ContainsKey("tt")) b["tt"] = new Bang("DeepL", "https://www.deepl.com/translator#{sl}/{tl}/{q}");
            if (!b.ContainsKey("c")) b["c"] = new Bang("Ask Claude", "https://claude.ai/new?q={q}");
        }

        static void AddV6Bangs(Dictionary<string, Bang> b)
        {
            if (!b.ContainsKey("a")) b["a"] = new Bang("Ask AI (your pick)", "https://sidearm.example/ai?q={q}");
            if (!b.ContainsKey("gpt")) b["gpt"] = new Bang("ChatGPT", "https://chatgpt.com/?q={q}");
            if (!b.ContainsKey("px")) b["px"] = new Bang("Perplexity", "https://www.perplexity.ai/search?q={q}");
        }

        static readonly string[] SendTargets = { "auto", "default", "zen", "chrome", "edge", "firefox", "brave", "opera", "vivaldi" };
        static readonly string[] AiIds = { "claude", "chatgpt", "gemini", "perplexity", "copilot" };

        static bool IsHex(string v) { return v != null && System.Text.RegularExpressions.Regex.IsMatch(v, "^#[0-9a-fA-F]{6}$"); }

        public static Settings Load(string path)
        {
            Settings s = null;
            bool existed = File.Exists(path);
            if (existed)
            {
                try
                {
                    s = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(path, Encoding.UTF8));
                }
                catch (Exception ex)
                {
                    Log.Error("settings.json is not valid JSON; using defaults (your file is kept as settings.bad.json)", ex);
                    try { File.Copy(path, Path.ChangeExtension(path, ".bad.json"), true); } catch (Exception) { }
                }
            }
            if (s == null)
            {
                s = new Settings();
                if (!existed)
                {
                    // first run: Windows' language, then anything in defaults.json next to the exe (the shared zip sets Ukrainian there)
                    s.language = Lang.FromWindows();
                    try
                    {
                        string defaults = Path.Combine(Program.AppDir, "defaults.json");
                        if (File.Exists(defaults))
                            foreach (var kv in new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(defaults, Encoding.UTF8)))
                                s.Set(kv.Key, kv.Value);
                    }
                    catch (Exception ex) { Log.Error("defaults.json", ex); }
                }
            }
            if (s.bangs == null) s.bangs = DefaultBangs();
            if (string.IsNullOrWhiteSpace(s.searchUrl) || !s.searchUrl.Contains("{q}")) s.searchUrl = new Settings().searchUrl;
            if (s.maxTabs < 1) s.maxTabs = 1;
            if (s.maxTabs > 10) s.maxTabs = 10;
            if (s.opacity < 0.3 || s.opacity > 1) s.opacity = 1;
            if (s.suspendAfterHideSeconds < 1) s.suspendAfterHideSeconds = 1;
            if (s.backgroundTabSuspendSeconds < 5) s.backgroundTabSuspendSeconds = 5;
            if (s.browserArguments == null) s.browserArguments = "";
            if (s.uiScale < 0.75 || s.uiScale > 2) s.uiScale = 1.0;
            if (s.claudeOpenIn != "browser" && s.claudeOpenIn != "tab") s.claudeOpenIn = "app";
            if (s.autoZoomBelow < 300) s.autoZoomBelow = 900;
            if (s.theme != "light" && s.theme != "system") s.theme = "dark";
            if (s.tabsPosition != "bottom") s.tabsPosition = "top";
            if (s.zenPath == null) s.zenPath = "";
            if (string.IsNullOrEmpty(s.browserPath) && s.zenPath.Length > 0) { s.browserPath = s.zenPath; if (s.defaultsVersion < 6) s.sendTo = "zen"; }
            s.zenPath = "";
            if (s.browserPath == null) s.browserPath = "";
            if (Array.IndexOf(SendTargets, s.sendTo) < 0) s.sendTo = "auto";
            if (Array.IndexOf(AiIds, s.aiProvider) < 0) s.aiProvider = "claude";
            if (string.IsNullOrWhiteSpace(s.translateTo)) s.translateTo = "uk";
            if (s.autoTranslate != "off" && s.autoTranslate != "always") s.autoTranslate = "offer";
            if (s.language != "uk") s.language = "en";
            if (s.neverTranslate == null) s.neverTranslate = "uk, en";
            if (!IsHex(s.accent)) s.accent = "";
            if (!IsHex(s.accent2)) s.accent2 = "";
            s.tint = Math.Max(0, Math.Min(100, s.tint));
            s.grain = Math.Max(0, Math.Min(100, s.grain));
            if (s.defaultsVersion < 6) { AddV6Bangs(s.bangs); s.spareTab = true; s.warmStart = true; }
            var d = new Settings();
            s.gestureLeft = ValidGesture(s.gestureLeft, d.gestureLeft);
            s.gestureRight = ValidGesture(s.gestureRight, d.gestureRight);
            s.gestureUp = ValidGesture(s.gestureUp, d.gestureUp);
            s.gestureDown = ValidGesture(s.gestureDown, d.gestureDown);
            s.gestureUpDown = ValidGesture(s.gestureUpDown, d.gestureUpDown);
            s.gestureDownRight = ValidGesture(s.gestureDownRight, d.gestureDownRight);
            s.gestureDownLeft = ValidGesture(s.gestureDownLeft, d.gestureDownLeft);
            if (s.defaultsVersion < 7)
            {
                // 0.9.5: ↓← opens a new tab and ↓→ the start page, unless you'd changed them
                if (s.gestureDownRight == "zen") s.gestureDownRight = "home";
                if (s.gestureDownLeft == "zenPrivate") s.gestureDownLeft = "newTab";
            }
            s.gestureCircle = ValidGesture(s.gestureCircle, d.gestureCircle);
            s.gestureCircleBack = ValidGesture(s.gestureCircleBack, d.gestureCircleBack);
            s.gestureSquare = ValidGesture(s.gestureSquare, d.gestureSquare);
            s.gestureTriangle = ValidGesture(s.gestureTriangle, d.gestureTriangle);
            s.gestureCheck = ValidGesture(s.gestureCheck, d.gestureCheck);
            s.gestureZ = ValidGesture(s.gestureZ, d.gestureZ);
            s.gestureZigzag = ValidGesture(s.gestureZigzag, d.gestureZigzag);
            s.myGestures = CleanMyGestures(s.myGestures);
            if (s.shotFolder == null) s.shotFolder = "";
            if (s.downloadFolder == null) s.downloadFolder = "";
            if (!IsHex(s.selectColor)) s.selectColor = "";
            s.joystickSpeed = Math.Max(1, Math.Min(10, s.joystickSpeed == 0 ? 5 : s.joystickSpeed));
            if (Array.IndexOf(JoystickKeys, s.joystickKey) < 0) s.joystickKey = "";
            if (string.IsNullOrWhiteSpace(s.ghostHotkey)) s.ghostHotkey = d.ghostHotkey;
            if (s.ghostOpacity < 0.2 || s.ghostOpacity > 0.9) s.ghostOpacity = d.ghostOpacity;
            if (s.blocker != "ublock") s.blocker = "sidearm";
            if (string.IsNullOrWhiteSpace(s.quickHotkey)) s.quickHotkey = d.quickHotkey;
            if (s.quickPosition != "center" && s.quickPosition != "top") s.quickPosition = "mouse";
            if (s.currency == null || !System.Text.RegularExpressions.Regex.IsMatch(s.currency, "^[A-Z]{3}$")) s.currency = "UAH";
            if (s.translateMode != "inplace") s.translateMode = "proxy";
            if (s.videoBounds == null) s.videoBounds = "";
            if (s.pageBounds == null) s.pageBounds = "";
            if (Array.IndexOf(RavenNames, s.ravenName) < 0) s.ravenName = "raven";
            s.callsign = CleanCallsign(s.callsign);
            s.avatar = Math.Max(0, Math.Min(4, s.avatar));
            if (s.ravenHome != "all" && s.ravenHome != "off") s.ravenHome = "pinned";
            s.quickReplies = CleanReplies(s.quickReplies);
            if (s.ravenServer == null) s.ravenServer = "";
            if (s.ravenPos == null) s.ravenPos = "";
            if (s.lastUpdateCheck == null) s.lastUpdateCheck = "";
            if (s.defaultsVersion < 2) AddV2Bangs(s.bangs);
            if (s.defaultsVersion < 3 && s.claudeOpenIn == "tab") s.claudeOpenIn = "app"; // 0.2.0 wrote "tab" by mistake
            if (s.defaultsVersion < 4) s.middleClickGlance = true; // new in 0.4
            s.defaultsVersion = CurrentDefaults;
            s.FilePath = path;
            s.IsFirstRun = !existed;
            s.Save(); // writes defaults on first run and adds any new keys after an update
            return s;
        }

        public static readonly string[] JoystickKeys = { "Alt+J", "Alt+Q", "CapsLock", "F2" };

        public static readonly string[] RavenNames = { "raven", "courier", "radio", "pigeon", "dispatch" };

        /// <summary>A call sign: one line, at most 24 characters.</summary>
        public static string CleanCallsign(string s)
        {
            s = System.Text.RegularExpressions.Regex.Replace(s ?? "", @"[\p{C}\s]+", " ").Trim();
            return s.Length > 24 ? s.Substring(0, 24).Trim() : s;
        }

        /// <summary>Quick replies: up to 5, each one line of at most 40 characters, separated by |.</summary>
        public static string CleanReplies(string s)
        {
            var list = (s ?? "").Split('|').Select(x => System.Text.RegularExpressions.Regex.Replace(x, @"[\p{C}\s]+", " ").Trim())
                .Where(x => x.Length > 0).Select(x => x.Length > 40 ? x.Substring(0, 40) : x).Distinct().Take(5);
            return string.Join("|", list);
        }

        public static DateTime LastSelfSave = DateTime.MinValue;

        /// <summary>Copies every setting from another instance (used when the file is edited outside Sidearm).</summary>
        public void CopyFrom(Settings other, bool includeWindow)
        {
            foreach (var p in typeof(Settings).GetProperties())
            {
                if (!p.CanWrite || !p.CanRead) continue;
                if (!includeWindow && p.Name.StartsWith("window", StringComparison.Ordinal)) continue;
                p.SetValue(this, p.GetValue(other, null), null);
            }
        }

        /// <summary>Sets one setting by name from a value that came from JSON (the settings page).</summary>
        public bool Set(string name, object value)
        {
            var p = typeof(Settings).GetProperty(name);
            if (p == null || !p.CanWrite || name == "defaultsVersion" || name == "bangs") return false;
            try
            {
                object v;
                if (p.PropertyType == typeof(bool)) v = Convert.ToBoolean(value);
                else if (p.PropertyType == typeof(int)) v = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
                else if (p.PropertyType == typeof(double)) v = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                else if (p.PropertyType == typeof(string)) v = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
                else return false;
                if (name.StartsWith("gesture", StringComparison.Ordinal) && p.PropertyType == typeof(string)
                    && Array.IndexOf(GestureActions, (string)v) < 0) return false;
                if (name == "ghostOpacity") v = Math.Max(0.2, Math.Min(0.9, (double)v));
                if ((name == "accent" || name == "accent2" || name == "selectColor") && (string)v != "" && !IsHex((string)v)) return false;
                if (name == "joystickSpeed") v = Math.Max(1, Math.Min(10, (int)v));
                if (name == "myGestures") v = CleanMyGestures((string)v);
                if (name == "joystickKey" && (string)v != "" && Array.IndexOf(JoystickKeys, (string)v) < 0) return false;
                if ((name == "tint" || name == "grain") ) v = Math.Max(0, Math.Min(100, (int)v));
                if (name == "sendTo" && Array.IndexOf(SendTargets, (string)v) < 0) return false;
                if (name == "aiProvider" && Array.IndexOf(AiIds, (string)v) < 0) return false;
                if (name == "autoTranslate" && (string)v != "off" && (string)v != "offer" && (string)v != "always") return false;
                if (name == "language" && (string)v != "en" && (string)v != "uk") return false;
                if (name == "blocker" && (string)v != "sidearm" && (string)v != "ublock") return false;
                if (name == "quickPosition" && (string)v != "mouse" && (string)v != "center" && (string)v != "top") return false;
                if (name == "translateMode" && (string)v != "proxy" && (string)v != "inplace") return false;
                if (name == "ravenName" && Array.IndexOf(RavenNames, (string)v) < 0) return false;
                if (name == "ravenHome" && (string)v != "pinned" && (string)v != "all" && (string)v != "off") return false;
                if (name == "avatar") v = Math.Max(0, Math.Min(4, (int)v));
                if (name == "callsign") v = CleanCallsign((string)v);
                if (name == "quickReplies") v = CleanReplies((string)v);
                if (name == "currency" && !System.Text.RegularExpressions.Regex.IsMatch((string)v, "^[A-Z]{3}$")) return false;
                if ((name == "hotkey" || name == "ghostHotkey" || name == "quickHotkey") && ((string)v).Trim().Length == 0) return false;
                p.SetValue(this, v, null);
                return true;
            }
            catch (Exception) { return false; }
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(FilePath)) return;
            LastSelfSave = DateTime.UtcNow;
            try
            {
                string json = Json.Pretty(new JavaScriptSerializer().Serialize(this));
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch (Exception ex) { Log.Error("Could not save settings", ex); }
        }
    }

    internal static class Json
    {
        static readonly JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };

        public static string Serialize(object o) { return serializer.Serialize(o); }

        public static Dictionary<string, object> Parse(string json)
        {
            try { return serializer.Deserialize<Dictionary<string, object>>(json); }
            catch (Exception) { return null; }
        }

        /// <summary>Any JSON value: objects come back as dictionaries, arrays as object[]; null if it doesn't parse.</summary>
        public static object ParseAny(string json)
        {
            try { return serializer.DeserializeObject(json); }
            catch (Exception) { return null; }
        }

        public static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : null;
        }

        public static double Num(Dictionary<string, object> d, string key, double fallback)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            try { return Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture); }
            catch (Exception) { return fallback; }
        }

        public static bool Bool(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v is bool && (bool)v;
        }

        /// <summary>Indents compact JSON so settings.json is pleasant to edit by hand.</summary>
        public static string Pretty(string json)
        {
            var sb = new StringBuilder();
            int indent = 0;
            bool inString = false, escaped = false;
            foreach (char c in json)
            {
                if (inString)
                {
                    sb.Append(c);
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inString = true; sb.Append(c); break;
                    case '{':
                    case '[':
                        sb.Append(c); indent++; sb.Append('\n').Append(' ', indent * 2); break;
                    case '}':
                    case ']':
                        indent--; sb.Append('\n').Append(' ', indent * 2).Append(c); break;
                    case ',': sb.Append(c).Append('\n').Append(' ', indent * 2); break;
                    case ':': sb.Append(": "); break;
                    default: if (!char.IsWhiteSpace(c)) sb.Append(c); break;
                }
            }
            // collapse empty containers like "{\n  }"
            return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"([\{\[])\s+([\}\]])", "$1$2") + "\n";
        }
    }

    internal static class Log
    {
        static string path;
        static readonly object gate = new object();

        public static void Init(string file)
        {
            path = file;
            try
            {
                var fi = new FileInfo(file);
                if (fi.Exists && fi.Length > 1024 * 1024)
                {
                    string old = file + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(file, old);
                }
            }
            catch (Exception) { }
        }

        public static void Info(string msg) { Write("INFO ", msg); }

        public static void Error(string msg, Exception ex)
        {
            Write("ERROR", ex == null ? msg : msg + " :: " + ex.GetType().Name + ": " + ex.Message + "\n    " + (ex.StackTrace ?? "").Replace("\n", "\n    "));
        }

        static void Write(string level, string msg)
        {
            if (path == null) return;
            lock (gate)
            {
                try { File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + level + " " + msg + Environment.NewLine); }
                catch (Exception) { }
            }
        }
    }
}
