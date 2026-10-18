using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Sidearm
{
    /// <summary>
    /// The quick bar: a small command bar that pops up where your mouse is (or mid-screen) with its own key.
    /// Sums, units, money and timers answer right in it; Enter opens the result in Sidearm.
    /// It hides as soon as it loses focus and gives focus back to whatever had it (usually the game).
    /// </summary>
    internal sealed class QuickBar : Form
    {
        public WebView2 View;
        public bool Ready;
        public event Action<System.Collections.Generic.Dictionary<string, object>> Message;
        IntPtr returnTo;
        int pageHeight = -1;
        bool keepFocus;

        public QuickBar(Color back)
        {
            Text = "Sidearm quick bar";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = back;
            Size = new Size(560, 64);
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
            s.AreDefaultContextMenusEnabled = false;
            s.AreBrowserAcceleratorKeysEnabled = false;
            s.IsZoomControlEnabled = false;
            s.IsStatusBarEnabled = false;
            s.IsGeneralAutofillEnabled = false;
            s.IsPasswordAutosaveEnabled = false;
            if (Math.Abs(zoom - 1.0) > 0.01) View.ZoomFactor = zoom;
            core.SetVirtualHostNameToFolderMapping(MainForm.Host, Program.UiDir, CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (sender, e) => { if (!e.Uri.StartsWith(MainForm.UiOrigin, StringComparison.OrdinalIgnoreCase)) e.Cancel = true; };
            core.NewWindowRequested += (sender, e) => e.Handled = true;
            core.WebMessageReceived += (sender, e) =>
            {
                if (!e.Source.StartsWith(MainForm.UiOrigin, StringComparison.OrdinalIgnoreCase)) return;
                var m = Json.Parse(e.WebMessageAsJson);
                if (m == null) return;
                if (Json.Str(m, "op") == "layout") { pageHeight = (int)Json.Num(m, "h", 0); FitHeight(); return; }
                if (Json.Str(m, "op") == "ready") Ready = true;
                var handler = Message;
                if (handler != null) handler(m);
            };
            core.Navigate(MainForm.UiOrigin + "quick.html");
        }

        public void Post(object message)
        {
            try { if (View.CoreWebView2 != null) View.CoreWebView2.PostWebMessageAsJson(Json.Serialize(message)); }
            catch (Exception ex) { Log.Error("Quick bar post", ex); }
        }

        /// <summary>Shows the bar with its input under the pointer ("mouse"), mid-screen ("center") or near the top ("top").</summary>
        public void Summon(string where)
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg != Handle) returnTo = fg;
            var cursor = Cursor.Position;
            var screen = where == "mouse" ? Screen.FromPoint(cursor) : Screen.FromHandle(returnTo != IntPtr.Zero ? returnTo : Handle);
            var wa = screen.WorkingArea;
            double scale = Native.Scale(Handle);
            int w = Math.Min((int)(560 * scale), wa.Width - 16);
            int bar = (int)(56 * scale), tall = (int)(380 * scale);
            int x, y;
            if (where == "mouse")
            {
                x = cursor.X - w / 2;
                y = cursor.Y - bar / 2;
                if (y + tall > wa.Bottom) y = Math.Max(wa.Top + 8, wa.Bottom - tall); // room for the answers below
            }
            else
            {
                x = wa.Left + (wa.Width - w) / 2;
                y = where == "top" ? wa.Top + (int)(wa.Height * 0.12) : wa.Top + (wa.Height - tall) / 2;
            }
            x = Math.Max(wa.Left + 8, Math.Min(x, wa.Right - w - 8));
            y = Math.Max(wa.Top + 8, y);
            Bounds = new Rectangle(x, y, w, pageHeight > 0 ? pageHeight : bar);
            if (!Visible) Show();
            Native.ForceForeground(Handle);
            Activate();
            try { View.Focus(); } catch (Exception) { }
            Post(new { type = "show" });
        }

        void FitHeight()
        {
            if (pageHeight <= 0) return;
            int h = Math.Max(24, pageHeight);
            if (h != Height) Height = h;
        }

        /// <summary>Hides the bar. Unless Sidearm itself is taking over, focus goes back to the game.</summary>
        public void Dismiss(bool toSidearm)
        {
            if (!Visible) return;
            keepFocus = toSidearm;
            Hide();
            if (!toSidearm && returnTo != IntPtr.Zero) Native.SetForegroundWindow(returnTo);
            keepFocus = false;
        }

        /// <summary>True while the foreground window is this bar or one of the engine's (its menus, its input).</summary>
        public Func<bool> ForegroundIsOurs = () => false;
        readonly Timer deactivate = new Timer { Interval = 120 };

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (keepFocus || !Visible) return;
            deactivate.Stop();
            deactivate.Tick -= OnDeactivateTick;
            deactivate.Tick += OnDeactivateTick;
            deactivate.Start();
        }

        void OnDeactivateTick(object sender, EventArgs e)
        {
            deactivate.Stop();
            // clicked somewhere else (the game, the desktop): just go, focus is already where you put it
            if (Visible && Native.GetForegroundWindow() != Handle && !ForegroundIsOurs()) Dismiss(true);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Dismiss(false); return; }
            base.OnFormClosing(e);
        }
    }
}
