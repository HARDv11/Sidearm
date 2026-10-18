using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Sidearm
{
    /// <summary>
    /// 0.8: making sure the strip actually paints after a draw. With hardware acceleration off (and after a warm
    /// start) the engine could keep an empty strip until something forced a layout. Now the window is shown first,
    /// the views are told it moved, and the strip answers "painted" from an animation frame; no answer means kick it.
    /// </summary>
    internal sealed partial class MainForm
    {
        readonly Timer paintCheck = new Timer { Interval = 350 };
        int paintTries;

        void InitPaintCheck()
        {
            paintCheck.Tick += (s, e) =>
            {
                paintCheck.Stop();
                if (!Visible || ui == null) return;
                paintTries++;
                KickView(ui, true);
                if (active != null && active.View != null) KickView(active.View, false);
                Log.Info(paintTries == 1 ? "Strip didn't paint after draw; kicked it" : "Strip still didn't paint; kicked it again");
                if (paintTries < 3) paintCheck.Start();
            };
        }

        /// <summary>Right after a draw: tell every visible view where its window is, then wait for the strip's "painted".</summary>
        void NudgeAfterShow()
        {
            KickView(ui, false);
            KickView(tabsView, false);
            if (active != null) KickView(active.View, false);
            if (glance != null) KickView(glance.View, false);
            paintTries = 0;
            paintCheck.Stop();
            if (uiReady) paintCheck.Start();
        }

        void OnStripPainted()
        {
            paintCheck.Stop();
            if (paintTries > 0) Log.Info("Strip painted after " + paintTries + (paintTries == 1 ? " kick" : " kicks"));
        }

        internal static CoreWebView2Controller ControllerOf(WebView2 v)
        {
            try { return v != null && ControllerField != null ? ControllerField.GetValue(v) as CoreWebView2Controller : null; }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Wakes a view's compositor. Soft: "your window moved". Hard: also flick its visibility and its size by a
        /// pixel and back, which is what a manual resize did when the strip came up blank.
        /// </summary>
        internal static void KickView(WebView2 v, bool hard)
        {
            if (v == null || v.IsDisposed) return;
            var c = ControllerOf(v);
            if (c == null) return;
            try { c.NotifyParentWindowPositionChanged(); } catch (Exception) { }
            if (!hard) return;
            try
            {
                c.IsVisible = false;
                c.IsVisible = true;
                var b = c.Bounds;
                c.Bounds = new Rectangle(b.X, b.Y, b.Width, b.Height + 1);
                v.BeginInvoke(new Action(() =>
                {
                    try { if (!v.IsDisposed) { c.Bounds = b; c.NotifyParentWindowPositionChanged(); } } catch (Exception) { }
                }));
            }
            catch (Exception ex) { Log.Error("KickView", ex); }
        }
    }
}
