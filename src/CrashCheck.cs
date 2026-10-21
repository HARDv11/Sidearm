using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Sidearm
{
    /// <summary>
    /// 0.8.1: a crash inside the browser engine kills Sidearm before it can write a word. So Sidearm leaves a
    /// "running" note in data\ while it runs and removes it when it quits. If the note is still there at the next
    /// start, the last session didn't end normally: Sidearm then looks in Windows' Application log for its own
    /// crash report (the one Reliability Monitor shows) and copies the useful part into sidearm.log.
    /// </summary>
    internal static class CrashCheck
    {
        static string FlagPath { get { return Path.Combine(Program.DataDir, "running.txt"); } }

        public static void Start()
        {
            try
            {
                if (File.Exists(FlagPath))
                {
                    var parts = File.ReadAllText(FlagPath).Split('|');
                    DateTime started;
                    if (parts.Length < 2 || !DateTime.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out started))
                        started = File.GetLastWriteTimeUtc(FlagPath);
                    Log.Info("The last session (" + parts[0] + ", started " + started.ToLocalTime().ToString("dd.MM HH:mm") + ") ended without quitting");
                    var since = started;
                    new Thread(() => Report(since)) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
                }
                File.WriteAllText(FlagPath, Program.Version + "|" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            }
            catch (Exception ex) { Log.Error("Crash check", ex); }
            // Windows shutting down ends Sidearm without a goodbye too: that one isn't a crash
            try { Microsoft.Win32.SystemEvents.SessionEnding += (s, e) => { Log.Info("Windows is signing out or shutting down"); End(); }; } catch (Exception) { }
        }

        public static void End()
        {
            try { if (File.Exists(FlagPath)) File.Delete(FlagPath); } catch (Exception) { }
        }

        /// <summary>Newest first, back to when the last session started: Sidearm's own "Application Error" / ".NET Runtime" entries.</summary>
        static void Report(DateTime sinceUtc)
        {
            try
            {
                using (var log = new EventLog("Application"))
                {
                    var entries = log.Entries;
                    int found = 0;
                    for (int i = entries.Count - 1, seen = 0; i >= 0 && seen < 4000; i--, seen++)
                    {
                        EventLogEntry e;
                        try { e = entries[i]; } catch (Exception) { continue; }
                        if (e.TimeGenerated.ToUniversalTime() < sinceUtc.AddMinutes(-1)) break;
                        string line = Describe(e);
                        if (line == null) continue;
                        Log.Info("Windows' crash record, " + e.TimeGenerated.ToString("dd.MM HH:mm:ss") + ": " + line);
                        if (++found >= 3) break;
                    }
                    if (found == 0) Log.Info("No crash record in Windows' log for it (Windows shut down, the PC lost power, or it was ended from Task Manager)");
                }
            }
            catch (Exception ex) { Log.Info("Couldn't read Windows' Application log: " + ex.Message); }
        }

        static string Describe(EventLogEntry e)
        {
            var r = e.ReplacementStrings ?? new string[0];
            // Application Error 1000: app, app version, app stamp, module, module version, module stamp, code, offset, …
            if (e.Source == "Application Error" && r.Length >= 8 && r[0].Equals("Sidearm.exe", StringComparison.OrdinalIgnoreCase))
                return "Sidearm " + r[1] + " crashed in " + r[3] + " " + r[4] + ", exception 0x" + r[6].Replace("0x", "") + " at offset 0x" + r[7].Replace("0x", "");
            // .NET Runtime 1026: a managed exception nothing caught; the text names the app and has the stack
            if (e.Source == ".NET Runtime" && r.Length >= 1 && r[0].IndexOf("Sidearm.exe", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string t = r[0].Replace("\r", " ").Replace("\n", " ");
                return t.Length > 700 ? t.Substring(0, 700) + "…" : t;
            }
            return null;
        }
    }
}
