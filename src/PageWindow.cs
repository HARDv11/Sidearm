using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace Sidearm
{
    /// <summary>
    /// 0.9: a tab popped out into its own small window. The tab's own web view moves in, so the page doesn't reload
    /// (a map keeps its place, a video keeps playing). The title bar's menu takes it back to Sidearm.
    /// </summary>
    internal sealed class PageWindow : FloatWindow
    {
        internal Tab Tab;
        public event Action BackRequested;
        /// <summary>Raised while closing, before the window disposes anything: the shell takes the view out first.</summary>
        public event Action Closing2;
        public bool Returning; // closing because the page went back to Sidearm

        public PageWindow(string title, Rectangle bounds, Color back) : base(title, bounds, back)
        {
            AddAction("Back to Sidearm  (Alt+Shift+P)", () => { var h = BackRequested; if (h != null) h(); });
        }

        public void Host(WebView2 v)
        {
            View = v;
            v.Dock = DockStyle.Fill;
            Controls.Add(v);
        }

        /// <summary>Takes the view out again, before the window closes (so closing never disposes the page).</summary>
        public WebView2 Release()
        {
            var v = View;
            View = null;
            if (v != null && v.Parent == this) Controls.Remove(v);
            return v;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            var h = Closing2;
            if (h != null) try { h(); } catch (Exception ex) { Log.Error("Page pop-out closing", ex); }
            base.OnFormClosing(e);
        }
    }
}
