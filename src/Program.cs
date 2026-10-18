using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

[assembly: AssemblyTitle("Sidearm")]
[assembly: AssemblyProduct("Sidearm")]
[assembly: AssemblyDescription("Sidearm pocket browser: an always-on-top browser window you call up mid-game.")]
[assembly: AssemblyCompany("Sidearm project")]
[assembly: AssemblyCopyright("Copyright © 2026 Sidearm project")]
[assembly: AssemblyVersion("0.7.0.0")]
[assembly: AssemblyFileVersion("0.7.0.0")]
[assembly: AssemblyInformationalVersion("0.7.0")]

namespace Sidearm
{
    internal static class Program
    {
        public const string Version = "0.7.0 · Quickdraw";
        public const string Tagline = "Switching to your sidearm is faster than opening a browser.";

        public static string AppDir, DataDir, UiDir;

        [STAThread]
        private static int Main(string[] args)
        {
            AppDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');

            bool firstInstance;
            var mutex = new Mutex(true, @"Local\Sidearm.Instance", out firstInstance);
            if (!firstInstance)
            {
                // Already running: ask that instance to draw itself, then leave.
                try { using (var ev = EventWaitHandle.OpenExisting(@"Local\Sidearm.Draw")) ev.Set(); }
                catch (Exception) { }
                return 0;
            }

            Native.EnableDpiAwareness();
            DataDir = ResolveDataDir();
            Log.Init(Path.Combine(DataDir, "sidearm.log"));
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);
            Application.ThreadException += (s, e) => Log.Error("UI thread exception", e.Exception);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            UiDir = Path.Combine(AppDir, "ui");
            if (!File.Exists(Path.Combine(UiDir, "chrome.html")))
            {
                MessageBox.Show("The \"ui\" folder is missing next to Sidearm.exe.\nKeep the whole Sidearm folder together.", "Sidearm", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            try { CoreWebView2Environment.SetLoaderDllFolderPath(AppDir); }
            catch (Exception ex) { Log.Error("SetLoaderDllFolderPath failed", ex); }

            string runtime = null;
            try { runtime = CoreWebView2Environment.GetAvailableBrowserVersionString(); }
            catch (WebView2RuntimeNotFoundException) { }
            catch (Exception ex) { Log.Error("WebView2 runtime check failed", ex); }
            if (string.IsNullOrEmpty(runtime))
            {
                Log.Info("WebView2 runtime not found");
                var answer = MessageBox.Show(
                    "Sidearm runs on the Microsoft Edge WebView2 Runtime, which isn't installed on this PC.\n\nOpen the download page?",
                    "Sidearm", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (answer == DialogResult.Yes)
                    try { Process.Start("https://go.microsoft.com/fwlink/p/?LinkId=2124703"); } catch (Exception) { }
                return 1;
            }

            try { System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)(3072 | 12288); } // TLS 1.2 + 1.3
            catch (Exception) { try { System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072; } catch (Exception) { } }

            Log.Info("Sidearm " + Version + " starting. WebView2 runtime " + runtime + ". .NET " + Environment.Version + ". Data: " + DataDir);
            Migrate.OfferIfFirstRun(); // a new copy in a new folder: bring the old sign-ins and tabs along
            var settings = Settings.Load(Path.Combine(DataDir, "settings.json"));
            bool startInTray = Array.Exists(args, a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));

            using (var drawSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Sidearm.Draw"))
            {
                var form = new MainForm(settings, drawSignal);
                form.Boot(startInTray);
                Application.Run(new ApplicationContext());
            }
            GC.KeepAlive(mutex);
            Log.Info("Sidearm exited");
            return 0;
        }

        /// <summary>Portable by default: a "data" folder beside the exe. Falls back to %LOCALAPPDATA%\Sidearm.</summary>
        private static string ResolveDataDir()
        {
            string local = Path.Combine(AppDir, "data");
            try
            {
                Directory.CreateDirectory(local);
                string probe = Path.Combine(local, ".write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return local;
            }
            catch (Exception)
            {
                string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sidearm");
                Directory.CreateDirectory(fallback);
                return fallback;
            }
        }
    }
}
