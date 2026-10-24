using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace Sidearm
{
    /// <summary>
    /// The window: a frameless, always-on-top shell holding two kinds of WebView2:
    /// the UI strip on top (ui\chrome.html) and one view per tab below it.
    /// </summary>
    internal sealed partial class MainForm : Form
    {
        public const string Host = "sidearm.example";
        public const string UiOrigin = "https://" + Host + "/";
        public const string NewTabUrl = UiOrigin + "newtab.html";

        const int HotkeyId = 0x5A1D;

        readonly Settings S;
        readonly EventWaitHandle drawSignal;
        readonly int ownPid = Process.GetCurrentProcess().Id;

        CoreWebView2Environment env;
        WebView2 ui;
        readonly Panel content;
        NotifyIcon tray;
        ToolStripMenuItem trayPin, trayAutostart;
        Task initTask;
        string injectJs = "";

        bool uiReady, quitting, fullscreen, dragging, unloaded, hotkeyOk, boundsApplied;
        int chromePx = -1, uiPx = -1; // device px reported by the UI page
        DateTime lastDraw = DateTime.MinValue;
        readonly List<string> uiQueue = new List<string>();

        readonly System.Windows.Forms.Timer memTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        readonly System.Windows.Forms.Timer sleepTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer unloadTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer dragGuard = new System.Windows.Forms.Timer { Interval = 800 }; // a move that never started
        readonly System.Windows.Forms.Timer deactivateTimer = new System.Windows.Forms.Timer { Interval = 150 };
        readonly System.Windows.Forms.Timer saveTimer = new System.Windows.Forms.Timer { Interval = 800 };

        public MainForm(Settings settings, EventWaitHandle drawSignal)
        {
            S = settings;
            this.drawSignal = drawSignal;
            Lang.Load(S.language);

            Text = "Sidearm";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = FrameColor;
            DoubleBuffered = true;
            KeyPreview = true;
            MinimumSize = new Size(320, 360);
            LoadIcons();

            content = new Panel { BackColor = PageColor };
            Controls.Add(content);

            memTimer.Tick += (s, e) => UpdateMemory();
            sleepTimer.Tick += (s, e) => { sleepTimer.Stop(); SleepAll(); };
            unloadTimer.Tick += (s, e) => { unloadTimer.Stop(); UnloadAll(); };
            dragGuard.Tick += (s, e) => { dragGuard.Stop(); dragging = false; };
            deactivateTimer.Tick += (s, e) => { deactivateTimer.Stop(); MaybeAutoHolster(); };
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); S.Save(); SaveSession(); };
            InitQuickSleep();
            InitPaintCheck();
            InitTimers();
            InitClips();
            InitRaven();

            try { injectJs = File.ReadAllText(Path.Combine(Program.UiDir, "inject.js")); }
            catch (Exception ex) { Log.Error("Could not read ui\\inject.js", ex); }
        }

        Icon smallIcon;

        void LoadIcons()
        {
            string ico = Path.Combine(Program.AppDir, "sidearm.ico");
            try
            {
                if (File.Exists(ico))
                {
                    Icon = new Icon(ico, SystemInformation.IconSize);
                    smallIcon = new Icon(ico, SystemInformation.SmallIconSize);
                    return;
                }
            }
            catch (Exception) { }
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); smallIcon = Icon; } catch (Exception) { }
        }

        // ───────────────────────── boot ─────────────────────────

        public void Boot(bool startInTray)
        {
            var handle = Handle; // creates the window so the hotkey has somewhere to land
            ApplyBounds();
            SetupTray();
            InitRates();
            InitUpdates();
            try { Shortcuts.FollowThisCopy(); } catch (Exception ex) { Log.Error("Shortcuts", ex); } // a copy in a new folder takes its shortcuts along
            ThreadPool.RegisterWaitForSingleObject(drawSignal, (state, timedOut) =>
            {
                try { BeginInvoke(new Action(Draw)); } catch (Exception) { }
            }, null, Timeout.Infinite, false);

            if (!hotkeyOk)
                ShowBalloon("Hotkey unavailable", S.hotkey + " is taken by another app. Change \"hotkey\" in settings.json.");

            if (startInTray)
            {
                ShowBalloon("Sidearm is holstered", "Press " + S.hotkey + " to draw.");
                if (S.warmStart) WarmStartSoon();
            }
            else
                Draw();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW; // no taskbar button, no Alt+Tab clutter
                cp.ClassStyle |= Native.CS_DROPSHADOW;
                if (ghost) cp.ExStyle |= 0x00000020 | 0x00080000; // WS_EX_TRANSPARENT | WS_EX_LAYERED: clicks pass through
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyAccentFrame();
            RegisterHotkey();
            RegisterQuickHotkey();
            if (S.opacity < 1) Opacity = S.opacity;
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            Native.UnregisterHotKey(Handle, HotkeyId);
            Native.UnregisterHotKey(Handle, GhostHotkeyId);
            Native.UnregisterHotKey(Handle, QuickHotkeyId);
            base.OnHandleDestroyed(e);
        }

        void RegisterHotkey()
        {
            uint mods, vk;
            if (!Native.ParseHotkey(S.hotkey, out mods, out vk))
            {
                Log.Info("Could not parse hotkey \"" + S.hotkey + "\", falling back to Alt+`");
                S.hotkey = "Alt+`";
                Native.ParseHotkey(S.hotkey, out mods, out vk);
            }
            Native.UnregisterHotKey(Handle, HotkeyId);
            hotkeyOk = Native.RegisterHotKey(Handle, HotkeyId, mods | Native.MOD_NOREPEAT, vk);
            if (!hotkeyOk) Log.Info("RegisterHotKey failed for " + S.hotkey + " (error " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ")");
            else Log.Info("Hotkey registered: " + S.hotkey);
        }

        Task EnsureInitialized()
        {
            if (initTask == null) initTask = InitializeAsync();
            return initTask;
        }

        async Task InitializeAsync()
        {
            try
            {
                string args = (S.browserArguments ?? "").Trim();
                if (S.disableGpu) args = (args + " --disable-gpu").Trim();
                var options = new CoreWebView2EnvironmentOptions(args.Length > 0 ? args : null);
                options.AreBrowserExtensionsEnabled = true; // Modern for Wikipedia, uBlock Origin Lite
                if (S.language == "uk") options.Language = "uk"; // pages (Google first of all) answer in Ukrainian, not Windows' language
                Sites.Load(Path.Combine(Program.DataDir, "sites.json"));
                InitPages();
                env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Program.DataDir, "webview"), options);
                Log.Info("WebView2 environment ready, browser " + env.BrowserVersionString);

                ui = new WebView2 { DefaultBackgroundColor = FrameColor };
                Controls.Add(ui);
                ui.KeyDown += OnViewKeyDown;
                var forceHandle = ui.Handle;
                LayoutChildren();
                await ui.EnsureCoreWebView2Async(env);
                if (Math.Abs(S.uiScale - 1.0) > 0.01) ui.ZoomFactor = S.uiScale;
                ConfigureUi(ui.CoreWebView2);
                ApplyColorScheme();
                if (S.adblock && S.blocker == "ublock") SyncUblock(); // falls back to Sidearm's own lists if it can't start
                else Adblock.Start();
                ui.CoreWebView2.Navigate(UiOrigin + "chrome.html");

                RestoreSession();
                if (active != null) await ActivateTab(active, true);
                ApplyTabsPosition();
                LayoutChildren();
            }
            catch (Exception ex)
            {
                Log.Error("Initialization failed", ex);
                MessageBox.Show("Sidearm couldn't start its browser engine:\n\n" + ex.Message + "\n\nDetails are in data\\sidearm.log.", "Sidearm", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void ConfigureUi(CoreWebView2 core)
        {
            var s = core.Settings;
            s.AreDevToolsEnabled = S.devTools;
            s.AreBrowserAcceleratorKeysEnabled = false;
            s.IsZoomControlEnabled = false;
            s.IsStatusBarEnabled = false;
            s.IsSwipeNavigationEnabled = false;
            s.IsPinchZoomEnabled = false;
            s.IsGeneralAutofillEnabled = false;
            s.IsPasswordAutosaveEnabled = false;
            core.SetVirtualHostNameToFolderMapping(Host, Program.UiDir, CoreWebView2HostResourceAccessKind.DenyCors);
            core.WebMessageReceived += OnUiMessage;
            core.NavigationStarting += (sender, e) =>
            {
                // The strip only ever shows our own page; anything else goes to a tab.
                if (!e.Uri.StartsWith(UiOrigin, StringComparison.OrdinalIgnoreCase))
                {
                    e.Cancel = true;
                    Navigate(e.Uri, false);
                }
            };
            core.NewWindowRequested += (sender, e) => { e.Handled = true; Navigate(e.Uri, true); };
            core.ProcessFailed += (sender, e) =>
            {
                Log.Info("UI process failed: " + e.ProcessFailedKind + ", reloading strip");
                uiReady = false;
                try { core.Reload(); } catch (Exception) { }
            };
        }

        // ───────────────────────── draw / holster ─────────────────────────

        public async void Draw() { await DrawAsync(); }

        async Task DrawAsync()
        {
            try
            {
                sleepTimer.Stop();
                unloadTimer.Stop();
                bool first = initTask == null;
                bool wasHidden = !Visible;
                RememberGameWindow();
                StartClipWatch();
                // Show the window first, then wake the views: a view made visible inside a hidden window
                // may not repaint until something forces a layout (the blank strip of 0.7)
                if (!Visible)
                {
                    if (!boundsApplied) ApplyBounds();
                    Show();
                }
                if (!first && wasHidden) await WakeAsync();
                if (wasHidden) ShowFloatsWithSidearm();
                RegisterGhostHotkey(true);
                Native.ForceForeground(Handle);
                Activate();
                lastDraw = DateTime.UtcNow;
                await EnsureInitialized();
                if (first) await WakeAsync();
                NudgeAfterShow();
                FocusBar(true);
                PostToUi(new { type = "drawn" });
                UpdateRavenTabs();
                PrepareSpareSoon();
                UpdateMemory();
                memTimer.Start();
                Rates.RefreshIfOld();
                CheckUpdatesIfDue();
                OfferShortcutsSoon();
                if (S.quickBar && !quickKeyOk && !quickKeyWarned)
                {
                    quickKeyWarned = true;
                    Toast(S.quickHotkey + " is taken by another app. Pick another key for the quick bar in Settings", null, null);
                }
            }
            catch (Exception ex) { Log.Error("Draw failed", ex); }
        }

        public void Holster()
        {
            if (!Visible) return;
            if (ghost) SetGhost(false, false);
            HideFloatsWithSidearm();
            RegisterGhostHotkey(AnyFloatShown()); // 0.9: the ghost key stays for the pop-outs while they're up
            deactivateTimer.Stop();
            SaveBounds();
            PostToUi(new { type = "holstered" });
            CancelLookup();
            HideRavenWindows();
            Hide();
            memTimer.Stop();
            sleepTimer.Interval = Math.Max(1, S.suspendAfterHideSeconds) * 1000;
            sleepTimer.Start();
            if (S.unloadAfterHideMinutes > 0)
            {
                unloadTimer.Interval = Math.Min(int.MaxValue / 2, S.unloadAfterHideMinutes * 60000);
                unloadTimer.Start();
            }
            if (S.amnesia) { AmnesiaWipe(); clips.Clear(); }
        }

        bool IsForegroundOurs()
        {
            int fg = Native.ForegroundProcessId();
            if (fg == ownPid) return true;
            return WebViewPids().Contains(fg);
        }

        HashSet<int> WebViewPids()
        {
            var set = new HashSet<int>();
            if (env == null) return set;
            try { foreach (var p in env.GetProcessInfos()) set.Add(p.ProcessId); }
            catch (Exception) { }
            return set;
        }

        void OnHotkey()
        {
            if (!Visible) { Draw(); return; }
            if (ghost) { SetGhost(false); Draw(); return; } // the draw key also brings a ghost back to solid
            if ((DateTime.UtcNow - lastDraw).TotalMilliseconds < 450) { ClipboardSearch(); return; }
            if (IsForegroundOurs()) Holster();
            else Draw();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (S.autoHolster && !S.pinned && Visible && !dragging && !ghost) deactivateTimer.Start();
            // pinned: a game that grabs "topmost" for itself can push Sidearm under it; take the top back once
            if (S.pinned && Visible) BeginInvoke(new Action(() => { if (Visible) Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE); }));
        }

        void MaybeAutoHolster()
        {
            if (!Visible || S.pinned || dragging || fullscreen || ghost) return;
            if (IsForegroundOurs()) return; // focus only moved into a web view, a dropdown or a context menu
            Holster();
        }

        /// <summary>Suspend every page while hidden: scripts stop, memory drops.</summary>
        async void SleepAll()
        {
            if (Visible) return;
            DropSpare(); // the spare engine only earns its memory while you're using Sidearm
            int count = 0;
            var views = new List<WebView2>();
            if (ui != null && !S.raven) views.Add(ui); // 0.9: with Raven on, the strip stays awake (it holds the connection)
            if (tabsView != null) views.Add(tabsView);
            if (glance != null && glance.View != null) views.Add(glance.View);
            foreach (var t in tabs) if (t.View != null) views.Add(t.View);
            foreach (var f in Floats()) if (!f.Visible && f.View != null) views.Add(f.View); // hidden with Sidearm (Settings)
            foreach (var v in views)
            {
                if (Visible) return;
                if (await TrySuspend(v)) count++;
            }
            Log.Info("Holstered: suspended " + count + " of " + views.Count + " views");
        }

        /// <summary>Views with a suspend in flight: closing one of those mid-suspend is a way to crash the engine.</summary>
        static readonly HashSet<WebView2> suspending = new HashSet<WebView2>();

        static async Task<bool> TrySuspend(WebView2 v)
        {
            if (v == null || v.IsDisposed || v.Disposing || retiring.Contains(v)) return false;
            suspending.Add(v);
            try
            {
                if (v.CoreWebView2 == null || v.CoreWebView2.IsSuspended) return false;
                SetViewVisible(v, false); // a visible WebView refuses to suspend
                return await v.CoreWebView2.TrySuspendAsync();
            }
            catch (Exception ex) { Log.Error("TrySuspend failed", ex); return false; }
            finally { suspending.Remove(v); }
        }

        static readonly HashSet<WebView2> retiring = new HashSet<WebView2>();

        /// <summary>
        /// 0.8.1: closes a view safely. It's hidden and taken off the window now, but only disposed a moment later,
        /// outside whatever engine event we're inside (a new-window request, a navigation) and after any suspend
        /// in flight has finished. Disposing a view in the middle of those crashed the engine (EmbeddedBrowserWebView.dll).
        /// </summary>
        static void RetireView(WebView2 v, string why)
        {
            if (v == null || v.IsDisposed || retiring.Contains(v)) return;
            retiring.Add(v);
            try { SetViewVisible(v, false); if (v.Parent != null) v.Parent.Controls.Remove(v); } catch (Exception) { }
            var t = new System.Windows.Forms.Timer { Interval = 400 };
            int waited = 0;
            t.Tick += (s, e) =>
            {
                waited += t.Interval;
                if (suspending.Contains(v) && waited < 5000) return; // let the suspend finish first
                t.Stop(); t.Dispose();
                retiring.Remove(v);
                try { if (!v.IsDisposed) v.Dispose(); }
                catch (Exception ex) { Log.Error("Dispose view (" + why + ")", ex); }
            };
            t.Start();
        }

        /// <summary>Long holster: close the tab engines entirely, keep only their URLs.</summary>
        void UnloadAll()
        {
            if (Visible) return;
            CloseGlance();
            int n = 0;
            foreach (var t in tabs) if (t.View != null) { DisposeView(t); n++; }
            if (quick != null && !quick.Visible) DropQuick(); // it's rebuilt the next time you call it up
            DropRavenWindow();
            unloaded = true;
            Log.Info("Long holster: unloaded " + n + " tab views");
            GC.Collect();
        }

        async Task WakeAsync()
        {
            if (ui != null)
            {
                SetViewVisible(ui, true);
                Resume(ui);
            }
            if (tabsView != null) { SetViewVisible(tabsView, true); Resume(tabsView); }
            if (glance != null && glance.View != null) { SetViewVisible(glance.View, true); Resume(glance.View); }
            if (active != null)
            {
                if (active.View == null) await EnsureView(active, true);
                if (active.View != null)
                {
                    SetViewVisible(active.View, true);
                    active.View.BringToFront();
                    Resume(active.View);
                }
            }
            if (unloaded) { unloaded = false; PushTabs(); }
            LayoutChildren();
        }

        static readonly System.Reflection.FieldInfo ControllerField = typeof(WebView2).GetField("_coreWebView2Controller",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        /// <summary>
        /// WinForms only forwards visibility to the engine when the control's visible state changes,
        /// and a hidden window makes every child read as hidden already. So tell the engine directly.
        /// </summary>
        static void SetViewVisible(WebView2 v, bool on)
        {
            if (v == null || v.IsDisposed || v.Disposing || (on && retiring.Contains(v))) return;
            v.Visible = on;
            try
            {
                var c = ControllerField != null ? ControllerField.GetValue(v) as CoreWebView2Controller : null;
                if (c != null && c.IsVisible != on) c.IsVisible = on;
            }
            catch (Exception ex) { Log.Error("SetViewVisible", ex); }
        }

        static void Resume(WebView2 v)
        {
            try { if (v.CoreWebView2 != null && v.CoreWebView2.IsSuspended) v.CoreWebView2.Resume(); }
            catch (Exception) { }
        }

        void CancelLookup()
        {
            if (active != null) PostToContent(active, new { t = "lookup-cancel" });
        }

        void ClipboardSearch()
        {
            string text = null;
            try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); } catch (Exception) { }
            text = (text ?? "").Trim();
            if (!Visible) Draw();
            if (text.Length == 0) { Toast("Clipboard has no text to search", null, null); return; }
            if (text.Length > 400) text = text.Substring(0, 400);
            text = text.Replace("\r", " ").Replace("\n", " ");
            PostToUi(new { type = "runQuery", text = text });
        }

        void FocusBar(bool selectAll)
        {
            if (ui == null) return;
            try { ui.Focus(); } catch (Exception) { }
            PostToUi(new { type = "focusBar", selectAll = selectAll });
        }

        // ───────────────────────── keyboard ─────────────────────────

        /// <summary>WebView2 forwards accelerator keys (Ctrl/Alt combos, Esc, F-keys) here.</summary>
        void OnViewKeyDown(object sender, KeyEventArgs e)
        {
            if (HandleShortcut(e.KeyData, sender == ui)) { e.Handled = true; e.SuppressKeyPress = true; }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (HandleShortcut(keyData, false)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// Keys that reach Sidearm while it has focus. The modifiers come with the key itself (plus the
        /// window's own keyboard state as a fallback): Sidearm never polls the keyboard.
        /// </summary>
        bool HandleShortcut(Keys keyData, bool fromBar)
        {
            Keys key = keyData & Keys.KeyCode;
            Keys mods = (keyData & Keys.Modifiers) | Control.ModifierKeys;
            bool ctrl = (mods & Keys.Control) != 0;
            bool alt = (mods & Keys.Alt) != 0;
            bool shift = (mods & Keys.Shift) != 0;

            if (alt && !ctrl && (key == Keys.D1 || key == Keys.NumPad1)) { CancelLookup(); ClipboardSearch(); return true; }
            if (alt && !ctrl && !shift && key == Keys.V) { CancelLookup(); ShowClips(); return true; }
            if (alt && !ctrl && key == Keys.R) { CancelLookup(); ToggleReader(active); return true; }
            if (alt && !ctrl && key == Keys.F) { CancelLookup(); ClearFog(active); return true; }
            if (alt && !ctrl && key == Keys.D) { CancelLookup(); FocusBar(true); return true; }
            if (alt && !ctrl && key == Keys.Home) { CancelLookup(); GoHome(); return true; }
            if (alt && !ctrl && key == Keys.M) { CancelLookup(); ToggleMute(); return true; }
            if (alt && !ctrl && key == Keys.T) { CancelLookup(); ToggleTranslate(active); return true; }
            if (alt && shift && !ctrl && key == Keys.P) { CancelLookup(); PopOutPage(active); return true; }
            if (alt && !ctrl && key == Keys.P) { CancelLookup(); PopOutVideo(glance ?? active); return true; }
            if (ctrl && shift && !alt && key == Keys.C && !fromBar) { QuickCopy(); return true; }
            if (alt && !ctrl && key == Keys.Enter && glance != null) { CancelLookup(); OnGlanceCommand(shift ? "newtab" : "promote"); return true; }
            // Ctrl+Enter in the bar sends what you typed (the bar's own script handles it)
            if (ctrl && !alt && key == Keys.Enter && !fromBar) { SendToBrowser(CurrentPageUrl(), shift); return true; }
            if (key == Keys.F6) { FocusBar(true); return true; }
            if (!ctrl || alt) return false;

            switch (key)
            {
                case Keys.T:
                    if (shift) ReopenClosed(); else NewTabAndFocus();
                    return true;
                case Keys.W:
                    if (active != null) CloseTab(active, true);
                    return true;
                case Keys.L:
                    FocusBar(true);
                    return true;
                case Keys.Tab:
                    CycleTab(shift ? -1 : 1);
                    return true;
                case Keys.D1: case Keys.D2: case Keys.D3: case Keys.D4: case Keys.D5:
                case Keys.D6: case Keys.D7: case Keys.D8: case Keys.D9:
                    int i = key - Keys.D1;
                    if (key == Keys.D9) i = tabs.Count - 1;
                    if (i < tabs.Count) Forget(ActivateTab(tabs[i], true));
                    return true;
            }
            return false;
        }

        // ───────────────────────── messages from the UI strip ─────────────────────────

        void OnUiMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (!e.Source.StartsWith(UiOrigin, StringComparison.OrdinalIgnoreCase)) return;
            var m = Json.Parse(e.WebMessageAsJson);
            string type = Json.Str(m, "type");
            if (type == null) return;
            try
            {
                switch (type)
                {
                    case "ready":
                        if (IsTabsViewSender(sender))
                        {
                            tabsReady = true;
                            tabsView.CoreWebView2.PostWebMessageAsJson(Json.Serialize(InitMessage()));
                            PushTabs();
                            break;
                        }
                        uiReady = true;
                        lastRoom = -1;
                        SendInit();
                        PushTabs();
                        if (timers.Count > 0) PushTimers();
                        if (clips.Items.Count > 0) PushClips();
                        StartRavenIfOn(); // the engine lives in the strip: a reloaded strip starts it again
                        foreach (var q in uiQueue) ui.CoreWebView2.PostWebMessageAsJson(q);
                        uiQueue.Clear();
                        if (Visible) { FocusBar(true); UpdateMemory(); }
                        break;
                    case "layout":
                        if (IsTabsViewSender(sender)) { tabsPx = (int)Json.Num(m, "chrome", 0); LayoutChildren(); break; }
                        chromePx = (int)Json.Num(m, "chrome", chromePx);
                        uiPx = (int)Json.Num(m, "full", chromePx);
                        object cutRaw; m.TryGetValue("cut", out cutRaw);
                        var cut = cutRaw as Dictionary<string, object>;
                        uiCut = cut == null ? null : new[] { (int)Json.Num(cut, "x", 0), (int)Json.Num(cut, "w", 0), (int)Json.Num(cut, "r", 0) };
                        LayoutChildren();
                        break;
                    case "zoom":
                        string zop = Json.Str(m, "op");
                        if (zop == "in" || zop == "out")
                        {
                            if (active != null && active.View != null && !active.IsNewTab)
                            {
                                double z = Math.Round(active.View.ZoomFactor + (zop == "in" ? 0.1 : -0.1), 1);
                                active.View.ZoomFactor = Math.Max(0.3, Math.Min(3, z)); // remembered via ZoomFactorChanged
                            }
                        }
                        else ResetZoom();
                        break;
                    case "glance":
                        OnGlanceCommand(Json.Str(m, "op"));
                        break;
                    case "zen":
                        SendToBrowser(Json.Str(m, "url"), Json.Bool(m, "private"));
                        break;
                    case "timer":
                        OnTimerMessage(m);
                        break;
                    case "mute":
                        OnMuteMessage(m);
                        break;
                    case "page":
                        string pop = Json.Str(m, "op");
                        if (pop == "reader") ToggleReader(active);
                        else if (pop == "fog") ClearFog(active);
                        else if (pop == "translate") ToggleTranslate(active);
                        else if (pop == "popout") PopOutVideo(active);
                        else if (pop == "popPage") PopOutPage(active);
                        else if (pop == "ublock") { if (UblockDashboard != null) Navigate(UblockDashboard, true); }
                        else SetSiteOption(pop, Json.Bool(m, "on"));
                        break;
                    case "navigate":
                        Navigate(Json.Str(m, "url"), Json.Bool(m, "newTab"));
                        break;
                    case "tab":
                        OnTabMessage(m);
                        break;
                    case "nav":
                        OnNavMessage(Json.Str(m, "op"));
                        break;
                    case "dragStart":
                        StartDrag();
                        break;
                    case "opacity":
                        double next = Math.Round(Math.Max(0.3, Math.Min(1.0, Opacity + Json.Num(m, "delta", 0))), 2);
                        Opacity = next;
                        S.opacity = next;
                        PostToUi(new { type = "opacity", value = next });
                        SaveSettingsSoon();
                        break;
                    case "pin":
                        SetPinned(Json.Bool(m, "on"));
                        break;
                    case "holster":
                        Holster();
                        break;
                    case "painted":
                        OnStripPainted();
                        break;
                    case "note":
                        AppendNote(Json.Str(m, "text"), true);
                        break;
                    case "away":
                        // the pointer moved from the page onto the strip: the page may not notice, so tell it
                        if (active != null) PostToContent(active, new { t = "away" });
                        if (glance != null) PostToContent(glance, new { t = "away" });
                        break;
                    case "alt":
                        // Alt held in the command bar: pass it to the page under the pointer for a word look-up
                        if (active != null && S.lookup) PostToContent(glance ?? active, new { t = "alt", down = Json.Bool(m, "down") });
                        break;
                    case "copy":
                        string text = Json.Str(m, "text");
                        if (Json.Bool(m, "clean")) CopyLink(text);
                        else if (!string.IsNullOrEmpty(text)) try { Clipboard.SetText(text); } catch (Exception) { }
                        break;
                    case "clip":
                        OnClipMessage(m);
                        break;
                    case "raven":
                        if (!IsTabsViewSender(sender)) OnRavenEvent(m);
                        break;
                    case "ravenSend":
                        if (RavenReady) RavenFromBar(Json.Str(m, "key"), Json.Str(m, "text"), Json.Bool(m, "withPage"));
                        break;
                    case "clipSearch":
                        ClipboardSearch();
                        break;
                    case "focusContent":
                        if (active != null && active.View != null) active.View.Focus();
                        break;
                    case "cmd":
                        string name = Json.Str(m, "name");
                        if (name == "reopen") ReopenClosed();
                        else if (name == "settings") OpenSettingsPage();
                        else if (name == "unmute") SetMuted(glance ?? active, false);
                        else if (name == "translate") ToggleTranslate(active);
                        else if (name == "ghost") ToggleGhost();
                        else if (name == "popout") PopOutVideo(glance ?? active);
                        else if (name == "update") UpdateNow();
                        else if (name == "shortcuts") MakeShortcuts();
                        else if (name == "ravenRequests") OpenRaven("list", null, null);
                        else if (name == "lookupOn" && active != null) { SetSiteOption("lookup", true); Toast("Word look-up is on again for this site", null, null); }
                        break;
                    case "log":
                        Log.Info("ui: " + Json.Str(m, "text"));
                        break;
                }
            }
            catch (Exception ex) { Log.Error("Handling UI message " + type, ex); }
        }

        void SendInit() { PostToUi(InitMessage()); PostToQuick(InitMessage()); S.IsFirstRun = false; }

        object InitMessage()
        {
            return new
            {
                type = "init",
                version = Program.ShownVersion,
                tagline = Program.Tagline,
                hotkey = S.hotkey,
                hotkeyOk = hotkeyOk,
                searchUrl = S.searchUrl,
                bangs = S.bangs,
                maxTabs = S.maxTabs,
                pinned = S.pinned,
                opacity = Opacity,
                firstRun = S.IsFirstRun,
                theme = ThemeName,
                tabsPosition = S.tabsPosition,
                ghostHotkey = S.ghostHotkey,
                lang = Lang.Code,
                gestures = S.gestures ? S.GestureMap() : null,
                browserName = BrowserName,
                aiName = AiName,
                look = new { accent = S.accent, accent2 = S.accent2, tint = S.tint, grain = S.grain },
                rates = Rates.Snapshot(),
                currency = S.currency,
                quickHotkey = S.quickBar ? S.quickHotkey : ""
            };
        }

        void PostToUi(object message)
        {
            string json = Json.Serialize(message);
            if (ui == null || ui.CoreWebView2 == null || !uiReady)
            {
                // State snapshots are re-sent on "ready"; only queue one-off events.
                bool snapshot = json.StartsWith("{\"type\":\"tabs\"") || json.StartsWith("{\"type\":\"mem\"") || json.StartsWith("{\"type\":\"init\"");
                if (!snapshot && uiQueue.Count < 50) uiQueue.Add(json);
                return;
            }
            try { ui.CoreWebView2.PostWebMessageAsJson(json); }
            catch (Exception ex) { Log.Error("PostToUi", ex); }
            PostToTabsView(json);
        }

        void Toast(string text, string actionLabel, string actionCmd)
        {
            object action = actionLabel == null ? null : (object)new { label = Lang.T(actionLabel), cmd = actionCmd };
            PostToUi(new { type = "toast", text = Lang.T(text), action = action });
        }

        void SetPinned(bool on)
        {
            S.pinned = on;
            if (trayPin != null) trayPin.Checked = on;
            PostToUi(new { type = "pinned", on = on });
            SaveSettingsSoon();
        }

        void UpdateMemory()
        {
            if (!Visible) return;
            long total = Native.PrivateWorkingSet(ownPid);
            foreach (int pid in WebViewPids()) if (pid != ownPid) total += Native.PrivateWorkingSet(pid);
            PostToUi(new { type = "mem", mb = Math.Round(total / 1048576.0) });
        }

        // ───────────────────────── window geometry ─────────────────────────

        int Border { get { return Math.Max(4, (int)Math.Round(5 * Native.Scale(Handle))); } }

        int ToPx(int css) { return (int)Math.Round(css * Native.Scale(Handle)); }

        int[] uiCut;          // the shield panel's x, width and corner radius, in the strip's device px
        string stripClip = "";

        /// <summary>
        /// The shield panel only needs a corner of the extra height. Clip the strip view to its own bar plus
        /// the panel, so the page beside the panel stays visible (a web view can't be see-through over another).
        /// </summary>
        void ClipStrip(int width, int chrome, int height)
        {
            if (ui == null || !ui.IsHandleCreated) return;
            bool clip = uiCut != null && height > chrome && uiCut[1] > 0;
            string key = clip ? width + ":" + chrome + ":" + height + ":" + uiCut[0] + ":" + uiCut[1] : "";
            if (key == stripClip) return;
            stripClip = key;
            if (!clip) { Native.SetWindowRgn(ui.Handle, IntPtr.Zero, true); return; }
            int r = Math.Max(2, uiCut[2]);
            IntPtr bar = Native.CreateRectRgn(0, 0, width, chrome);
            IntPtr panel = Native.CreateRoundRectRgn(uiCut[0], Math.Max(0, chrome - 2 * r), uiCut[0] + uiCut[1] + 1, height + 1, 2 * r, 2 * r);
            Native.CombineRgn(bar, bar, panel, Native.RGN_OR);
            Native.DeleteObject(panel);
            if (Native.SetWindowRgn(ui.Handle, bar, true) == 0) Native.DeleteObject(bar); // on success Windows owns it
        }
        int lastRoom = -1;

        void LayoutChildren()
        {
            if (!IsHandleCreated) return;
            int b = Border;
            var inner = new Rectangle(b, b, Math.Max(0, ClientSize.Width - 2 * b), Math.Max(0, ClientSize.Height - 2 * b));
            if (fullscreen)
            {
                content.Bounds = new Rectangle(Point.Empty, ClientSize);
                if (ui != null) SetViewVisible(ui, false);
                if (tabsView != null) tabsView.Visible = false;
                content.BringToFront();
                return;
            }
            if (inner.Height != lastRoom && uiReady) { lastRoom = inner.Height; PostToUi(new { type = "room", h = inner.Height }); }
            int chrome = Math.Min(inner.Height, chromePx > 0 ? chromePx : ToPx(80));
            int strip = Math.Min(inner.Height, Math.Max(chrome, uiPx));
            int bottom = tabsView != null ? Math.Min(tabsPx, inner.Height - chrome) : 0;
            content.Bounds = new Rectangle(inner.X, inner.Y + chrome, inner.Width, Math.Max(0, inner.Height - chrome - bottom));
            if (tabsView != null)
            {
                tabsView.Visible = bottom > 0;
                tabsView.Bounds = new Rectangle(inner.X, inner.Bottom - bottom, inner.Width, bottom);
            }
            if (glanceHost != null && glance != null)
            {
                glanceHost.Bounds = GlanceBounds(content.Bounds);
                glanceHost.BringToFront();
                if (glanceRail != null && glanceRail.Visible)
                {
                    var want = glanceRail.Want(Native.Scale(Handle));
                    int gx = glanceHost.Right + (content.Bounds.Right - glanceHost.Right - want.Width) / 2;
                    glanceRail.Bounds = new Rectangle(gx, glanceHost.Top, want.Width, Math.Min(want.Height, glanceHost.Height));
                    glanceRail.BringToFront();
                }
            }
            if (ui != null)
            {
                ui.Bounds = new Rectangle(inner.X, inner.Y, inner.Width, strip);
                ui.BringToFront(); // the command palette may hang over the page
                ClipStrip(inner.Width, chrome, strip);
            }
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
            PositionRaven();
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            PositionRaven(); // Raven's side tabs and chat move with the window
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(FrameLine))
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case Native.WM_HOTKEY:
                    if (m.WParam.ToInt32() == HotkeyId) OnHotkey();
                    else if (m.WParam.ToInt32() == GhostHotkeyId) ToggleGhost();
                    else if (m.WParam.ToInt32() == QuickHotkeyId) SummonQuick();
                    return;
                case Native.WM_NCHITTEST:
                    base.WndProc(ref m);
                    if (m.Result.ToInt32() == Native.HTCLIENT && !fullscreen) m.Result = (IntPtr)HitTestBorder(m.LParam);
                    return;
                case Native.WM_ENTERSIZEMOVE:
                    dragGuard.Stop();
                    base.WndProc(ref m);
                    return;
                case Native.WM_EXITSIZEMOVE:
                    base.WndProc(ref m);
                    dragging = false;
                    SnapToEdges();
                    SaveBounds();
                    return;
                case Native.WM_DPICHANGED:
                    base.WndProc(ref m);
                    var r = (RECT)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(RECT));
                    Bounds = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                    LayoutChildren();
                    return;
            }
            base.WndProc(ref m);
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        int HitTestBorder(IntPtr lParam)
        {
            long lp = lParam.ToInt64();
            var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
            int b = Border + 2, w = ClientSize.Width, h = ClientSize.Height;
            bool left = p.X < b, right = p.X >= w - b, top = p.Y < b, bottom = p.Y >= h - b;
            if (top && left) return Native.HTTOPLEFT;
            if (top && right) return Native.HTTOPRIGHT;
            if (bottom && left) return Native.HTBOTTOMLEFT;
            if (bottom && right) return Native.HTBOTTOMRIGHT;
            if (left) return Native.HTLEFT;
            if (right) return Native.HTRIGHT;
            if (top) return Native.HTTOP;
            if (bottom) return Native.HTBOTTOM;
            return Native.HTCLIENT;
        }

        /// <summary>
        /// Moving the window from the strip: hand the press to Windows as if it landed on a title bar,
        /// so Windows runs the move itself (no polling of the mouse). Snapping happens when it ends.
        /// </summary>
        void StartDrag()
        {
            dragging = true;
            dragGuard.Stop(); dragGuard.Start();
            Native.ReleaseCapture();
            var c = Cursor.Position;
            Native.PostMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, (IntPtr)((c.Y << 16) | (c.X & 0xFFFF)));
        }

        void SnapToEdges()
        {
            // off in Settings, or Shift held as you let go: leave it exactly where it was dropped
            if (!S.snapEdges || (Control.ModifierKeys & Keys.Shift) != 0) return;
            var wa = Screen.FromHandle(Handle).WorkingArea;
            int snap = ToPx(24);
            int x = Left, y = Top;
            if (Math.Abs(Left - wa.Left) < snap) x = wa.Left;
            else if (Math.Abs(Right - wa.Right) < snap) x = wa.Right - Width;
            if (Math.Abs(Top - wa.Top) < snap) y = wa.Top;
            else if (Math.Abs(Bottom - wa.Bottom) < snap) y = wa.Bottom - Height;
            if (x != Left || y != Top) Location = new Point(x, y);
        }

        void ApplyBounds()
        {
            boundsApplied = true;
            double scale = Native.Scale(Handle);
            var wa = Screen.PrimaryScreen.WorkingArea;
            var r = new Rectangle(S.windowX, S.windowY, S.windowWidth, S.windowHeight);
            bool valid = r.Width >= MinimumSize.Width && r.Height >= MinimumSize.Height;
            if (valid)
            {
                valid = false;
                foreach (var screen in Screen.AllScreens)
                    if (screen.WorkingArea.IntersectsWith(new Rectangle(r.X + 40, r.Y + 10, Math.Max(1, r.Width - 80), 30))) valid = true;
            }
            if (!valid)
            {
                int w = Math.Min((int)(520 * scale), (int)(wa.Width * 0.9));
                int h = Math.Min((int)(720 * scale), (int)(wa.Height * 0.9));
                int margin = (int)(16 * scale);
                r = new Rectangle(wa.Right - w - margin, wa.Top + margin, w, h);
            }
            Bounds = r;
        }

        void SaveBounds()
        {
            if (WindowState != FormWindowState.Normal) return;
            S.windowX = Left; S.windowY = Top; S.windowWidth = Width; S.windowHeight = Height;
            SaveSettingsSoon();
        }

        void SaveSettingsSoon() { saveTimer.Stop(); saveTimer.Start(); }

        void SetFullscreen(bool on)
        {
            if (fullscreen == on) return;
            fullscreen = on;
            if (!on && ui != null) SetViewVisible(ui, true);
            LayoutChildren();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!quitting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true; // Alt+F4 holsters; Quit lives in the tray menu
                Holster();
                return;
            }
            base.OnFormClosing(e);
        }

        // ───────────────────────── tray ─────────────────────────

        void SetupTray()
        {
            tray = new NotifyIcon
            {
                Icon = smallIcon ?? Icon ?? SystemIcons.Application,
                Text = "Sidearm · " + S.hotkey,
                ContextMenuStrip = BuildTrayMenu(),
                Visible = true
            };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) Draw(); };
        }

        /// <summary>The tray menu, in the interface language (rebuilt when the language changes).</summary>
        ContextMenuStrip BuildTrayMenu()
        {
            Func<string, string> L = Lang.T;
            var menu = new ContextMenuStrip();
            var draw = new ToolStripMenuItem(L("Draw  (" + S.hotkey + ")"), null, (s, e) => Draw()) { Font = new Font(menu.Font, FontStyle.Bold) };
            trayPin = new ToolStripMenuItem(L("Pin on screen"), null, (s, e) => SetPinned(!S.pinned)) { Checked = S.pinned };
            trayAutostart = new ToolStripMenuItem(L("Start with Windows"), null, (s, e) => ToggleAutostart()) { Checked = IsAutostart() };
            menu.Items.Add(draw);
            menu.Items.Add(trayPin);
            trayGhost = new ToolStripMenuItem(L("Ghost mode  (" + S.ghostHotkey + ")"), null, (s, e) => ToggleGhost()) { Checked = ghost };
            menu.Items.Add(trayGhost);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(trayAutostart);
            menu.Items.Add(new ToolStripMenuItem(L("Shortcuts on the desktop and in Start"), null, (s, e) => MakeShortcuts()));
            menu.Items.Add(new ToolStripMenuItem(L("Settings…"), null, (s, e) => OpenSettingsPage()));
            menu.Items.Add(new ToolStripMenuItem(L("Edit settings file"), null, (s, e) => OpenSettingsFile()));
            menu.Items.Add(new ToolStripMenuItem(L("Open data folder"), null, (s, e) =>
            {
                try { Process.Start("explorer.exe", "\"" + Program.DataDir + "\""); } catch (Exception) { }
            }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem(L("Quit Sidearm"), null, (s, e) => Quit()));
            return menu;
        }

        void ShowBalloon(string title, string text)
        {
            try { if (tray != null) tray.ShowBalloonTip(4000, Lang.T(title), Lang.T(text), ToolTipIcon.None); } catch (Exception) { }
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        bool IsAutostart()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue("Sidearm") != null;
            }
            catch (Exception) { return false; }
        }

        void ToggleAutostart()
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (IsAutostart()) k.DeleteValue("Sidearm", false);
                    else k.SetValue("Sidearm", "\"" + Application.ExecutablePath + "\" --tray");
                }
            }
            catch (Exception ex) { Log.Error("Autostart toggle failed", ex); }
            trayAutostart.Checked = IsAutostart();
        }

        void OpenSettingsFile()
        {
            S.Save();
            try { Process.Start("notepad.exe", "\"" + S.FilePath + "\""); }
            catch (Exception ex) { Log.Error("Could not open settings", ex); }
        }

        void Quit()
        {
            quitting = true;
            try
            {
                if (Visible) SaveBounds();
                S.Save();
                SaveSession();
                Native.UnregisterHotKey(Handle, HotkeyId);
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
                foreach (var t in tabs) if (t.View != null) t.View.Dispose();
                if (video != null && !video.IsDisposed) video.Close();
                ClosePops();
                FlushRaven();
                if (ravenTabs != null) ravenTabs.Dispose();
                if (ravenWin != null) ravenWin.Dispose();
                DropQuick();
                if (ui != null) ui.Dispose();
            }
            catch (Exception ex) { Log.Error("Quit cleanup", ex); }
            Close();
            Application.ExitThread();
        }
    }
}
