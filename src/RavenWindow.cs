using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Sidearm
{
    /// <summary>
    /// 0.9: Raven's window beside Sidearm (ui\raven.html): a chat, the list of friends, adding a friend, picking who gets
    /// a page, and the small quick-reply card. One web view, made the first time it's needed, asleep 30 s after it
    /// hides and dropped with the tabs after a long holster.
    /// </summary>
    internal sealed class RavenWindow : Form
    {
        public WebView2 View;
        public bool Ready;
        public string Mode = "", Key;   // what it shows: chat (Key = the friend), list, add, review, pick, peek
        public event Action<Dictionary<string, object>> Message;
        public event Action Moved; // 0.9.5: dragged by its header and let go
        int pageHeight = -1;

        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);

        public RavenWindow(Color back)
        {
            Text = "Sidearm · Raven";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = back;
            KeyPreview = true;
            Size = new Size(320, 560);
            View = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = back };
            Controls.Add(View);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
                cp.ClassStyle |= Native.CS_DROPSHADOW;
                return cp;
            }
        }

        public async Task Init(CoreWebView2Environment env, double zoom)
        {
            var h = View.Handle;
            await View.EnsureCoreWebView2Async(env);
            var core = View.CoreWebView2;
            var s = core.Settings;
            s.AreDevToolsEnabled = false;
            s.AreDefaultContextMenusEnabled = true; // copy and paste in the message box
            s.AreBrowserAcceleratorKeysEnabled = false;
            s.IsZoomControlEnabled = false;
            s.IsStatusBarEnabled = false;
            s.IsGeneralAutofillEnabled = false;
            s.IsPasswordAutosaveEnabled = false;
            if (Math.Abs(zoom - 1.0) > 0.01) View.ZoomFactor = zoom;
            core.SetVirtualHostNameToFolderMapping(MainForm.Host, Program.UiDir, CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (sender, e) => { if (!e.Uri.StartsWith(MainForm.UiOrigin + "raven.html", StringComparison.OrdinalIgnoreCase)) e.Cancel = true; };
            core.NewWindowRequested += (sender, e) => e.Handled = true;
            core.WebMessageReceived += (sender, e) =>
            {
                if (!e.Source.StartsWith(MainForm.UiOrigin + "raven.html", StringComparison.OrdinalIgnoreCase)) return;
                var m = Json.Parse(e.WebMessageAsJson);
                if (m == null) return;
                string t = Json.Str(m, "t");
                if (t == "layout") { pageHeight = (int)Json.Num(m, "h", 0); if (Mode == "peek") FitPeek(); return; }
                if (t == "ready") Ready = true;
                if (t == "drag") { StartDrag(); return; }
                var handler = Message;
                if (handler != null) handler(m);
            };
            core.Navigate(MainForm.UiOrigin + "raven.html");
        }

        public void Post(object message)
        {
            try { if (View.CoreWebView2 != null) View.CoreWebView2.PostWebMessageAsJson(Json.Serialize(message)); }
            catch (Exception ex) { Log.Error("Raven window post", ex); }
        }

        Rectangle anchor;

        /// <summary>Shows it at <paramref name="where"/>. The quick-reply card doesn't take the focus from the bar or the page.</summary>
        public void ShowAt(Rectangle where, bool activate)
        {
            anchor = where;
            Bounds = Mode == "peek" && pageHeight > 0 ? new Rectangle(where.X, where.Y, where.Width, Math.Min(where.Height, pageHeight)) : where;
            if (!Visible)
            {
                if (activate) Show();
                else { ShowWindow(Handle, 8); Visible = true; } // SW_SHOWNA
            }
            if (activate)
            {
                Native.ForceForeground(Handle);
                Activate();
                try { View.Focus(); } catch (Exception) { }
            }
            MainForm.KickView(View, false);
        }

        bool dragging;

        /// <summary>0.9.5: the header hands the press to Windows as if it landed on a title bar; Windows runs the move.</summary>
        void StartDrag()
        {
            dragging = true;
            Native.ReleaseCapture();
            var c = Cursor.Position;
            Native.PostMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, (IntPtr)((c.Y << 16) | (c.X & 0xFFFF)));
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == Native.WM_EXITSIZEMOVE && dragging)
            {
                dragging = false;
                anchor = new Rectangle(Left, Top, anchor.Width, anchor.Height);
                var h = Moved; if (h != null) h();
            }
        }

        void FitPeek()
        {
            if (pageHeight <= 0 || !Visible) return;
            int h = Math.Min(anchor.Height > 0 ? anchor.Height : pageHeight, pageHeight);
            if (h != Height) Height = h;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Hide(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
