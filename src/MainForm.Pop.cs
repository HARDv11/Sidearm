using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace Sidearm
{
    /// <summary>
    /// 0.9: pop out a page (Alt+Shift+P, the shield panel, the right-click menu). The tab leaves the strip and its
    /// own web view moves into a small always-on-top window, so nothing reloads. The ghost key turns the pop-outs
    /// see-through together with Sidearm, and Settings decides whether they hide when Sidearm holsters.
    /// </summary>
    internal sealed partial class MainForm
    {
        readonly List<PageWindow> pops = new List<PageWindow>();
        const int MaxPops = 3;

        IEnumerable<Tab> PoppedTabs { get { return pops.Select(p => p.Tab).Where(t => t != null); } }

        /// <summary>Any pop-out (page or video) on screen right now.</summary>
        bool AnyFloatShown()
        {
            return pops.Any(p => !p.IsDisposed && p.Visible) || (video != null && !video.IsDisposed && video.Visible);
        }

        IEnumerable<FloatWindow> Floats()
        {
            foreach (var p in pops) if (!p.IsDisposed) yield return p;
            if (video != null && !video.IsDisposed) yield return video;
        }

        async void PopOutPage(Tab tab)
        {
            try
            {
                if (tab == null || tab.IsGlance || tab.Pop != null || tab.View == null || !IsWebPage(tab)) { Toast("Pop out works on web pages", null, null); return; }
                pops.RemoveAll(p => p.IsDisposed);
                if (pops.Count >= MaxPops) { Toast("Up to " + MaxPops + " pages can be popped out at once", null, null); return; }
                var v = tab.View;
                int idx = tabs.IndexOf(tab);
                tabs.Remove(tab);
                StopSuspendTimer(tab);

                var w = new PageWindow(Trim(tab.Title, 60), PageBounds(), PageColor) { GhostOpacity = S.ghostOpacity };
                w.Tab = tab;
                tab.Pop = w;
                pops.Add(w);
                w.BackRequested += () => ReturnPop(w);
                StyleFloat(w);
                w.Closing2 += () => OnPopClosing(w);
                w.Move += (s, e) => RememberPopBounds(w);
                w.ResizeEnd += (s, e) => RememberPopBounds(w);

                v.KeyDown -= OnViewKeyDown;
                v.KeyDown += OnPopKeyDown;
                content.Controls.Remove(v);
                w.Host(v);
                var h = w.Handle;
                Native.StyleFrame(w.Handle, ToBgr(FrameLine), !IsLight);
                w.Show();
                RepairParent(v);
                SetViewVisible(v, true);
                Resume(v);
                SetMemoryLow(tab, false);
                KickView(v, false);
                if (ghost) w.SetGhost(true);
                RegisterGhostHotkey(true);

                if (active == tab)
                {
                    active = null;
                    if (tabs.Count == 0) await OpenTab(NewTabUrl, true, true);
                    else await ActivateTab(tabs[Math.Max(0, Math.Min(idx, tabs.Count - 1))], true);
                }
                PushTabs();
                SaveSessionSoon();
                Log.Info("Popped out a page: " + HostOf(tab.Url));
            }
            catch (Exception ex) { Log.Error("Pop out page", ex); Toast("Couldn't pop out this page", null, null); }
        }

        /// <summary>Back into Sidearm as a tab, still without a reload.</summary>
        async void ReturnPop(PageWindow w)
        {
            try
            {
                var tab = w.Tab;
                if (tab == null || tab.Closed) { w.Close(); return; }
                var v = w.Release();
                tab.Pop = null;
                w.Tab = null;
                pops.Remove(w);
                w.Returning = true;
                w.Close();
                if (v == null || v.IsDisposed) { tab.View = null; }
                else
                {
                    v.KeyDown -= OnPopKeyDown;
                    v.KeyDown += OnViewKeyDown;
                    v.Dock = DockStyle.Fill;
                    content.Controls.Add(v);
                    RepairParent(v);
                }
                if (tabs.Count >= S.maxTabs)
                {
                    var victim = tabs.Where(t => t != active).OrderBy(t => t.LastActive).FirstOrDefault();
                    if (victim != null) { Log.Info("Recycled a tab at the " + S.maxTabs + "-tab cap: " + HostOf(victim.Url)); RemoveTab(victim, true); }
                }
                int at = active != null ? tabs.IndexOf(active) + 1 : tabs.Count;
                tabs.Insert(Math.Max(0, Math.Min(at, tabs.Count)), tab);
                if (!Visible) await DrawAsync();
                await ActivateTab(tab, tab.View == null);
                if (!AnyFloatShown() && !Visible) RegisterGhostHotkey(false);
                Log.Info("Page back in Sidearm: " + HostOf(tab.Url));
            }
            catch (Exception ex) { Log.Error("Pop-out back to Sidearm", ex); }
        }

        /// <summary>The pop-out's × : the page closes like a tab (Ctrl+Shift+T brings it back).</summary>
        void OnPopClosing(PageWindow w)
        {
            if (w.Returning) return;
            pops.Remove(w);
            var tab = w.Tab;
            if (tab != null && !tab.Closed)
            {
                w.Release();
                tab.Pop = null;
                Remember(tab);
                tab.Closed = true;
                DisposeView(tab);
                SaveSessionSoon();
                Log.Info("Closed a popped-out page: " + HostOf(tab.Url));
            }
            BeginInvoke(new Action(() => { if (!Visible && !AnyFloatShown()) RegisterGhostHotkey(false); }));
        }

        /// <summary>Keys inside a pop-out: Ctrl+W closes it, Alt+Shift+P takes it back. The rest belongs to the page.</summary>
        void OnPopKeyDown(object sender, KeyEventArgs e)
        {
            var v = sender as WebView2;
            var w = pops.FirstOrDefault(p => p.View == v);
            if (w == null) return;
            Keys key = e.KeyCode;
            bool ctrl = e.Control, alt = e.Alt, shift = e.Shift;
            if (ctrl && !alt && key == Keys.W) { e.Handled = true; e.SuppressKeyPress = true; w.Close(); }
            else if (alt && shift && !ctrl && key == Keys.P) { e.Handled = true; e.SuppressKeyPress = true; ReturnPop(w); }
        }

        /// <summary>WinForms may give a moved control a new window handle: point the engine at it.</summary>
        static void RepairParent(WebView2 v)
        {
            var c = ControllerOf(v);
            if (c == null) return;
            try
            {
                if (c.ParentWindow != v.Handle) { c.ParentWindow = v.Handle; Log.Info("Pop-out: the view got a new window handle; moved the engine along"); }
                c.NotifyParentWindowPositionChanged();
            }
            catch (Exception ex) { Log.Error("Pop-out parent", ex); }
        }

        void RememberPopBounds(PageWindow w)
        {
            if (w.IsDisposed || w.WindowState != FormWindowState.Normal || !w.Visible) return;
            S.pageBounds = w.Left + "," + w.Top + "," + w.Width + "," + w.Height;
            SaveSettingsSoon();
        }

        /// <summary>One place for every page pop-out: where the last one was, else beside Sidearm.</summary>
        Rectangle PageBounds()
        {
            var parts = (S.pageBounds ?? "").Split(',');
            int x, y, wd, ht;
            Rectangle r;
            if (parts.Length == 4 && int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y) && int.TryParse(parts[2], out wd) && int.TryParse(parts[3], out ht)
                && wd >= 240 && ht >= 160 && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new Rectangle(x + 20, y + 5, Math.Max(1, wd - 40), 20))))
                r = new Rectangle(x, y, wd, ht);
            else
            {
                var wa = Screen.FromHandle(Handle).WorkingArea;
                double scale = Native.Scale(Handle);
                int w = (int)(520 * scale), h = (int)(420 * scale), margin = (int)(16 * scale);
                int left = Left - w - margin;
                if (left < wa.Left) left = Math.Min(wa.Right - w, Right + margin);
                r = new Rectangle(Math.Max(wa.Left, left), Math.Max(wa.Top, Top), w, h);
            }
            // a second and third pop-out step down a little, so they don't sit exactly on top of each other
            int open = pops.Count(p => !p.IsDisposed);
            int step = (int)(28 * Native.Scale(Handle));
            if (open > 0) r.Offset(step * open, step * open);
            return r;
        }

        /// <summary>Holster: pop-outs stay up, unless Settings says they go with Sidearm.</summary>
        void HideFloatsWithSidearm()
        {
            if (!S.popsHide) return;
            foreach (var f in Floats()) if (f.Visible) { f.Hide(); f.Tag = "hidden-with-sidearm"; }
        }

        void ShowFloatsWithSidearm()
        {
            foreach (var f in Floats())
                if ((f.Tag as string) == "hidden-with-sidearm")
                {
                    f.Tag = null;
                    f.ShowQuietly();
                    if (f.View != null) { SetViewVisible(f.View, true); Resume(f.View); KickView(f.View, false); }
                }
        }

        /// <summary>The ghost key with Sidearm holstered: only the pop-outs go see-through (or solid again).</summary>
        void ToggleFloatsGhost()
        {
            bool on = !Floats().Any(f => f.Visible && f.Ghost);
            foreach (var f in Floats()) if (f.Visible) { f.GhostOpacity = S.ghostOpacity; f.SetGhost(on); }
            if (on && gameWindow != IntPtr.Zero) Native.SetForegroundWindow(gameWindow);
            Log.Info(on ? "Ghost mode on (pop-outs)" : "Ghost mode off (pop-outs)");
        }

        /// <summary>0.9.5: the pop-out's strip in Sidearm's colors; its ghost button ghosts every pop-out.</summary>
        void StyleFloat(FloatWindow w)
        {
            w.Theme(FrameColor, FrameLine, IsLight ? Color.FromArgb(0x1D, 0x20, 0x23) : Color.FromArgb(0xE2, 0xE4, 0xDE),
                IsLight ? Color.FromArgb(0x5B, 0x60, 0x5C) : Color.FromArgb(0x97, 0x9E, 0x98), AccentColor);
            w.GhostRequested -= OnFloatGhostButton;
            w.GhostRequested += OnFloatGhostButton;
        }

        void OnFloatGhostButton()
        {
            bool on = !Floats().Any(f => f.Visible && f.Ghost);
            if (on) RegisterGhostHotkey(true); // the way back: a ghost can't be clicked
            ToggleFloatsGhost();
            if (on && !ghostKeyOk) { SetFloatsGhost(false); Toast(S.ghostHotkey + " is taken by another app, so ghost mode would have no way back", null, null); }
            else if (on) Toast("Ghost: " + S.ghostHotkey + " brings the pop-outs back", null, null);
        }

        void RestyleFloats() { foreach (var f in Floats()) StyleFloat(f); }

        void SetFloatsGhost(bool on)
        {
            foreach (var f in Floats()) { f.GhostOpacity = S.ghostOpacity; f.SetGhost(on); }
        }

        void ClosePops()
        {
            foreach (var p in pops.ToList()) try { p.Close(); } catch (Exception) { }
            pops.Clear();
        }
    }
}
