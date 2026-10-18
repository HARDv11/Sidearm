using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace Sidearm
{
    /// <summary>Light/dark theme, tabs at the bottom, Send to Zen.</summary>
    internal sealed partial class MainForm
    {
        // ───────────────────────── theme ─────────────────────────

        bool IsLight
        {
            get
            {
                if (S.theme == "light") return true;
                if (S.theme != "system") return false;
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    {
                        object v = k != null ? k.GetValue("AppsUseLightTheme") : null;
                        return v is int && (int)v == 1;
                    }
                }
                catch (Exception) { return false; }
            }
        }

        string ThemeName { get { return IsLight ? "light" : "dark"; } }
        Color FrameColor { get { return Tinted(IsLight ? Color.FromArgb(0xEB, 0xE8, 0xE0) : Color.FromArgb(0x11, 0x14, 0x16)); } }
        Color FrameLine { get { return IsLight ? Color.FromArgb(0xC4, 0xBD, 0xAE) : Color.FromArgb(0x2B, 0x31, 0x33); } }
        Color PageColor { get { return IsLight ? Color.FromArgb(0xF4, 0xF2, 0xEC) : Color.FromArgb(0x0D, 0x0F, 0x10); } }

        bool themeHooked;

        void ApplyTheme()
        {
            if (!themeHooked)
            {
                themeHooked = true;
                SystemEvents.UserPreferenceChanged += (s, e) =>
                {
                    if (e.Category == UserPreferenceCategory.General && S.theme == "system")
                        try { BeginInvoke(new Action(ApplyTheme)); } catch (Exception) { }
                };
            }
            BackColor = FrameColor;
            content.BackColor = PageColor;
            if (glanceHost != null) glanceHost.BackColor = FrameLine;
            try
            {
                if (ui != null) ui.DefaultBackgroundColor = FrameColor;
                if (tabsView != null) tabsView.DefaultBackgroundColor = FrameColor;
                foreach (var t in tabs) if (t.View != null) t.View.DefaultBackgroundColor = PageColor;
            }
            catch (Exception) { }
            ApplyAccentFrame();
            Invalidate();
            SendInit();
            string onAcc, acc = ReadableAccent(out onAcc);
            foreach (var t in tabs) PostToContent(t, new { t = "theme", value = ThemeName, accent = acc, onAccent = onAcc });
            if (glance != null) PostToContent(glance, new { t = "theme", value = ThemeName, accent = acc, onAccent = onAcc });
        }

        // ───────────────────────── tabs at the bottom ─────────────────────────

        WebView2 tabsView;
        bool tabsReady;
        int tabsPx;
        Task tabsViewTask;

        /// <summary>A slim second strip at the bottom of the window that shows only the tabs.</summary>
        async void ApplyTabsPosition()
        {
            try
            {
                if (S.tabsPosition == "bottom")
                {
                    if (tabsView != null || env == null) return;
                    if (tabsViewTask != null) { await tabsViewTask; return; }
                    tabsViewTask = CreateTabsView();
                    await tabsViewTask;
                    tabsViewTask = null;
                }
                else if (tabsView != null)
                {
                    var v = tabsView;
                    tabsView = null;
                    tabsReady = false;
                    tabsPx = 0;
                    Controls.Remove(v);
                    v.Dispose();
                }
                LayoutChildren();
            }
            catch (Exception ex) { Log.Error("Tabs position", ex); }
        }

        async Task CreateTabsView()
        {
            var v = new WebView2 { DefaultBackgroundColor = FrameColor };
            Controls.Add(v);
            v.KeyDown += OnViewKeyDown;
            var forceHandle = v.Handle;
            await v.EnsureCoreWebView2Async(env);
            if (Math.Abs(S.uiScale - 1.0) > 0.01) v.ZoomFactor = S.uiScale;
            ConfigureUi(v.CoreWebView2);
            tabsView = v;
            v.CoreWebView2.Navigate(UiOrigin + "chrome.html?view=tabs");
        }

        bool IsTabsViewSender(object sender)
        {
            return tabsView != null && tabsView.CoreWebView2 != null && sender == tabsView.CoreWebView2;
        }

        void PostToTabsView(string json)
        {
            if (!tabsReady || tabsView == null || tabsView.CoreWebView2 == null) return;
            try { tabsView.CoreWebView2.PostWebMessageAsJson(json); } catch (Exception) { }
        }

        /// <summary>What Ctrl+Enter sends: the Glance page if one is open, else the current page.</summary>
        string CurrentPageUrl()
        {
            if (glance != null && IsWebUrl(glance.Url)) return glance.Url;
            return active != null && !active.IsNewTab ? active.Url : null;
        }

        void GoHome()
        {
            CloseGlance();
            Navigate(NewTabUrl, false);
            FocusBar(true);
        }
    }
}
