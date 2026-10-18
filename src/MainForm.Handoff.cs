using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Sidearm
{
    /// <summary>Handing pages to a full browser (Zen, Chrome, Edge…) and questions to an AI.</summary>
    internal sealed partial class MainForm
    {
        // ───────────────────────── browsers ─────────────────────────

        sealed class BrowserKind
        {
            public string Id, Name, Exe, Private; // Private: the flag for a private window
            public bool Gecko;                    // Firefox family: -new-tab url
            public string[] Folders;              // usual install folders, relative to Program Files / Local AppData
        }

        static readonly BrowserKind[] Browsers =
        {
            new BrowserKind { Id = "zen", Name = "Zen", Exe = "zen.exe", Private = "-private-window", Gecko = true, Folders = new[] { "Zen Browser", @"Programs\Zen Browser" } },
            new BrowserKind { Id = "firefox", Name = "Firefox", Exe = "firefox.exe", Private = "-private-window", Gecko = true, Folders = new[] { "Mozilla Firefox" } },
            new BrowserKind { Id = "chrome", Name = "Chrome", Exe = "chrome.exe", Private = "--incognito", Folders = new[] { @"Google\Chrome\Application" } },
            new BrowserKind { Id = "edge", Name = "Edge", Exe = "msedge.exe", Private = "--inprivate", Folders = new[] { @"Microsoft\Edge\Application" } },
            new BrowserKind { Id = "brave", Name = "Brave", Exe = "brave.exe", Private = "--incognito", Folders = new[] { @"BraveSoftware\Brave-Browser\Application" } },
            new BrowserKind { Id = "opera", Name = "Opera", Exe = "opera.exe", Private = "--private", Folders = new[] { @"Programs\Opera", "Opera" } },
            new BrowserKind { Id = "vivaldi", Name = "Vivaldi", Exe = "vivaldi.exe", Private = "--incognito", Folders = new[] { @"Vivaldi\Application" } },
        };

        static BrowserKind KindById(string id) { return Browsers.FirstOrDefault(b => b.Id == id); }

        static BrowserKind KindByExe(string exe)
        {
            string name = Path.GetFileName(exe ?? "").ToLowerInvariant();
            if (name == "launcher.exe" && (exe ?? "").IndexOf("opera", StringComparison.OrdinalIgnoreCase) >= 0) return KindById("opera");
            if (name == "librewolf.exe" || name == "floorp.exe" || name == "waterfox.exe")
                return new BrowserKind { Id = "gecko", Name = Path.GetFileNameWithoutExtension(name), Exe = name, Private = "-private-window", Gecko = true };
            return Browsers.FirstOrDefault(b => b.Exe == name);
        }

        readonly Dictionary<string, string> exeCache = new Dictionary<string, string>();

        /// <summary>Where a browser is installed: App Paths, then Start-menu registration, then the usual folders.</summary>
        string FindExe(BrowserKind k)
        {
            string hit;
            if (exeCache.TryGetValue(k.Id, out hit) && hit != null && File.Exists(hit)) return hit;
            hit = null;
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                try
                {
                    using (var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + k.Exe))
                    {
                        var p = key != null ? key.GetValue("") as string : null;
                        if (p != null && File.Exists(p.Trim('"'))) { hit = p.Trim('"'); break; }
                    }
                    using (var clients = hive.OpenSubKey(@"Software\Clients\StartMenuInternet"))
                    {
                        if (clients == null) continue;
                        foreach (var name in clients.GetSubKeyNames())
                            using (var cmd = clients.OpenSubKey(name + @"\shell\open\command"))
                            {
                                string exe = ExeOf(cmd != null ? cmd.GetValue("") as string : null);
                                if (exe != null && Path.GetFileName(exe).Equals(k.Exe, StringComparison.OrdinalIgnoreCase) && File.Exists(exe)) { hit = exe; break; }
                            }
                    }
                    if (hit != null) break;
                }
                catch (Exception) { }
            }
            if (hit == null && k.Folders != null)
            {
                var roots = new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData };
                foreach (var root in roots)
                    foreach (var f in k.Folders)
                    {
                        string c = Path.Combine(Environment.GetFolderPath(root), f, k.Exe);
                        if (File.Exists(c)) { hit = c; break; }
                    }
            }
            exeCache[k.Id] = hit;
            return hit;
        }

        static string ExeOf(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine)) return null;
            commandLine = commandLine.Trim();
            if (commandLine.StartsWith("\""))
            {
                int end = commandLine.IndexOf('"', 1);
                return end > 1 ? commandLine.Substring(1, end - 1) : null;
            }
            int exeAt = commandLine.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return exeAt > 0 ? commandLine.Substring(0, exeAt + 4) : commandLine.Split(' ')[0];
        }

        /// <summary>The browser Windows opens links in, from the https association.</summary>
        static string DefaultBrowserExe()
        {
            try
            {
                string progId;
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice"))
                    progId = k != null ? k.GetValue("ProgId") as string : null;
                if (string.IsNullOrEmpty(progId)) return null;
                using (var cmd = Registry.ClassesRoot.OpenSubKey(progId + @"\shell\open\command"))
                {
                    string exe = ExeOf(cmd != null ? cmd.GetValue("") as string : null);
                    return exe != null && File.Exists(exe) ? exe : null;
                }
            }
            catch (Exception) { return null; }
        }

        /// <summary>Which browser Ctrl+Enter and friends use right now, and its exe (null exe = let Windows pick).</summary>
        BrowserKind TargetBrowser(out string exe)
        {
            exe = null;
            string choice = S.sendTo ?? "auto";
            if (!string.IsNullOrWhiteSpace(S.browserPath) && File.Exists(S.browserPath))
            {
                exe = S.browserPath;
                return KindById(choice) ?? KindByExe(exe) ?? new BrowserKind { Id = "custom", Name = Path.GetFileNameWithoutExtension(exe), Exe = Path.GetFileName(exe) };
            }
            if (choice == "auto")
            {
                var zen = KindById("zen");
                exe = FindExe(zen);
                if (exe != null) return zen;
                choice = "default";
            }
            if (choice == "default")
            {
                exe = DefaultBrowserExe();
                var k = exe != null ? KindByExe(exe) : null;
                return k ?? new BrowserKind { Id = "default", Name = "your browser" };
            }
            var kind = KindById(choice);
            if (kind == null) return new BrowserKind { Id = "default", Name = "your browser" };
            exe = FindExe(kind);
            return kind;
        }

        string BrowserName { get { string exe; return TargetBrowser(out exe).Name; } }

        void SendToBrowser(string url, bool privately)
        {
            if (string.IsNullOrEmpty(url) || !IsWebUrl(url) || url.StartsWith(UiOrigin, StringComparison.OrdinalIgnoreCase))
            { Toast("Nothing to send: this isn't a web page", null, null); return; }
            string exe;
            var kind = TargetBrowser(out exe);
            try
            {
                if (exe == null)
                {
                    Process.Start(url); // Windows' default, whatever it is
                    if (privately) Toast("Opened in your default browser; a private window needs a browser picked in Settings", null, null);
                    else if (S.sendTo != "default" && S.sendTo != "auto") Toast(kind.Name + " not found, so it opened in your default browser", null, null);
                    return;
                }
                string q = "\"" + url.Replace("\"", "%22") + "\"";
                string args = privately && kind.Private != null ? kind.Private + " " + q : (kind.Gecko ? "-new-tab " + q : q);
                if (StartBrowser(exe, args)) { Log.Info("Sent to " + kind.Name + (privately ? " (private)" : "") + ": " + url); return; }
                // that browser's install is broken ("side-by-side configuration is incorrect" and friends): use Windows' default
                Process.Start(url);
                Log.Info("Sent to the default browser instead of " + kind.Name + ": " + url);
                Toast(kind.Name + " wouldn't start, so it opened in your default browser. Reinstalling " + kind.Name + " usually fixes this", null, null);
            }
            catch (Exception ex)
            {
                Log.Error("Send to browser", ex);
                Toast("Couldn't open a browser: " + ex.Message, null, null);
            }
        }

        /// <summary>Starts a browser; on failure tries once more without the shell, which skips some broken file associations.</summary>
        static bool StartBrowser(string exe, string args)
        {
            try { Process.Start(exe, args); return true; }
            catch (Exception ex) { Log.Error("Starting " + exe, ex); }
            try
            {
                Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) ?? "" });
                return true;
            }
            catch (Exception ex) { Log.Error("Starting " + exe + " directly", ex); return false; }
        }

        // ───────────────────────── Ask AI ─────────────────────────

        sealed class AiKind { public string Id, Name, Url; } // Url: {q} is the question; null = no way to pre-fill it

        static readonly AiKind[] Ais =
        {
            new AiKind { Id = "claude", Name = "Claude", Url = "https://claude.ai/new?q={q}" },
            new AiKind { Id = "chatgpt", Name = "ChatGPT", Url = "https://chatgpt.com/?q={q}" },
            new AiKind { Id = "gemini", Name = "Gemini", Url = null },
            new AiKind { Id = "perplexity", Name = "Perplexity", Url = "https://www.perplexity.ai/search?q={q}" },
            new AiKind { Id = "copilot", Name = "Copilot", Url = "https://copilot.microsoft.com/?q={q}" },
        };

        AiKind Ai { get { return Ais.FirstOrDefault(a => a.Id == S.aiProvider) ?? Ais[0]; } }
        string AiName { get { return Ai.Name; } }

        const string GeminiUrl = "https://gemini.google.com/app";
        string pendingGeminiPrompt;

        /// <summary>Opens the chosen AI with the question typed in (Claude can go to the desktop app).</summary>
        void OpenAI(string prompt)
        {
            if (prompt.Length > 1800) prompt = prompt.Substring(0, 1800) + "…";
            var ai = Ai;
            if (ai.Url == null)
            {
                // Gemini takes no question in its link: copy it, open Gemini, and type it in when the page is ready
                try { System.Windows.Forms.Clipboard.SetText(prompt); } catch (Exception) { }
                if (S.claudeOpenIn == "browser") { SendToBrowser(GeminiUrl, false); Toast("Question copied: paste it into Gemini (Ctrl+V)", null, null); return; }
                if (S.claudeOpenIn == "app")
                {
                    string app = FindGeminiApp();
                    if (app != null)
                    {
                        // the Gemini app takes no question from outside yet: it's on the clipboard, Ctrl+V pastes it
                        try { Process.Start(app); Log.Info("Opened the Gemini app: " + app); Holster(); return; }
                        catch (Exception ex) { Log.Error("Gemini app", ex); }
                    }
                }
                pendingGeminiPrompt = prompt;
                Navigate(GeminiUrl, true);
                Toast("Opening Gemini; your question is also on the clipboard", null, null);
                return;
            }
            string url = ai.Url.Replace("{q}", Uri.EscapeDataString(prompt));
            if (ai.Id == "claude") { Navigate(url, true); return; } // Navigate sends claude.ai to the app when that's set
            if (S.claudeOpenIn == "browser") { SendToBrowser(url, false); Holster(); return; }
            Navigate(url, true);
        }

        string geminiApp;

        /// <summary>
        /// Google's Gemini desktop app (gemini.google/desktop): found by its Start-menu shortcut, which is how
        /// Windows itself launches it, or by the usual install folders.
        /// </summary>
        string FindGeminiApp()
        {
            if (geminiApp != null && File.Exists(geminiApp)) return geminiApp;
            geminiApp = null;
            var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) };
            foreach (var root in roots)
            {
                try
                {
                    if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                    foreach (var lnk in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                    {
                        string name = Path.GetFileNameWithoutExtension(lnk);
                        if (name.Equals("Gemini", StringComparison.OrdinalIgnoreCase) || name.Equals("Google Gemini", StringComparison.OrdinalIgnoreCase))
                            return geminiApp = lnk;
                    }
                }
                catch (Exception) { }
            }
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (var c in new[] { @"Programs\Gemini\Gemini.exe", @"Google\Gemini\Gemini.exe", @"Programs\Google Gemini\Gemini.exe" })
            {
                string p = Path.Combine(local, c);
                if (File.Exists(p)) return geminiApp = p;
            }
            return null;
        }

        /// <summary>Types the waiting question into Gemini's box once it exists (tries for a few seconds).</summary>
        async void FillGemini(Tab tab)
        {
            string prompt = pendingGeminiPrompt;
            if (prompt == null || tab.View == null || tab.View.CoreWebView2 == null || !(tab.Url ?? "").StartsWith("https://gemini.google.com/", StringComparison.Ordinal)) return;
            pendingGeminiPrompt = null;
            string js = "(function(t){var n=0;var iv=setInterval(function(){var e=document.querySelector('rich-textarea [contenteditable=true],div[contenteditable=true][role=textbox],textarea');" +
                "if(e){clearInterval(iv);e.focus();document.execCommand('insertText',false,t);}else if(++n>40)clearInterval(iv);},200);})(" + Json.Serialize(prompt) + ")";
            try { await tab.View.CoreWebView2.ExecuteScriptAsync(js); } catch (Exception ex) { Log.Error("Fill Gemini", ex); }
        }
    }
}
