using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace Sidearm
{
    /// <summary>
    /// A popped-out video: a small always-on-top window that stays up while Sidearm is holstered, so a guide
    /// or a stream keeps playing over the game. The title bar's menu (right-click it) sets its opacity.
    /// </summary>
    internal sealed class VideoWindow : Form
    {
        public WebView2 View;
        public event Action Closing2;

        const int WM_SYSCOMMAND = 0x0112, MF_STRING = 0x0, MF_SEPARATOR = 0x800, MF_CHECKED = 0x8;
        const int CmdBase = 0x5A10; // multiples of 16, as WM_SYSCOMMAND wants
        static readonly int[] Levels = { 100, 85, 70, 55 };

        [DllImport("user32.dll")] static extern IntPtr GetSystemMenu(IntPtr hwnd, bool revert);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenu(IntPtr menu, int flags, int id, string text);
        [DllImport("user32.dll")] static extern int CheckMenuItem(IntPtr menu, int id, int flags);

        public VideoWindow(string title, Rectangle bounds, Color back)
        {
            Text = "Sidearm · " + title;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = back;
            KeyPreview = true;
            MinimumSize = new Size(240, 160);
            Bounds = bounds;
            try { Icon = new Icon(System.IO.Path.Combine(Program.AppDir, "sidearm.ico"), SystemInformation.SmallIconSize); } catch (Exception) { }
            View = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
            Controls.Add(View);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                IntPtr menu = GetSystemMenu(Handle, false);
                AppendMenu(menu, MF_SEPARATOR, 0, null);
                for (int i = 0; i < Levels.Length; i++)
                    AppendMenu(menu, MF_STRING | (i == 0 ? MF_CHECKED : 0), CmdBase + i * 16, Lang.T("Opacity " + Levels[i] + "%"));
            }
            catch (Exception) { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SYSCOMMAND)
            {
                int id = m.WParam.ToInt32() & 0xFFF0;
                for (int i = 0; i < Levels.Length; i++)
                    if (id == CmdBase + i * 16)
                    {
                        Opacity = Levels[i] / 100.0;
                        IntPtr menu = GetSystemMenu(Handle, false);
                        for (int j = 0; j < Levels.Length; j++) CheckMenuItem(menu, CmdBase + j * 16, j == i ? MF_CHECKED : 0);
                        return;
                    }
            }
            base.WndProc(ref m);
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
