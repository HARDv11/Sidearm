using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Sidearm
{
    /// <summary>
    /// 0.8.1: Sidearm.lnk on the desktop and in the Start menu. Sidearm has no taskbar button of its own (it's a tool
    /// window that stays out of Alt+Tab), so a shortcut is the way to pin it: right-click the desktop one → Pin to
    /// taskbar, or pin it from Start. Clicking it while Sidearm runs simply draws it. Written with Windows' own
    /// IShellLink, the way installers do; only when you ask for it.
    /// </summary>
    internal static class Shortcuts
    {
        public const string Name = "Sidearm.lnk";

        public static string DesktopPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Name); } }
        public static string StartMenuPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Name); } }
        static string Exe { get { return Path.Combine(Program.AppDir, "Sidearm.exe"); } }

        /// <summary>"none", "ok" (points at this copy) or "elsewhere" (an older copy in another folder).</summary>
        public static string State(string path)
        {
            if (!File.Exists(path)) return "none";
            string target = Target(path);
            return target != null && string.Equals(Path.GetFullPath(target), Path.GetFullPath(Exe), StringComparison.OrdinalIgnoreCase) ? "ok" : "elsewhere";
        }

        /// <summary>Creates (or points to this copy) the desktop and Start menu shortcuts. Returns how many it wrote.</summary>
        public static int CreateBoth()
        {
            int n = 0;
            if (Create(DesktopPath)) n++;
            if (Create(StartMenuPath)) n++;
            Log.Info("Shortcuts: " + n + " written (desktop, Start menu) for " + Exe);
            return n;
        }

        public static int RemoveBoth()
        {
            int n = 0;
            foreach (var p in new[] { DesktopPath, StartMenuPath })
                try { if (File.Exists(p)) { File.Delete(p); n++; } } catch (Exception ex) { Log.Error("Removing " + p, ex); }
            Log.Info("Shortcuts: " + n + " removed");
            return n;
        }

        /// <summary>After an update into a new folder: shortcuts that point at the old copy follow this one.</summary>
        public static void FollowThisCopy()
        {
            foreach (var p in new[] { DesktopPath, StartMenuPath })
            {
                if (State(p) != "elsewhere") continue;
                string old = Target(p) ?? "";
                // only our own shortcuts: the old target was a Sidearm.exe too
                if (!Path.GetFileName(old).Equals("Sidearm.exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (Create(p)) Log.Info("Shortcut " + Path.GetFileName(Path.GetDirectoryName(p)) + "\\" + Name + " now points at this copy (was " + old + ")");
            }
        }

        static bool Create(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var link = (IShellLinkW)new ShellLink();
                link.SetPath(Exe);
                link.SetWorkingDirectory(Program.AppDir);
                link.SetDescription("Sidearm: the pocket browser you draw over a game");
                link.SetIconLocation(Exe, 0);
                ((IPersistFile)link).Save(path, true);
                Marshal.FinalReleaseComObject(link);
                return true;
            }
            catch (Exception ex) { Log.Error("Shortcut " + path, ex); return false; }
        }

        static string Target(string path)
        {
            try
            {
                var link = (IShellLinkW)new ShellLink();
                ((IPersistFile)link).Load(path, 0);
                var sb = new StringBuilder(1024);
                IntPtr fd = IntPtr.Zero;
                link.GetPath(sb, sb.Capacity, fd, 0);
                Marshal.FinalReleaseComObject(link);
                return sb.ToString();
            }
            catch (Exception) { return null; }
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        class ShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
        interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }
    }
}
