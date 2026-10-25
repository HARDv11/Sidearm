using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace Sidearm
{
    /// <summary>
    /// A popped-out video: a small always-on-top window that stays up while Sidearm is holstered, so a guide
    /// or a stream keeps playing over the game. The title bar's menu (right-click it) sets its opacity.
    /// </summary>
    internal sealed class VideoWindow : FloatWindow
    {
        public event Action Closing2;

        public VideoWindow(string title, Rectangle bounds, Color back) : base(title, bounds, back)
        {
            View = new WebView2 { DefaultBackgroundColor = Color.Black };
            PlaceView(View);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            var h = Closing2;
            if (h != null) try { h(); } catch (Exception) { }
            base.OnFormClosed(e);
            try { View.Dispose(); } catch (Exception) { }
        }
    }
}
