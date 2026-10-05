using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace MaterialBrowser
{
    // ─── Registry helpers (HKCU / HKLM, safe by design) ─────────────
    public static class Reg
    {
        public const string Personalize = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        public const string Dwm = @"SOFTWARE\Microsoft\Windows\DWM";
        public const string Explorer = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer";
        public const string Advanced = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        public const string StuckRects = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3";
        public const string ContentDelivery = @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        public const string Advertising = @"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo";
        public const string SearchSettings = @"SOFTWARE\Microsoft\Windows\CurrentVersion\SearchSettings";
        public const string Search = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Search";
        public const string Desktop = @"Control Panel\Desktop";
        public const string WindowMetrics = @"Control Panel\Desktop\WindowMetrics";
        public const string International = @"Control Panel\International";
        public const string Mouse = @"Control Panel\Mouse";
        public const string Keyboard = @"Control Panel\Keyboard";
        public const string GameConfigStore = @"System\GameConfigStore";
        public const string GameDvr = @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR";
        public const string GameBar = @"SOFTWARE\Microsoft\GameBar";
        public const string Narrator = @"SOFTWARE\Microsoft\Accessibility\Narrator";
        public const string Feedback = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Feedback\SiufRules";
        public const string CloudContent = @"SOFTWARE\Policies\Microsoft\Windows\CloudContent";
        public const string WindowsUpdate = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
        public const string ExplorerAdvancedSettings = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced\ExplorerAdvanced";

        static Microsoft.Win32.RegistryKey Open(RegistryHive hive, string path, bool writable)
        {
            try
            {
                RegistryKey root = hive == RegistryHive.LocalMachine ? Registry.LocalMachine : Registry.CurrentUser;
                return writable ? root.CreateSubKey(path) : root.OpenSubKey(path);
            }
            catch { return null; }
        }

        public static int GetDword(string path, string name, int fallback)
        {
            using (Microsoft.Win32.RegistryKey k = Open(RegistryHive.CurrentUser, path, false))
            {
                if (k == null) return fallback;
                object v = k.GetValue(name);
                if (v == null) return fallback;
                try { return Convert.ToInt32(v); } catch { return fallback; }
            }
        }

        public static int GetDwordMachine(string path, string name, int fallback)
        {
            using (Microsoft.Win32.RegistryKey k = Open(RegistryHive.LocalMachine, path, false))
            {
                if (k == null) return fallback;
                object v = k.GetValue(name);
                if (v == null) return fallback;
                try { return Convert.ToInt32(v); } catch { return fallback; }
            }
        }

        public static bool SetDword(string path, string name, int value)
        {
            using (Microsoft.Win32.RegistryKey k = Open(RegistryHive.CurrentUser, path, true))
            {
                if (k == null) return false;
                k.SetValue(name, value, RegistryValueKind.DWord);
                return true;
            }
        }

        public static string GetString(string path, string name, string fallback)
        {
            using (Microsoft.Win32.RegistryKey k = Open(RegistryHive.CurrentUser, path, false))
            {
                if (k == null) return fallback;
                object v = k.GetValue(name);
                return v == null ? fallback : v.ToString();
            }
        }

        public static string GetStringMachine(string path, string name, string fallback)
        {
            using (Microsoft.Win32.RegistryKey k = Open(RegistryHive.LocalMachine, path, false))
            {
                if (k == null) return fallback;
                object v = k.GetValue(name);
                return v == null ? fallback : v.ToString();
            }
        }

        public static bool SetString(string path, string name, string value)
        {
            using (Microsoft.Win32.RegistryKey k = Open(RegistryHive.CurrentUser, path, true))
            {
                if (k == null) return false;
                k.SetValue(name, value, RegistryValueKind.String);
                return true;
            }
        }

        public static byte[] GetBinary(string path, string name)
        {
            using (Microsoft.Win32.RegistryKey k = Open(RegistryHive.CurrentUser, path, false))
            {
                if (k == null) return null;
                return k.GetValue(name) as byte[];
            }
        }

        public static bool SetBinary(string path, string name, byte[] data)
        {
            using (Microsoft.Win32.RegistryKey k = Open(RegistryHive.CurrentUser, path, true))
            {
                if (k == null) return false;
                k.SetValue(name, data, RegistryValueKind.Binary);
                return true;
            }
        }

        public static bool KeyExists(string path)
        {
            using (Microsoft.Win32.RegistryKey k = Open(RegistryHive.CurrentUser, path, false))
                return k != null;
        }

        public static void Delete(string path, string name)
        {
            using (Microsoft.Win32.RegistryKey k = Open(RegistryHive.CurrentUser, path, true))
            {
                if (k != null) k.DeleteValue(name, false);
            }
        }

        public static bool ValueExists(string path, string name)
        {
            using (Microsoft.Win32.RegistryKey k = Open(RegistryHive.CurrentUser, path, false))
                return k != null && k.GetValue(name) != null;
        }
    }

    // ─── Win32 / DWM / Shell interop ─────────────────────────────────
    public static class Native
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindow(string cls, string win);

        [DllImport("user32.dll")]
        public static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [StructLayout(LayoutKind.Sequential)]
        public struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("dwmapi.dll")]
        public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        [DllImport("dwmapi.dll")]
        public static extern int DwmIsCompositionEnabled(out bool enabled);

        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS { public int Left, Right, Top, Bottom; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, int Msg, IntPtr wParam, string lParam,
            uint flags, uint timeout, out IntPtr result);

        [DllImport("user32.dll")]
        public static extern bool SystemParametersInfo(uint action, uint param, IntPtr value, uint winIni);

        [DllImport("user32.dll")]
        public static extern bool SystemParametersInfo(uint action, uint param, ref bool value, uint winIni);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        public static double TotalMemoryBytes()
        {
            try
            {
                var status = new MEMORYSTATUSEX();
                status.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                if (GlobalMemoryStatusEx(ref status)) return status.ullTotalPhys;
            }
            catch { }
            return 0;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        // ── child window hosting, used by the Gecko engine to keep a real
        //    browser window inside our own panels
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hwnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLength(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern IntPtr SetParent(IntPtr child, IntPtr parent);

        // ── keyboard hand-off to the embedded browser ────────────
        // The Gecko window is re-parented into our panel but belongs to the
        // browser's own thread, so it never becomes the keyboard focus window
        // of our queue. Sharing the input queues and setting focus there is
        // what makes typing reach the page.
        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint from, uint to, bool attach);

        [DllImport("user32.dll")]
        public static extern IntPtr SetFocus(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern IntPtr SetActiveWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetFocus();

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint threadId);

        [DllImport("user32.dll")]
        public static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X, Y;
        }

        public const int WH_MOUSE_LL = 14;
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202,
                      WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207,
                      WM_MOUSEWHEEL = 0x020A, WM_LBUTTONDBLCLK = 0x0203;
        public const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101,
                      WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;

        public const int VK_CONTROL = 0x11, VK_SHIFT = 0x10, VK_MENU = 0x12;
        public const uint MAPVK_VSC_TO_CHAR = 3;

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint code, uint mapType);

        

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode, scanCode, flags, time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr GetParent(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hwnd, int command);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        public static extern int SetWindowLong32(IntPtr hwnd, int index, IntPtr value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        public static extern int GetWindowLong32(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;

            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }

        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const int WS_CHILD = 0x40000000;
        public const int WS_CLIPSIBLINGS = 0x04000000;
        public const int WS_CLIPCHILDREN = 0x02000000;
        public const int WS_VISIBLE = 0x10000000;
        public const int SW_SHOW = 5, SW_HIDE = 0, SW_RESTORE = 9;
        public const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010,
                      SWP_FRAMECHANGED = 0x0020, SWP_SHOWWINDOW = 0x0040,
                      SWP_HIDEWINDOW = 0x0080, SWP_NOOWNERZORDER = 0x0200;
        public const int WM_CLOSE = 0x0010, WM_QUIT = 0x0012;

        /// <summary>SetWindowLongPtr on both word sizes.</summary>
        public static IntPtr SetWindowLongValue(IntPtr hwnd, int index, IntPtr value)
        {
            if (IntPtr.Size == 8) return SetWindowLongPtr(hwnd, index, value);
            return new IntPtr(SetWindowLong32(hwnd, index, value));
        }

        public static IntPtr GetWindowLongValue(IntPtr hwnd, int index)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr(hwnd, index)
                                    : new IntPtr(GetWindowLong32(hwnd, index));
        }

        /// <summary>Window rectangle of a foreign window, or an empty rect when it is gone.</summary>
        public static bool TryGetWindowRect(IntPtr hwnd, out RECT rect)
        {
            rect = new RECT();
            if (hwnd == IntPtr.Zero) return false;
            return GetWindowRect(hwnd, out rect);
        }

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);

        [DllImport("user32.dll")]
        public static extern bool IsWindows10OrGreater();

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int ShellExecute(IntPtr hwnd, string verb, string file, string parms, string dir, int show);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE dm);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int ChangeDisplaySettingsEx(string deviceName, ref DEVMODE dm, IntPtr hwnd, int flags, IntPtr param);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY;
            public int dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        public const uint SPIF_UPDATEINIFILE = 0x01;
        public const uint SPIF_SENDCHANGE = 0x02;
        public const uint SPI_SETCLIENTAREAANIMATION = 0x1043;
        public const uint SPI_SETANIMATION = 0x0049;
        public const uint SPI_GETANIMATION = 0x0048;
        public const uint SPI_SETBEEPS = 0x0057;
        public const uint SPI_GETBEEPS = 0x0056;
        public const uint SPI_SETMOUSESPEED = 0x00E1;
        public const uint SPI_GETMOUSESPEED = 0x00E0;
        public const uint SPI_SETDOUBLECLICKTIME = 0x0020;
        public const uint SPI_GETDOUBLECLICKTIME = 0x001E;
        public const uint SPI_SETSWAPBUTTON = 0x0024;
        public const uint SPI_GETSWAPBUTTON = 0x0023;
        public const uint SPI_SETWHEELSCROLLLINES = 0x0067;
        public const uint SPI_GETWHEELSCROLLLINES = 0x0068;
        public const uint SPI_SETKEYBOARDDELAY = 0x0037;
        public const uint SPI_SETKEYREPEAT = 0x0033;
        public const uint SPI_SETSTICKYKEYS = 0x003B;
        public const uint SPI_SETFILTERKEYS = 0x0032;
        public const uint SPI_SETDESKWALLPAPER = 0x0014;
        public const uint SPIF_SENDCHANGE_ALL = SPIF_SENDCHANGE;

        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        public const int DWMWA_CAPTION_COLOR = 35;
        public const int DWMWA_TEXT_COLOR = 36;

        public const int WCA_ACCENT_POLICY = 19;

        [StructLayout(LayoutKind.Sequential)]
        public struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        // AccentState values
        public const int ACCENT_ENABLE_GRADIENT = 0;
        public const int ACCENT_ENABLE_TRANSPARENTGRADIENT = 1;
        public const int ACCENT_ENABLE_BLURBEHIND = 3;
        public const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
        public const int ACCENT_ENABLE_HOSTBACKDROP = 5;
        public const int ACCENT_ENABLE_TRANSPARENT = 2;

        public const int DWMWA_CORNER_ROUND = 2;

        public static IntPtr HWND_BROADCAST = new IntPtr(0xffff);
        public const int WM_SETTINGCHANGE = 0x001A;
        public const int WM_SYSCOLORCHANGE = 0x0015;

        public static void BroadcastSettingChange()
        {
            IntPtr res;
            try
            {
                SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "Environment",
                    0x0002, 1500, out res);
                SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "Policy",
                    0x0002, 1500, out res);
                SendMessageTimeout(HWND_BROADCAST, WM_SYSCOLORCHANGE, IntPtr.Zero, null, 0x0002, 1500, out res);
            }
            catch { }
        }

        public static void SetImmersiveDarkMode(IntPtr hwnd, bool dark)
        {
            int value = dark ? 1 : 0;
            try { DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, 4); } catch { }
            try { DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref value, 4); } catch { }
        }

        /// <summary>Applies Mica (Win11 22H2+) or Acrylic (Win10 1803+) to a borderless window.</summary>
        public static bool ApplyBackdrop(IntPtr hwnd, bool dark, Color tint)
        {
            // Mica / Mica Alt first (Win11 22H2 and newer)
            int backdrop = dark ? 2 : 3;   // 2 = Mica Alt (darker tint), 3 = Mica
            if (DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, 4) == 0)
                return true;

            // Acrylic fallback for Windows 10 / 11 21H2
            try
            {
                int gradient = (tint.A << 24) | (tint.R << 16) | (tint.G << 8) | tint.B;
                var policy = new AccentPolicy
                {
                    AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
                    AccentFlags = 2,
                    GradientColor = gradient,
                    AnimationId = 0
                };
                int size = Marshal.SizeOf(typeof(AccentPolicy));
                IntPtr ptr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(policy, ptr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = ptr,
                    SizeOfData = size
                };
                bool ok = SetWindowCompositionAttribute(hwnd, ref data);
                Marshal.FreeHGlobal(ptr);
                return ok;
            }
            catch { return false; }
        }

        public static void SetCornerPreference(IntPtr hwnd, bool rounded)
        {
            try
            {
                int pref = rounded ? 2 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, 4);
            }
            catch { }
        }

        /// <summary>Rounded window region for Windows 10 (where DWM corners are not available).</summary>
        public static void ApplyRegionRounding(IntPtr hwnd, int width, int height, int radius)
        {
            if (radius <= 0) { SetWindowRgn(hwnd, IntPtr.Zero, true); return; }
            try
            {
                IntPtr rgn = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
                SetWindowRgn(hwnd, rgn, true);
            }
            catch { }
        }

        public static void OpenUri(string uri)
        {
            try { ShellExecute(IntPtr.Zero, "open", uri, null, null, 1); }
            catch { }
        }

        public static void OpenFile(string file)
        {
            try { ShellExecute(IntPtr.Zero, "open", file, null, null, 1); }
            catch { }
        }

        public static void RunSilent(string exe, string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(exe, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                System.Diagnostics.Process.Start(psi);
            }
            catch { }
        }
    }

    // ─── System information ─────────────────────────────────────────
    public class SysInfo
    {
        private static SysInfo _cached;

        public static SysInfo Cached
        {
            get
            {
                if (_cached == null) _cached = Read();
                return _cached;
            }
        }
        public string ProductName;
        public string DisplayVersion;
        public string Build;
        public string Edition;
        public string Owner;
        public string DeviceName;
        public string Cpu;
        public string Ram;
        public string DiskFree;
        public string DiskTotal;
        public string Battery;
        public bool OnBattery;
        public bool BatteryPresent;
        public bool IsWindows11;
        public bool IsServer;

        public static SysInfo Read()
        {
            var s = new SysInfo();
            const string nt = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            s.ProductName = Reg.GetStringMachine(nt, "ProductName", "Windows");
            s.DisplayVersion = Reg.GetStringMachine(nt, "DisplayVersion", "");
            s.Edition = Reg.GetStringMachine(nt, "EditionID", "");
            s.Owner = Reg.GetStringMachine(nt, "RegisteredOwner", Environment.UserName);
            int build = Reg.GetDwordMachine(nt, "CurrentBuild", 0);
            int ubr = Reg.GetDwordMachine(nt, "UBR", 0);
            s.Build = build + "." + ubr;
            s.IsWindows11 = build >= 22000;
            s.IsServer = s.ProductName.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0;
            if (s.DisplayVersion.Length > 0) s.ProductName = s.ProductName + " " + s.DisplayVersion;
            s.DeviceName = Environment.MachineName;

            s.Cpu = "—";
            s.Ram = "—";
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
                {
                    foreach (System.Management.ManagementObject o in searcher.Get())
                    {
                        string name = o["Name"] == null ? null : o["Name"].ToString();
                        if (!string.IsNullOrEmpty(name))
                        {
                            s.Cpu = name;
                            int idx = name.IndexOf(" @", StringComparison.Ordinal);
                            if (idx > 0) s.Cpu = name.Substring(0, idx);
                            break;
                        }
                    }
                }
                using (var cs = new System.Management.ManagementObject("Win32_ComputerSystem"))
                {
                    ulong mem = Convert.ToUInt64(cs["TotalPhysicalMemory"]);
                    s.Ram = (mem / (1024.0 * 1024 * 1024)).ToString("0.0") + " ГБ";
                }
                if (s.Ram == "—")
                {
                    double bytes = Native.TotalMemoryBytes();
                    if (bytes > 0) s.Ram = (bytes / (1024.0 * 1024 * 1024)).ToString("0.0") + " ГБ";
                }
            }
            catch { }

            try
            {
                var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
                var drive = new DriveInfo(root);
                s.DiskFree = (drive.AvailableFreeSpace / (1024.0 * 1024 * 1024)).ToString("0.0") + " ГБ";
                s.DiskTotal = (drive.TotalSize / (1024.0 * 1024 * 1024)).ToString("0.0") + " ГБ";
            }
            catch { s.DiskFree = "—"; s.DiskTotal = "—"; }

            try
            {
                System.Windows.Forms.PowerStatus ps = System.Windows.Forms.SystemInformation.PowerStatus;
                s.OnBattery = ps.PowerLineStatus.ToString() == "Offline";
                s.BatteryPresent = ps.BatteryChargeStatus.ToString() != "Unknown";
                s.Battery = (ps.BatteryLifePercent * 100).ToString("0") + "%";
            }
            catch { }

            return s;
        }
    }

    /// <summary>Battery and power line helpers (System.Windows.Forms.PowerStatus in a typed scope).</summary>
    public static class Power
    {
        static System.Windows.Forms.PowerStatus State()
        {
            return System.Windows.Forms.SystemInformation.PowerStatus;
        }

        public static bool OnBattery()
        {
            try { return State().PowerLineStatus.ToString() == "Offline"; }
            catch { return false; }
        }

        public static int Percent()
        {
            try { return (int)Math.Round(State().BatteryLifePercent * 100); }
            catch { return -1; }
        }

        public static bool Present()
        {
            try { return State().BatteryChargeStatus.ToString() != "Unknown"; }
            catch { return false; }
        }

        public static string LifeStatus()
        {
            try { return State().BatteryChargeStatus.ToString(); }
            catch { return "—"; }
        }

        public static string Source()
        {
            return OnBattery() ? "От батареи" : "От сети";
        }
    }

    // ─── Display modes ──────────────────────────────────────────────
    public class DisplayMode
    {
        public int Width, Height, Refresh;
        public string Text
        {
            get
            {
                return Width + " × " + Height + "  @  " + Refresh + " Гц";
            }
        }
    }

    public static class Displays
    {
        const int ENUM_CURRENT_SETTINGS = -1;
        const int DM_PELSWIDTH = 0x00080000;
        const int DM_PELSHEIGHT = 0x00100000;
        const int DM_DISPLAYFREQUENCY = 0x00400000;
        const int DM_BITSPERPEL = 0x00040000;
        const int ENUM_REGISTRY_SETTINGS = -2;
        const int CDS_UPDATEREGISTRY = 0x00000001;
        const int CDS_TEST = 0x00000002;

        public static List<DisplayMode> ListModes()
        {
            var list = new List<DisplayMode>();
            var dm = new Native.DEVMODE();
            dm.dmSize = (ushort)Marshal.SizeOf(typeof(Native.DEVMODE));
            int i = 0;
            while (Native.EnumDisplaySettings(null, i, ref dm) != 0)
            {
                if (dm.dmPelsWidth > 0 && dm.dmPelsHeight > 0 && dm.dmDisplayFrequency > 0)
                {
                    var m = new DisplayMode { Width = dm.dmPelsWidth, Height = dm.dmPelsHeight, Refresh = dm.dmDisplayFrequency };
                    if (!list.Contains(m)) list.Add(m);
                }
                i++;
            }
            list.Sort(delegate (DisplayMode a, DisplayMode b)
            {
                int c = b.Width.CompareTo(a.Width);
                if (c != 0) return c;
                c = b.Height.CompareTo(a.Height);
                if (c != 0) return c;
                return b.Refresh.CompareTo(a.Refresh);
            });
            return list;
        }

        public static DisplayMode Current()
        {
            var dm = new Native.DEVMODE();
            dm.dmSize = (ushort)Marshal.SizeOf(typeof(Native.DEVMODE));
            Native.EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm);
            return new DisplayMode { Width = dm.dmPelsWidth, Height = dm.dmPelsHeight, Refresh = dm.dmDisplayFrequency };
        }

        public static bool Apply(int width, int height, int refresh, out string error)
        {
            error = null;
            var dm = new Native.DEVMODE();
            dm.dmSize = (ushort)Marshal.SizeOf(typeof(Native.DEVMODE));
            dm.dmPelsWidth = width;
            dm.dmPelsHeight = height;
            dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_BITSPERPEL;
            dm.dmBitsPerPel = 32;
            if (refresh > 0)
            {
                dm.dmDisplayFrequency = refresh;
                dm.dmFields |= DM_DISPLAYFREQUENCY;
            }
            int test = Native.ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
            if (test != 0)
            {
                error = "Режим не поддерживается (код " + test + ")";
                return false;
            }
            int res = Native.ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
            if (res != 0)
            {
                error = "Не удалось применить (код " + res + ")";
                return false;
            }
            return true;
        }
    }
}