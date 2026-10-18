using System;
using System.Runtime.InteropServices;

namespace Sidearm
{
    /// <summary>Win32 calls the WinForms layer doesn't expose.</summary>
    internal static class Native
    {
        public const int WM_HOTKEY = 0x0312;
        public const int WM_NCHITTEST = 0x0084;
        public const int WM_EXITSIZEMOVE = 0x0232;
        public const int WM_DPICHANGED = 0x02E0;

        public const int HTCLIENT = 1, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
            HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;


        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int CS_DROPSHADOW = 0x00020000;

        [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
        [DllImport("gdi32.dll")] public static extern int CombineRgn(IntPtr dest, IntPtr a, IntPtr b, int mode);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr hwnd, IntPtr rgn, bool redraw);
        public const int RGN_OR = 2;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        public const int WM_NCLBUTTONDOWN = 0x00A1, HTCAPTION = 2, WM_ENTERSIZEMOVE = 0x0231;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        public const uint GA_ROOTOWNER = 3;

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo")]
        private static extern bool GetProcessMemoryInfo(IntPtr process, ref PROCESS_MEMORY_COUNTERS_EX2 counters, uint cb);

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_MEMORY_COUNTERS_EX2
        {
            public uint cb;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
            public UIntPtr PrivateUsage;
            public UIntPtr PrivateWorkingSetSize;
            public ulong SharedCommitUsage;
        }

        public static void EnableDpiAwareness()
        {
            try
            {
                // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4 (Windows 10 1703+)
                if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return;
            }
            catch (Exception) { }
            try { SetProcessDPIAware(); } catch (Exception) { }
        }

        public static double Scale(IntPtr hwnd)
        {
            try
            {
                uint dpi = GetDpiForWindow(hwnd);
                if (dpi > 0) return dpi / 96.0;
            }
            catch (Exception) { }
            return 1.0;
        }

        /// <summary>Windows 11: rounded corners and a tinted 1px frame. Silently ignored elsewhere.</summary>
        public static void StyleFrame(IntPtr hwnd, int borderColorBgr, bool darkMode)
        {
            try
            {
                int round = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, 33, ref round, 4);
                int color = borderColorBgr;
                DwmSetWindowAttribute(hwnd, 34, ref color, 4);
                int dark = darkMode ? 1 : 0;
                DwmSetWindowAttribute(hwnd, 20, ref dark, 4); // immersive dark mode
            }
            catch (Exception) { }
        }

        /// <summary>Private working set in bytes: the number Task Manager shows as "Memory".</summary>
        public static long PrivateWorkingSet(int pid)
        {
            const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, PROCESS_VM_READ = 0x0010;
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, false, pid);
            if (h == IntPtr.Zero) h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return 0;
            try
            {
                var c = new PROCESS_MEMORY_COUNTERS_EX2();
                c.cb = (uint)Marshal.SizeOf(typeof(PROCESS_MEMORY_COUNTERS_EX2));
                if (GetProcessMemoryInfo(h, ref c, c.cb))
                {
                    ulong v = c.PrivateWorkingSetSize.ToUInt64();
                    if (v == 0) v = c.WorkingSetSize.ToUInt64();
                    return (long)v;
                }
                return 0;
            }
            catch (Exception) { return 0; }
            finally { CloseHandle(h); }
        }

        /// <summary>
        /// Windows only lets the foreground app hand focus away. A hotkey press usually grants us
        /// that right; if not, briefly join the foreground thread's input queue.
        /// </summary>
        public static void ForceForeground(IntPtr hwnd)
        {
            if (GetForegroundWindow() == hwnd) return;
            SetForegroundWindow(hwnd);
            if (GetForegroundWindow() == hwnd) return;
            uint pid;
            uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            uint me = GetCurrentThreadId();
            if (fgThread != 0 && fgThread != me)
            {
                AttachThreadInput(me, fgThread, true);
                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
                AttachThreadInput(me, fgThread, false);
            }
        }

        public static int ForegroundProcessId()
        {
            uint pid;
            GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            return (int)pid;
        }

        /// <summary>Parses "Alt+`", "Ctrl+Shift+Space", "Alt+F1" into RegisterHotKey arguments.</summary>
        public static bool ParseHotkey(string text, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                // "Alt++" style: an empty part means the key itself is '+'
                if (p.Length == 0) { if (i == parts.Length - 1) vk = 0xBB; continue; }
                string l = p.ToLowerInvariant();
                if (i < parts.Length - 1)
                {
                    if (l == "alt") mods |= MOD_ALT;
                    else if (l == "ctrl" || l == "control") mods |= MOD_CONTROL;
                    else if (l == "shift") mods |= MOD_SHIFT;
                    else if (l == "win" || l == "windows") mods |= MOD_WIN;
                    else return false;
                    continue;
                }
                if (l == "`" || l == "~" || l == "backtick" || l == "tilde") vk = 0xC0;
                else if (l == "space") vk = 0x20;
                else if (l == "tab") vk = 0x09;
                else if (l.Length == 1 && l[0] >= 'a' && l[0] <= 'z') vk = (uint)char.ToUpperInvariant(l[0]);
                else if (l.Length == 1 && l[0] >= '0' && l[0] <= '9') vk = l[0];
                else if (l.StartsWith("f") && l.Length <= 3)
                {
                    int n;
                    if (int.TryParse(l.Substring(1), out n) && n >= 1 && n <= 24) vk = (uint)(0x70 + n - 1);
                }
            }
            return vk != 0;
        }
    }
}
