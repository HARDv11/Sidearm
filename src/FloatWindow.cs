using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace Sidearm
{
    /// <summary>
    /// 0.9: what the video pop-out and the page pop-out share. A small always-on-top tool window whose title-bar
    /// menu (right-click the title) has its own actions and the opacity levels, and which can go ghost
    /// (see-through, clicks pass through) together with Sidearm.
    /// </summary>
    internal class FloatWindow : Form
    {
        const int WM_SYSCOMMAND = 0x0112, MF_STRING = 0x0, MF_SEPARATOR = 0x800, MF_CHECKED = 0x8;
        const int ActionBase = 0x5A00, LevelBase = 0x5A80; // multiples of 16, as WM_SYSCOMMAND wants
        static readonly int[] Levels = { 100, 85, 70, 55 };

        [DllImport("user32.dll")] static extern IntPtr GetSystemMenu(IntPtr hwnd, bool revert);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenu(IntPtr menu, int flags, int id, string text);
        [DllImport("user32.dll")] static extern int CheckMenuItem(IntPtr menu, int id, int flags);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);

        readonly List<KeyValuePair<string, Action>> actions = new List<KeyValuePair<string, Action>>();
        double solid = 1.0;
        bool ghost;
        public double GhostOpacity = 0.6;
        public WebView2 View;

        public FloatWindow(string title, Rectangle bounds, Color back)
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
        }

        /// <summary>An item for the title bar's menu, above the opacity levels. Add them before the window is shown.</summary>
        protected void AddAction(string label, Action run) { actions.Add(new KeyValuePair<string, Action>(label, run)); }

        public bool Ghost { get { return ghost; } }

        /// <summary>See-through and click-through, like Sidearm's ghost mode (opacity first, then click-through).</summary>
        public void SetGhost(bool on)
        {
            if (on == ghost || IsDisposed) return;
            if (on)
            {
                solid = Opacity;
                Opacity = Math.Max(0.2, Math.Min(0.9, GhostOpacity));
                ghost = true;
                UpdateStyles();
            }
            else
            {
                ghost = false;
                Opacity = solid;
                UpdateStyles();
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                if (ghost) cp.ExStyle |= 0x00000020 | 0x00080000; // WS_EX_TRANSPARENT | WS_EX_LAYERED
                return cp;
            }
        }

        /// <summary>Shows it again after a holster without taking the focus from whatever you're in.</summary>
        public void ShowQuietly()
        {
            if (IsDisposed || Visible) return;
            ShowWindow(Handle, 8); // SW_SHOWNA
            Visible = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                IntPtr menu = GetSystemMenu(Handle, false);
                AppendMenu(menu, MF_SEPARATOR, 0, null);
                for (int i = 0; i < actions.Count; i++) AppendMenu(menu, MF_STRING, ActionBase + i * 16, Lang.T(actions[i].Key));
                if (actions.Count > 0) AppendMenu(menu, MF_SEPARATOR, 0, null);
                for (int i = 0; i < Levels.Length; i++)
                    AppendMenu(menu, MF_STRING | (i == 0 ? MF_CHECKED : 0), LevelBase + i * 16, Lang.T("Opacity " + Levels[i] + "%"));
            }
            catch (Exception) { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SYSCOMMAND)
            {
                int id = m.WParam.ToInt32() & 0xFFF0;
                for (int i = 0; i < actions.Count; i++)
                    if (id == ActionBase + i * 16) { var a = actions[i].Value; BeginInvoke(a); return; }
                for (int i = 0; i < Levels.Length; i++)
                    if (id == LevelBase + i * 16)
                    {
                        solid = Levels[i] / 100.0;
                        if (!ghost) Opacity = solid;
                        IntPtr menu = GetSystemMenu(Handle, false);
                        for (int j = 0; j < Levels.Length; j++) CheckMenuItem(menu, LevelBase + j * 16, j == i ? MF_CHECKED : 0);
                        return;
                    }
            }
            base.WndProc(ref m);
        }
    }
}
